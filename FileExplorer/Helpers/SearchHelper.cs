using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using FileExplorer.Core;
using FileExplorer.Model;
using FileExplorer.Native;
using FileExplorer.Properties;

namespace FileExplorer.Helpers
{
    public enum SearchBackendKind
    {
        EverythingManaged,
        EverythingLegacyDll,
        FileSystemFallback
    }

    public class SearchDirectoryResult
    {
        public SearchDirectoryResult(SearchBackendKind backEnd, IList<FileModel> items)
        {
            BackEnd = backEnd;
            Items = items ?? Array.Empty<FileModel>();
        }

        public SearchBackendKind BackEnd { get; }

        public IList<FileModel> Items { get; }
    }

    public class SearchHelper
    {
        public static bool IsSearchEverythingAvailable(string path)
        {
            if (!IsEverythingPathSupported(path))
                return false;

            if (!Settings.Default.SearchWithEverything)
                return false;

            string installDirectory = Settings.Default.EverythingInstallDirectory;
            if (!String.IsNullOrWhiteSpace(installDirectory))
                return EverythingIpcClient.IsInstallDirectoryValid(installDirectory);

            return SearchEverything.IsLegacyBackendAvailable;
        }

        public static async Task<SearchDirectoryResult> SearchDirectoryWithPreferredBackend(string path, string searchPattern, CancellationToken cancellationToken = default)
        {
            if (Settings.Default.SearchWithEverything && IsEverythingPathSupported(path))
            {
                string installDirectory = Settings.Default.EverythingInstallDirectory;
                if (!String.IsNullOrWhiteSpace(installDirectory))
                {
                    SearchDirectoryResult managedResult = await SearchWithManagedEverything(path, searchPattern, installDirectory, cancellationToken).ConfigureAwait(false);
                    if (managedResult != null)
                        return managedResult;
                }
                else if (SearchEverything.IsLegacyBackendAvailable)
                {
                    SearchDirectoryResult legacyResult = await SearchWithLegacyEverything(path, searchPattern, cancellationToken).ConfigureAwait(false);
                    if (legacyResult != null)
                        return legacyResult;
                }
            }

            return await SearchDirectoryWithFileSystem(path, searchPattern, cancellationToken).ConfigureAwait(false);
        }

        private static async Task<SearchDirectoryResult> SearchWithManagedEverything(string path, string searchPattern, string installDirectory, CancellationToken cancellationToken)
        {
            if (!EverythingIpcClient.IsInstallDirectoryValid(installDirectory))
                return null;

            EverythingIpcClient client = new EverythingIpcClient(installDirectory);
            if (!await client.EnsureRunning(cancellationToken).ConfigureAwait(false))
                return null;

            FileSystemInfo[] items = null;
            try
            {
                string query = BuildEverythingQuery(path, searchPattern);
                items = await Task.Run(() => client.Search(query, cancellationToken), cancellationToken).ConfigureAwait(false);
            }
            catch
            {
                return null;
            }

            if (items == null)
                return null;

            IList<FileModel> itemModelList = await BuildFileModels(items, cancellationToken).ConfigureAwait(false);
            return new SearchDirectoryResult(SearchBackendKind.EverythingManaged, itemModelList);
        }

        private static async Task<SearchDirectoryResult> SearchWithLegacyEverything(string path, string searchPattern, CancellationToken cancellationToken)
        {
            FileSystemInfo[] items = null;
            try
            {
                string query = BuildEverythingQuery(path, searchPattern);
                items = await Task.Run(() =>
                {
                    using (SearchEverything searchEverything = new SearchEverything())
                        return searchEverything.Search(query);
                }, cancellationToken).ConfigureAwait(false);
            }
            catch
            {
                return null;
            }

            if (items == null)
                return null;

            IList<FileModel> itemModelList = await BuildFileModels(items, cancellationToken).ConfigureAwait(false);
            return new SearchDirectoryResult(SearchBackendKind.EverythingLegacyDll, itemModelList);
        }

