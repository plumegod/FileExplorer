using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using FileExplorer.Persistence;
using FileExplorer.Tools.ShellImport;

namespace FileExplorer.ViewModel
{
    public sealed class ShellMenuImportViewModel : INotifyPropertyChanged, IDisposable
    {
        public ShellMenuImportViewModel(
            PersistentCollection<MenuItem> menuItems,
            IShellMenuDiscoveryService discoveryService,
            Func<string, string> resourceLookup = null,
            Func<MenuItemDraft, bool> addDraft = null)
        {
            this.menuItems = menuItems ?? throw new ArgumentNullException(nameof(menuItems));
            this.discoveryService = discoveryService ?? throw new ArgumentNullException(nameof(discoveryService));
            this.resourceLookup = resourceLookup
                ?? (key => Properties.Resources.ResourceManager.GetString(key) ?? key);
            this.addDraft = addDraft ?? (draft => ShellMenuImportFactory.AddIfMissing(menuItems, draft));
            Items = new ObservableCollection<ShellMenuImportSelectionItem>();
            VisibleItems = new ObservableCollection<ShellMenuImportSelectionItem>();
            ScopeOptions = new ObservableCollection<ShellMenuScopeOption>(new[]
            {
                new ShellMenuScopeOption(ShellMenuScope.File.Id, this.resourceLookup("MenuContextFileSelection")),
                new ShellMenuScopeOption(ShellMenuScope.AllFilesystemObjects.Id, this.resourceLookup("MenuContextFileSystemSelection")),
                new ShellMenuScopeOption(ShellMenuScope.Directory.Id, this.resourceLookup("MenuContextDirectorySelection")),
                new ShellMenuScopeOption(ShellMenuScope.DirectoryBackground.Id, this.resourceLookup("MenuContextDirectoryBackground")),
                new ShellMenuScopeOption(ShellMenuScope.Folder.Id, this.resourceLookup("Folder")),
                new ShellMenuScopeOption(ShellMenuScope.Drive.Id, this.resourceLookup("MenuContextDriveSelection")),
                new ShellMenuScopeOption("extension", this.resourceLookup("ShellMenuExtension"))
            });
        }

        public ObservableCollection<ShellMenuImportSelectionItem> Items { get; }
        public ObservableCollection<ShellMenuImportSelectionItem> VisibleItems { get; }
        public ObservableCollection<ShellMenuScopeOption> ScopeOptions { get; }

        public string SearchText
        {
            get => searchText;
            set
            {
                if (String.Equals(searchText, value, StringComparison.Ordinal))
                    return;
                searchText = value;
                RaisePropertyChanged();
                RefreshVisibleItems();
            }
        }

        public string Extension
        {
            get => extension;
            set
            {
                if (String.Equals(extension, value, StringComparison.Ordinal))
                    return;
                extension = value;
                RaisePropertyChanged();
            }
        }

        public ShellMenuConversionKind? ConversionFilter
        {
            get => conversionFilter;
            set
            {
                if (conversionFilter == value)
                    return;
                conversionFilter = value;
                RaisePropertyChanged();
                RefreshVisibleItems();
            }
        }

        public bool OnlyExistingExecutables
        {
            get => onlyExistingExecutables;
            set
            {
                if (onlyExistingExecutables == value)
                    return;
                onlyExistingExecutables = value;
                RaisePropertyChanged();
                RefreshVisibleItems();
            }
        }

        public bool IsScanning
        {
            get => isScanning;
            private set
            {
                if (isScanning == value)
                    return;
                isScanning = value;
                RaisePropertyChanged();
                RaisePropertyChanged(nameof(CanCommitResults));
            }
        }

