using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.ExceptionServices;
using System.Threading;
using System.Threading.Tasks;
using LiteDB;

namespace FileExplorer.Persistence
{
    public class Repository
    {
        private const int MaxFolderSortStateCount = 1024;

        public LiteDatabase Database { get; private set; }

        public PersistentCollection<MenuItem> MenuItems { get; private set; }

        public PersistentCollection<ToolLocationOverride> ToolLocationOverrides { get; private set; }

        public PersistentCollection<Expression> Expressions { get; private set; }

        public PersistentCollection<FolderLayout> FolderLayouts { get; private set; }

        public PersistentCollection<ExtensionMetadata> Extensions { get; private set; }

        public Repository(string databaseName)
        {
            string connectionString = $"Filename={Path.Combine(AppDomain.CurrentDomain.BaseDirectory, databaseName)}; Upgrade=true";
            Database = new LiteDatabase(connectionString);

            MenuItems = new PersistentCollection<MenuItem>(Database, "MenuItems");
            ToolLocationOverrides = new PersistentCollection<ToolLocationOverride>(Database, "ToolLocationOverrides");
            Expressions = new PersistentCollection<Expression>(Database, "Expressions");
            FolderLayouts = new PersistentCollection<FolderLayout>(Database, "FolderLayouts");
            Extensions = new PersistentCollection<ExtensionMetadata>(Database, "Extensions");

            folderSortStateRepository = Database.GetCollection<FolderSortState>("FolderSortStates");
            folderSortStateRepository.EnsureIndex(x => x.FolderPathKey);
            folderSortStateRepository.EnsureIndex(x => x.UpdatedAtUtc);

            folderSortStateWriterTask = Task.Run(ProcessFolderSortStateSaveQueueAsync);
        }

        public FolderSortState GetFolderSortState(string folderPath)
        {
            ThrowIfFolderSortStateWriterFailed();

            if (String.IsNullOrWhiteSpace(folderPath))
                return null;

            string folderPathKey = FolderSortState.GetFolderPathKey(folderPath);
            if (pendingFolderSortStates.TryGetValue(folderPathKey, out FolderSortState pendingSortState))
                return CloneFolderSortState(pendingSortState);

            FolderSortState sortState;
            lock (folderSortStateRepositorySyncRoot)
            {
                sortState = FindFolderSortState(folderPath, folderPathKey);
                if (sortState == null)
                    return null;

                if (EnsureFolderSortStateMetadata(sortState))
                    folderSortStateRepository.Update(sortState);
            }

            if (sortState == null)
                return null;

            return CloneFolderSortState(sortState);
        }

        public void SaveFolderSortState(FolderSortState sortState)
        {
            ThrowIfFolderSortStateWriterFailed();

            FolderSortState snapshot = CreateFolderSortStateSnapshot(sortState);
            if (snapshot == null)
                return;

            pendingFolderSortStates.TryRemove(snapshot.FolderPathKey, out _);
            SaveFolderSortStateCore(snapshot);
        }

        public void QueueFolderSortStateSave(FolderSortState sortState)
        {
            ThrowIfFolderSortStateWriterFailed();

            FolderSortState snapshot = CreateFolderSortStateSnapshot(sortState);
            if (snapshot == null)
                return;

            pendingFolderSortStates.AddOrUpdate(snapshot.FolderPathKey, snapshot, (key, existing) => snapshot);
            folderSortStateWriterSignal.Release();
        }

        public void Shutdown()
        {
            if (isShutdown)
                return;

            isShutdown = true;
            folderSortStateWriterSignal.Release();

            try
            {
                folderSortStateWriterTask.GetAwaiter().GetResult();
                ThrowIfFolderSortStateWriterFailed();
            }
            finally
            {
                folderSortStateWriterSignal.Dispose();
                Database?.Dispose();
                Database = null;
            }
        }

        private FolderSortState FindFolderSortState(string folderPath, string folderPathKey)
        {
            FolderSortState sortState = folderSortStateRepository.FindOne(Query.EQ(nameof(FolderSortState.FolderPathKey), folderPathKey));
            if (sortState != null)
                return sortState;

            return folderSortStateRepository.FindOne(Query.EQ(nameof(FolderSortState.FolderPath), folderPath));
        }