        private static async Task<SearchDirectoryResult> SearchDirectoryWithFileSystem(string path, string searchPattern, CancellationToken cancellationToken)
        {
            List<FileModel> items = new List<FileModel>();
            await SearchDirectoryWithFileSystem(path, searchPattern, items, cancellationToken).ConfigureAwait(false);
            return new SearchDirectoryResult(SearchBackendKind.FileSystemFallback, items);
        }

        private static async Task SearchDirectoryWithFileSystem(string path, string searchPattern, List<FileModel> items, CancellationToken cancellationToken)
        {
            if (cancellationToken.IsCancellationRequested)
                return;

            FileSystemInfo[] currentFolderItems = await GetFolderEntries(path, searchPattern).ConfigureAwait(false);
            IList<FileModel> currentFolderModels = await BuildFileModels(currentFolderItems, cancellationToken).ConfigureAwait(false);
            items.AddRange(currentFolderModels);

            string[] childFolders = await GetFolderPaths(path).ConfigureAwait(false);
            foreach (string childFolder in childFolders)
            {
                if (cancellationToken.IsCancellationRequested)
                    break;

                await SearchDirectoryWithFileSystem(childFolder, searchPattern, items, cancellationToken).ConfigureAwait(false);
            }
        }

        private static bool IsEverythingPathSupported(string path)
        {
            try
            {
                string rootPath = Path.GetPathRoot(path);
                if (!FileSystemHelper.IsDrive(rootPath))
                    return false;

                DriveInfo driveInfo = new DriveInfo(rootPath);
                return driveInfo.DriveFormat.OrdinalEquals("NTFS");
            }
            catch
            {
                return false;
            }
        }

        private static string BuildEverythingQuery(string path, string searchPattern)
        {
            string scopedPath = (path ?? String.Empty).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            string normalizedPath = EscapeEverythingTerm(scopedPath + Path.DirectorySeparatorChar);
            string normalizedPattern = EscapeEverythingTerm(searchPattern ?? String.Empty);

            return $"{normalizedPath} {normalizedPattern}";
        }

        private static string EscapeEverythingTerm(string value)
        {
            return (value ?? String.Empty).Replace(" ", "&sp:");
        }

        private static async Task<FileSystemInfo[]> GetFolderEntries(string path, string searchPattern)
        {
            return await Task.Run(() =>
            {
                try
                {
                    string[] entries = Directory.GetFileSystemEntries(path, searchPattern);
                    FileSystemInfo[] items = new FileSystemInfo[entries.Length];

                    for (int index = 0; index < entries.Length; index++)
                    {
                        if (Directory.Exists(entries[index]))
                            items[index] = new DirectoryInfo(entries[index]);
                        else
                            items[index] = new FileInfo(entries[index]);
                    }

                    return items;
                }
                catch
                {
                    return Array.Empty<FileSystemInfo>();
                }
            }).ConfigureAwait(false);
        }

        private static async Task<IList<FileModel>> BuildFileModels(IEnumerable<FileSystemInfo> items, CancellationToken cancellationToken)
        {
            List<FileModel> itemModelList = new List<FileModel>();
            if (items == null)
                return itemModelList;

            foreach (FileSystemInfo item in items)
            {
                if (item == null || cancellationToken.IsCancellationRequested)
                    break;

                FileModel fileModel = FileModel.Create(item);
                if (fileModel == null)
                    continue;

                await fileModel.EnumerateParents().ConfigureAwait(false);
                itemModelList.Add(fileModel);
            }

            return itemModelList;
        }

        private static async Task<string[]> GetFolderPaths(string path)
        {
            try
            {
                return await Task.Run(() => Directory.GetDirectories(path)).ConfigureAwait(false);
            }
            catch
            {
                return Array.Empty<string>();
            }
        }
    }
}