        public async Task ScanAsync()
        {
            int requestGeneration = Interlocked.Increment(ref generation);
            var cancellation = new CancellationTokenSource();
            CancellationTokenSource previous = Interlocked.Exchange(ref scanCancellation, cancellation);
            previous?.Cancel();
            previous?.Dispose();
            SetResultsCurrent(false);
            foreach (ShellMenuImportSelectionItem item in Items)
                item.IsSelected = false;
            IsScanning = true;

            try
            {
                var discovered = await discoveryService
                    .DiscoverAsync(
                        Extension,
                        ScopeOptions.Where(x => x.IsSelected).Select(x => x.Id).ToArray(),
                        cancellation.Token);
                if (requestGeneration != Volatile.Read(ref generation) || cancellation.IsCancellationRequested)
                    return;

                Items.Clear();
                foreach (ShellMenuDiscoveryItem item in discovered)
                    Items.Add(new ShellMenuImportSelectionItem(item, IsAlreadyImported(item.Draft), resourceLookup));
                CandidateCount = Items.Count(x => !x.IsDiagnostic);
                DiagnosticCount = Items.Count(x => x.IsDiagnostic);
                ScannedScopeCount = discovered.Select(x => x.ScannedScopeCount)
                    .FirstOrDefault(x => x.HasValue)
                    ?? Items.Where(x => !x.IsDiagnostic)
                        .Select(x => x.Scope).Distinct(StringComparer.OrdinalIgnoreCase).Count();
                ReadFailureCount = Items.Where(x => x.Discovery.DiagnosticCode == ShellMenuDiagnosticCode.SourceReadFailed)
                    .Select(x => x.Source).Distinct(StringComparer.OrdinalIgnoreCase).Count();
                SetResultsCurrent(true);
                RefreshVisibleItems();
            }
            catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
            {
            }
            catch (Exception) when (requestGeneration != Volatile.Read(ref generation)
                || cancellation.IsCancellationRequested)
            {
            }
            finally
            {
                if (requestGeneration == Volatile.Read(ref generation))
                    IsScanning = false;
            }
        }

        public void CancelScan()
        {
            Interlocked.Increment(ref generation);
            scanCancellation?.Cancel();
            if (IsScanning)
                SetResultsCurrent(false);
            IsScanning = false;
        }

        public int AddSelected()
        {
            if (!CanCommitResults)
                return 0;
            int added = 0;
            AddFailureCount = 0;
            LastAddError = null;
            foreach (ShellMenuImportSelectionItem item in Items.Where(x => x.IsSelected && x.CanSelect).ToList())
            {
                try
                {
                    if (addDraft(item.CreateImportDraft()))
                        added++;
                    item.IsSelected = false;
                    item.CreateDuplicate = false;
                    item.IsAlreadyImported = true;
                }
                catch (Exception exception)
                {
                    AddFailureCount++;
                    LastAddError = exception.Message;
                }
            }
            return added;
        }

        public int AddFailureCount
        {
            get => addFailureCount;
            private set
            {
                if (addFailureCount == value)
                    return;
                addFailureCount = value;
                RaisePropertyChanged();
            }
        }

        public string LastAddError
        {
            get => lastAddError;
            private set
            {
                if (String.Equals(lastAddError, value, StringComparison.Ordinal))
                    return;
                lastAddError = value;
                RaisePropertyChanged();
            }
        }

        public int DiagnosticCount
        {
            get => diagnosticCount;
            private set
            {
                if (diagnosticCount == value)
                    return;
                diagnosticCount = value;
                RaisePropertyChanged();
                RaisePropertyChanged(nameof(ScanSummary));
            }
        }

        public int CandidateCount
        {
            get => candidateCount;
            private set
            {
                if (candidateCount == value)
                    return;
                candidateCount = value;
                RaisePropertyChanged();
                RaisePropertyChanged(nameof(ScanSummary));
            }
        }

        public int ReadFailureCount
        {
            get => readFailureCount;
            private set
            {
                if (readFailureCount == value)
                    return;
                readFailureCount = value;
                RaisePropertyChanged();
                RaisePropertyChanged(nameof(ScanSummary));
            }
        }

        public int ScannedScopeCount
        {
            get => scannedScopeCount;
            private set
            {
                if (scannedScopeCount == value)
                    return;
                scannedScopeCount = value;
                RaisePropertyChanged();
                RaisePropertyChanged(nameof(ScanSummary));
            }
        }

        public bool CanCommitResults => hasCurrentResults && !IsScanning;

        public string ScanSummary => !hasCurrentResults
            ? resourceLookup("ShellMenuResultsNotCurrent")
            : ReadFailureCount > 0
                ? String.Format(resourceLookup("ShellMenuScanSummaryPartialFormat"),
                    CandidateCount, ScannedScopeCount, ReadFailureCount, DiagnosticCount)
                : String.Format(resourceLookup("ShellMenuScanSummaryCompleteFormat"),
                    CandidateCount, ScannedScopeCount, DiagnosticCount);

        public void Dispose()
        {
            CancelScan();
            scanCancellation?.Dispose();
            scanCancellation = null;
        }

        private bool IsAlreadyImported(MenuItemDraft draft)
        {
            return draft != null
                && !String.IsNullOrEmpty(draft.SourceId)
                && menuItems.Any(x => String.Equals(
                    x.ImportedMenuSourceId,
                    draft.SourceId,
                    StringComparison.OrdinalIgnoreCase));
        }