        private bool EnsureFolderSortStateMetadata(FolderSortState sortState)
        {
            bool changed = false;
            string folderPathKey = FolderSortState.GetFolderPathKey(sortState.FolderPath);

            if (!String.Equals(sortState.FolderPathKey, folderPathKey, StringComparison.Ordinal))
            {
                sortState.FolderPathKey = folderPathKey;
                changed = true;
            }

            if (sortState.Columns == null)
            {
                sortState.Columns = new List<FolderSortColumnState>();
                changed = true;
            }

            if (sortState.UpdatedAtUtc == default(DateTime))
            {
                sortState.UpdatedAtUtc = DateTime.UtcNow;
                changed = true;
            }

            return changed;
        }

        private void PrepareFolderSortStateForSave(FolderSortState sortState)
        {
            sortState.FolderPathKey = FolderSortState.GetFolderPathKey(sortState.FolderPath);
            sortState.Columns = sortState.Columns ?? new List<FolderSortColumnState>();
            sortState.UpdatedAtUtc = DateTime.UtcNow;
        }

        private async Task ProcessFolderSortStateSaveQueueAsync()
        {
            try
            {
                while (true)
                {
                    await folderSortStateWriterSignal.WaitAsync().ConfigureAwait(false);

                    foreach (FolderSortState pendingSortState in DequeuePendingFolderSortStates())
                        SaveFolderSortStateCore(pendingSortState);

                    if (isShutdown && pendingFolderSortStates.IsEmpty)
                        return;
                }
            }
            catch (Exception ex)
            {
                Interlocked.CompareExchange(ref folderSortStateWriterFailure, ExceptionDispatchInfo.Capture(ex), null);
            }
        }

        private FolderSortState[] DequeuePendingFolderSortStates()
        {
            List<FolderSortState> drainedSortStates = new List<FolderSortState>();

            foreach (KeyValuePair<string, FolderSortState> entry in pendingFolderSortStates.ToArray())
            {
                if (pendingFolderSortStates.TryRemove(entry.Key, out FolderSortState sortState))
                    drainedSortStates.Add(sortState);
            }

            return drainedSortStates.ToArray();
        }

        private FolderSortState CreateFolderSortStateSnapshot(FolderSortState sortState)
        {
            if (String.IsNullOrWhiteSpace(sortState?.FolderPath))
                return null;

            FolderSortState snapshot = CloneFolderSortState(sortState);
            PrepareFolderSortStateForSave(snapshot);
            return snapshot;
        }

        private FolderSortState CloneFolderSortState(FolderSortState sortState)
        {
            return new FolderSortState
            {
                Id = sortState.Id,
                FolderPath = sortState.FolderPath,
                FolderPathKey = sortState.FolderPathKey,
                UpdatedAtUtc = sortState.UpdatedAtUtc,
                Columns = (sortState.Columns ?? new List<FolderSortColumnState>())
                    .Select(x => new FolderSortColumnState { FieldName = x.FieldName, SortOrder = x.SortOrder })
                    .ToList()
            };
        }

        private void SaveFolderSortStateCore(FolderSortState sortState)
        {
            lock (folderSortStateRepositorySyncRoot)
            {
                FolderSortState existingSortState = FindFolderSortState(sortState.FolderPath, sortState.FolderPathKey);
                if (existingSortState != null)
                    sortState.Id = existingSortState.Id;

                folderSortStateRepository.Upsert(sortState);
                TrimFolderSortStates();
            }
        }

        private void ThrowIfFolderSortStateWriterFailed()
        {
            folderSortStateWriterFailure?.Throw();
        }

        private void TrimFolderSortStates()
        {
            int overflow = folderSortStateRepository.Count() - MaxFolderSortStateCount;
            if (overflow <= 0)
                return;

            FolderSortState[] statesToRemove = folderSortStateRepository
                .Find(Query.All(nameof(FolderSortState.UpdatedAtUtc), Query.Ascending), 0, overflow)
                .ToArray();

            foreach (FolderSortState sortState in statesToRemove)
                folderSortStateRepository.Delete(sortState.Id);
        }

        private readonly ConcurrentDictionary<string, FolderSortState> pendingFolderSortStates = new ConcurrentDictionary<string, FolderSortState>(StringComparer.Ordinal);
        private readonly SemaphoreSlim folderSortStateWriterSignal = new SemaphoreSlim(0);
        private readonly object folderSortStateRepositorySyncRoot = new object();
        private readonly ILiteCollection<FolderSortState> folderSortStateRepository;
        private readonly Task folderSortStateWriterTask;
        private ExceptionDispatchInfo folderSortStateWriterFailure;
        private bool isShutdown;
    }
}