        private void RefreshVisibleItems()
        {
            string filter = SearchText?.Trim();
            VisibleItems.Clear();
            foreach (ShellMenuImportSelectionItem item in Items.Where(x => MatchesSearch(x, filter)
                && (!ConversionFilter.HasValue || x.ConversionKind == ConversionFilter.Value)
                && (!OnlyExistingExecutables || x.ExecutableExists)))
                VisibleItems.Add(item);
        }

        private void SetResultsCurrent(bool value)
        {
            if (hasCurrentResults == value)
                return;
            hasCurrentResults = value;
            RaisePropertyChanged(nameof(CanCommitResults));
            RaisePropertyChanged(nameof(ScanSummary));
        }

        private static bool MatchesSearch(ShellMenuImportSelectionItem item, string filter)
        {
            if (String.IsNullOrEmpty(filter))
                return true;
            return Contains(item.Title, filter)
                || Contains(item.Scope, filter)
                || Contains(item.RawCommand, filter)
                || Contains(item.Reason, filter)
                || Contains(item.Source, filter)
                || Contains(item.ParsedExecutable, filter);
        }

        private static bool Contains(string value, string filter)
        {
            return value?.IndexOf(filter, StringComparison.CurrentCultureIgnoreCase) >= 0;
        }

        public event PropertyChangedEventHandler PropertyChanged;

        private void RaisePropertyChanged([CallerMemberName] string propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }

        private string searchText;
        private string extension;
        private ShellMenuConversionKind? conversionFilter;
        private bool onlyExistingExecutables;
        private bool isScanning;
        private int diagnosticCount;
        private int candidateCount;
        private int readFailureCount;
        private int scannedScopeCount;
        private bool hasCurrentResults;
        private int addFailureCount;
        private string lastAddError;
        private int generation;
        private CancellationTokenSource scanCancellation;
        private readonly PersistentCollection<MenuItem> menuItems;
        private readonly IShellMenuDiscoveryService discoveryService;
        private readonly Func<string, string> resourceLookup;
        private readonly Func<MenuItemDraft, bool> addDraft;
    }

    public sealed class ShellMenuScopeOption : INotifyPropertyChanged
    {
        public ShellMenuScopeOption(string id, string displayName)
        {
            Id = id;
            DisplayName = displayName;
            isSelected = true;
        }

        public string Id { get; }
        public string DisplayName { get; }

        public bool IsSelected
        {
            get => isSelected;
            set
            {
                if (isSelected == value)
                    return;
                isSelected = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsSelected)));
            }
        }

        public event PropertyChangedEventHandler PropertyChanged;
        private bool isSelected;
    }

    public sealed class ShellMenuImportSelectionItem : INotifyPropertyChanged
    {
        public ShellMenuImportSelectionItem(
            ShellMenuDiscoveryItem discovery,
            bool isAlreadyImported,
            Func<string, string> resourceLookup = null)
        {
            Discovery = discovery ?? throw new ArgumentNullException(nameof(discovery));
            this.isAlreadyImported = isAlreadyImported;
            this.resourceLookup = resourceLookup ?? (key => key);
        }

        public ShellMenuDiscoveryItem Discovery { get; }
        public string Title => IsDiagnostic ? resourceLookup("ShellMenuScanDiagnostic") : Discovery.Title;
        public string Scope => Discovery.Scope;
        public string ScopeText => GetScopeText(Scope);
        public string RawCommand => Discovery.RawCommand;
        public bool CanCopyRawCommand => !String.IsNullOrEmpty(RawCommand);
        public ShellMenuConversionKind ConversionKind => Discovery.ConversionKind;
        public string Reason => Discovery.Reason;
        public string ReasonText => GetReasonText(Reason);
        public MenuItemDraft Draft => Discovery.Draft;
        public bool IsDiagnostic => Discovery.IsDiagnostic;
        public bool IsReviewItem => ConversionKind == ShellMenuConversionKind.NeedsReview && Draft != null;
        public string ConversionKindText => resourceLookup(
            ConversionKind == ShellMenuConversionKind.Direct
                ? "ShellMenuDirect"
                : ConversionKind == ShellMenuConversionKind.NeedsReview
                    ? "ShellMenuNeedsReview"
                    : ConversionKind == ShellMenuConversionKind.ShellBridge
                        ? "ShellMenuShellBridge"
                        : "ShellMenuUnsupported");
        public string Source => Discovery.SourceSummary ?? Draft?.SourceId ?? Scope;
        public string ExpandedCommand => Draft?.ExpandedCommand;
        public string ParsedExecutable => Draft?.ParsedExecutable;
        public string ParsedArguments => Draft?.ParsedArguments;
        public string PlaceholderSummary => Draft?.PlaceholderSummary;
        public bool ExecutableExists => Draft?.ExecutableExists == true;
        public string ConvertedCommandPreview => Draft == null
            ? null
            : Draft.IsShellBridge
                ? "shell-handler:" + Draft.ShellHandlerClsid + " (" + (Draft.ShellHandlerName ?? Draft.Name) + ")"
                : String.Join(" ", new[]
                {
                    Draft.Application,
                    Draft.Prefix + GetParameterPreview(Draft.Parameter, Draft.SelectionFilter) + Draft.Suffix
                }.Where(x => !String.IsNullOrWhiteSpace(x)));

        public string DraftApplication
        {
            get => Draft?.Application;
            set
            {
                if (Draft == null || String.Equals(Draft.Application, value, StringComparison.Ordinal))
                    return;
                Draft.Application = value;
                DraftChanged();
            }
        }

        public string DraftArguments
        {
            get => Draft == null ? null : (Draft.Prefix ?? String.Empty) + (Draft.Suffix ?? String.Empty);
            set
            {
                if (Draft == null || String.Equals(DraftArguments, value, StringComparison.Ordinal))
                    return;
                Draft.Parameter = FileExplorer.Core.ParameterType.Expression;
                Draft.Prefix = value;
                Draft.Suffix = String.Empty;
                DraftChanged();
            }
        }

        public bool IsReviewAccepted
        {
            get => isReviewAccepted;
            set
            {
                if (isReviewAccepted == value)
                    return;
                isReviewAccepted = value;
                RaisePropertyChanged();
                RaisePropertyChanged(nameof(CanSelect));
                if (!CanSelect)
                    IsSelected = false;
            }
        }

        public bool IsAlreadyImported
        {
            get => isAlreadyImported;
            internal set
            {
                if (isAlreadyImported == value)
                    return;
                isAlreadyImported = value;
                RaisePropertyChanged();
                RaisePropertyChanged(nameof(CreateDuplicate));
                RaisePropertyChanged(nameof(CanSelect));
                if (!CanSelect)
                    IsSelected = false;
            }
        }

        public bool CreateDuplicate
        {
            get => createDuplicate;
            set
            {
                bool effective = value && IsAlreadyImported;
                if (createDuplicate == effective)
                    return;
                createDuplicate = effective;
                RaisePropertyChanged();
                RaisePropertyChanged(nameof(CanSelect));
                if (!CanSelect)
                    IsSelected = false;
            }
        }

        public bool IsSelected
        {
            get => isSelected;
            set
            {
                if (isSelected == value || (value && !CanSelect))
                    return;
                isSelected = value;
                RaisePropertyChanged();
            }
        }

        public bool CanSelect => Draft != null
            && (!IsAlreadyImported || CreateDuplicate)
            && IsDraftValid
            && (ConversionKind == ShellMenuConversionKind.Direct
                || ConversionKind == ShellMenuConversionKind.ShellBridge
                || (ConversionKind == ShellMenuConversionKind.NeedsReview && IsReviewAccepted));

        public bool IsDraftValid => Draft != null
            && (
                (Draft.IsShellBridge
                    && !String.IsNullOrWhiteSpace(Draft.ShellHandlerClsid)
                    && Guid.TryParse(Draft.ShellHandlerClsid, out _))
                || (!String.IsNullOrWhiteSpace(Draft.Application)
                    && (!Draft.RequiresEditing || (hasDraftBeenEdited
                        && !UnresolvedCommandToken.IsMatch(DraftArguments ?? String.Empty)))));

        internal MenuItemDraft CreateImportDraft()
        {
            return CreateDuplicate ? Draft.CreateDuplicate() : Draft;
        }

        private void DraftChanged()
        {
            hasDraftBeenEdited = true;
            RaisePropertyChanged(nameof(DraftApplication));
            RaisePropertyChanged(nameof(DraftArguments));
            RaisePropertyChanged(nameof(ConvertedCommandPreview));
            RaisePropertyChanged(nameof(IsDraftValid));
            RaisePropertyChanged(nameof(CanSelect));
            if (!CanSelect)
                IsSelected = false;
        }

        private static string GetParameterPreview(
            FileExplorer.Core.ParameterType parameter,
            FileExplorer.Core.SelectionFilter selectionFilter)
        {
            switch (parameter)
            {
                case FileExplorer.Core.ParameterType.Path:
                    return selectionFilter != FileExplorer.Core.SelectionFilter.Single
                        ? "<paths>"
                        : "<path>";
                default:
                    return String.Empty;
            }
        }

        private string GetScopeText(string scope)
        {
            string key;
            switch (scope)
            {
                case "file": key = "MenuContextFileSelection"; break;
                case "directory":
                case "folder": key = "MenuContextDirectorySelection"; break;
                case "directory-background": key = "MenuContextDirectoryBackground"; break;
                case "drive": key = "MenuContextDriveSelection"; break;
                case "all-filesystem-objects": key = "MenuContextFileSystemSelection"; break;
                default: return scope;
            }
            return resourceLookup(key);
        }

        private string GetReasonText(string reason)
        {
            if (String.IsNullOrWhiteSpace(reason))
                return null;
            string key;
            if (!ReasonResourceKeys.TryGetValue(reason, out key))
                return reason;
            return resourceLookup(key) + " (" + reason + ")";
        }

        public event PropertyChangedEventHandler PropertyChanged;

        private void RaisePropertyChanged([CallerMemberName] string propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }

        private bool isReviewAccepted;
        private bool isAlreadyImported;
        private bool isSelected;
        private bool createDuplicate;
        private bool hasDraftBeenEdited;
        private readonly Func<string, string> resourceLookup;

        private static readonly System.Collections.Generic.IReadOnlyDictionary<string, string> ReasonResourceKeys
            = new System.Collections.Generic.Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["ambiguous-executable"] = "ShellMenuReasonAmbiguousExecutable",
                ["arguments-unparseable"] = "ShellMenuReasonArgumentsUnparseable",
                ["cascade-cycle"] = "ShellMenuReasonCascadeCycle",
                ["cascade-depth"] = "ShellMenuReasonCascadeDepth",
                ["cascade-missing"] = "ShellMenuReasonCascadeMissing",
                ["cascade-read-failed"] = "ShellMenuReasonCascadeReadFailed",
                ["command-empty"] = "ShellMenuReasonCommandEmpty",
                ["command-missing"] = "ShellMenuReasonCommandMissing",
                ["conditional-visibility-unresolved"] = "ShellMenuReasonConditionalVisibilityUnresolved",
                ["dangerous-host"] = "ShellMenuReasonDangerousHost",
                ["dde"] = "ShellMenuReasonDde",
                ["dynamic-com"] = "ShellMenuReasonDynamicCom",
                ["dynamic-handler"] = "ShellMenuReasonDynamicHandler",

                ["handler-clsid-missing"] = "ShellMenuReasonHandlerClsidMissing",

                ["shell-bridge"] = "ShellMenuReasonShellBridge",
                ["elevation-sensitive"] = "ShellMenuReasonElevationSensitive",
                ["executable-missing"] = "ShellMenuReasonExecutableMissing",
                ["executable-quote-unclosed"] = "ShellMenuReasonExecutableQuoteUnclosed",
                ["environment-unresolved"] = "ShellMenuReasonEnvironmentUnresolved",
                ["hidden-by-registration"] = "ShellMenuReasonHiddenByRegistration",
                ["indirect-title-unresolved"] = "ShellMenuReasonIndirectTitleUnresolved",
                ["multiple-placeholders"] = "ShellMenuReasonMultiplePlaceholders",
                ["not-static"] = "ShellMenuReasonNotStatic",
                ["round-trip-mismatch"] = "ShellMenuReasonRoundTripMismatch",
                ["scope-placeholder-mismatch"] = "ShellMenuReasonScopePlaceholderMismatch",
                ["script-host"] = "ShellMenuReasonScriptHost",
                ["shift-only-source-would-become-always-visible"] = "ShellMenuReasonShiftOnly",
                ["source-incomplete"] = "ShellMenuReasonSourceIncomplete",
                ["unsupported-executable"] = "ShellMenuReasonUnsupportedExecutable",
                ["unsupported-placeholder"] = "ShellMenuReasonUnsupportedPlaceholder",
                ["view-conflict"] = "ShellMenuReasonViewConflict"
            };
        private static readonly System.Text.RegularExpressions.Regex UnresolvedCommandToken
            = new System.Text.RegularExpressions.Regex("%", System.Text.RegularExpressions.RegexOptions.Compiled);
    }
}
