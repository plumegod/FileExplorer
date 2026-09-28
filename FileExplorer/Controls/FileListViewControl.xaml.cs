using System;
using System.Collections;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Forms;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Threading;
using DevExpress.Data;
using DevExpress.Mvvm;
using DevExpress.Mvvm.POCO;
using DevExpress.Xpf.Grid;
using DevExpress.Xpf.Grid.TreeList;
using FileExplorer.Core;
using FileExplorer.Helpers;
using FileExplorer.Model;
using FileExplorer.Persistence;
using FileExplorer.Properties;

namespace FileExplorer.Controls
{
    public partial class FileListViewControl : GridControl
    {
        public string CurrentFolderPath
        {
            get { return (string)GetValue(CurrentFolderPathProperty); }
            set { SetValue(CurrentFolderPathProperty, value); }
        }
        public static readonly DependencyProperty CurrentFolderPathProperty = DependencyProperty.Register(nameof(CurrentFolderPath), typeof(string), typeof(FileListViewControl), new PropertyMetadata(null, OnCurrentFolderPathChanged));

        public string HighlightedText
        {
            get { return (string)GetValue(HighlightedTextProperty); }
            set { SetValue(HighlightedTextProperty, value); }
        }
        public static readonly DependencyProperty HighlightedTextProperty = DependencyProperty.Register(nameof(HighlightedText), typeof(string), typeof(FileListViewControl), new PropertyMetadata(null, OnHighlightedTextChanged));

        public ICommand RowDoubleClickCommand
        {
            get => (ICommand)GetValue(RowDoubleClickCommandProperty);
            set => SetValue(RowDoubleClickCommandProperty, value);
        }
        public static readonly DependencyProperty RowDoubleClickCommandProperty = DependencyProperty.Register(nameof(RowDoubleClickCommand), typeof(ICommand), typeof(FileListViewControl));

        public ICommand EditSelectedItemCommand
        {
            get { return (ICommand)GetValue(EditSelectedItemCommandProperty); }
            set { SetValue(EditSelectedItemCommandProperty, value); }
        }
        public static readonly DependencyProperty EditSelectedItemCommandProperty = DependencyProperty.Register(nameof(EditSelectedItemCommand), typeof(ICommand), typeof(FileListViewControl));

        public ICommand EditSelectedItemsCommand
        {
            get { return (ICommand)GetValue(EditSelectedItemsCommandProperty); }
            set { SetValue(EditSelectedItemsCommandProperty, value); }
        }
        public static readonly DependencyProperty EditSelectedItemsCommandProperty = DependencyProperty.Register(nameof(EditSelectedItemsCommand), typeof(ICommand), typeof(FileListViewControl));

        public ICommand EditCommand
        {
            get { return (ICommand)GetValue(EditCommandProperty); }
            private set { SetValue(EditCommandProperty, value); }
        }
        public static readonly DependencyProperty EditCommandProperty = DependencyProperty.Register(nameof(EditCommand), typeof(ICommand), typeof(FileListViewControl));

        public ICommand SaveSelectionCommand
        {
            get { return (ICommand)GetValue(SaveSelectionCommandProperty); }
            set { SetValue(SaveSelectionCommandProperty, value); }
        }
        public static readonly DependencyProperty SaveSelectionCommandProperty = DependencyProperty.Register(nameof(SaveSelectionCommand), typeof(ICommand), typeof(FileListViewControl));

        public ICommand RestoreSelectionCommand
        {
            get { return (ICommand)GetValue(RestoreSelectionCommandProperty); }
            set { SetValue(RestoreSelectionCommandProperty, value); }
        }
        public static readonly DependencyProperty RestoreSelectionCommandProperty = DependencyProperty.Register(nameof(RestoreSelectionCommand), typeof(ICommand), typeof(FileListViewControl));

        public Settings LocalSettings
        {
            get { return (Settings)GetValue(LocalSettingsProperty); }
            set { SetValue(LocalSettingsProperty, value); }
        }
        public static readonly DependencyProperty LocalSettingsProperty =
            DependencyProperty.Register(nameof(LocalSettings), typeof(Settings), typeof(FileListViewControl), new PropertyMetadata(new Settings(), OnLocalSettingsChanged));

        public FileListViewControl()
        {
            InitializeComponent();
            InitializeQuickPreviewHost();
            AttachLocalSettings(LocalSettings);
            SizeChanged += OnFileListViewSizeChanged;

            Loaded += (s, e) =>
            {
                if (DefaultLayoutStream == null)
                {
                    DefaultLayoutStream = new MemoryStream();
                    SaveLayoutToStream(DefaultLayoutStream);
                }
            };

            EndSorting += (s, e) =>
            {
                if (!SuppressFolderSortCaching && !IsFolderPresentationRestorePending)
                    CacheFolderSortState();

                if (SelectedItem != null)
                    View.ScrollIntoView(SelectedItem);
            };

            SelectionChanged += (s, e) =>
            {
                if (SelectedItems.Count == 0)
                    CurrentItem = null;
            };

            ItemsSourceChanged += (s, e) =>
            {
                folderPresentationItemsVersion++;
                IsFolderPresentationRestorePending = true;
                ResetQuickPreviewSession();
                RestoreFolderPresentationStateIfReady();

                if (LocalSettings?.AutoRestoreSelection == true)
                    RestoreSelection(CurrentFolderPath);
            };

            CustomColumnSort += (s, e) =>
            {
                this.NaturalSort(e, LocalSettings?.UnifiedSorting == true);
            };

            ClickTimer = new DispatcherTimer();
            ClickTimer.Interval = TimeSpan.FromMilliseconds(SystemInformation.DoubleClickTime);
            ClickTimer.Tick += (s, e) =>
            {
                StopClickTimer();
                EditSelectedItemCommand?.Execute(SelectedItem);
            };

            EditCommand = new DelegateCommand(() =>
            {
                if (SelectedItems?.Count > 1)
                    EditSelectedItemsCommand?.Execute(SelectedItems);
                else
                    EditSelectedItemCommand?.Execute(SelectedItem);
            }, 
            () => { return EditSelectedItemCommand?.CanExecute(SelectedItem) == true || EditSelectedItemsCommand?.CanExecute(SelectedItems) == true; });

            SaveSelectionCommand = new DelegateCommand(() => { SaveSelection(); }, () => { return SelectedItems.Count > 0; });

            RestoreSelectionCommand = new DelegateCommand(() => { RestoreSelection(); }, () => { return !String.IsNullOrEmpty(CurrentFolderPath) && ManuelRestoreItemsDictionary.ContainsKey(CurrentFolderPath); });

            Unloaded += (s, e) =>
            {
                ResetQuickPreviewSession();
                DetachLocalSettings(LocalSettings);
            };
        }

        public void InvertSelection()
        {
            ArrayList selection = new ArrayList(SelectedItems);

            foreach (var item in VisibleItems)
            {
                if (selection.Contains(item))
                    SelectedItems.Remove(item);
                else
                    SelectedItems.Add(item);
            }
        }

        public void ToggleGrouping(string fieldName)
        {
            if (View is GridViewBase gridView)
            {
                GridColumn column = Columns[fieldName];
                bool isGroupedColumn = gridView.GroupedColumns.Contains(column);

                if (isGroupedColumn)
                    UngroupBy(column);
                else
                    GroupBy(column, true);
            }
        }

        public void CopySelectedRowsToClipboard(GridColumn gridColumn = null)
        {
            try
            {
                int startRowHandle = GetRowHandleByVisibleIndex(0);
                int endRowHandle = GetRowHandleByVisibleIndex(VisibleRowCount - 1);

                if (gridColumn == null)
                {
                    ClipboardCopyMode = ClipboardCopyMode.Default;

                    if (SelectedItem != null)
                        CopySelectedItemsToClipboard();
                    else
                        CopyRangeToClipboard(startRowHandle, endRowHandle);
                }
                else
                {
                    ClipboardCopyMode = ClipboardCopyMode.ExcludeHeader;

                    if (SelectedItem != null)
                    {
                        List<string> values = new List<string>();

                        foreach (int rowHandle in GetSelectedRowHandles())
                            values.Add(GetCellDisplayText(rowHandle, gridColumn));

                        System.Windows.Clipboard.SetText(values.Join(Environment.NewLine));
                    }
                    else
                    {
                        if (View is TableView tableView)
                            tableView.CopyCellsToClipboard(startRowHandle, gridColumn, endRowHandle, gridColumn);
                        else if (View is TreeListView treeView)
                            treeView.CopyCellsToClipboard(startRowHandle, gridColumn, endRowHandle, gridColumn);
                    }                    
                }
            }
            finally { ClipboardCopyMode = ClipboardCopyMode.None; }
        }

        public void SaveFolderLayout(string folderPath, bool applyToSubFolders = false)
        {
            if (!Path.IsPathRooted(folderPath))
                return;

            FolderLayout layout = App.Repository.FolderLayouts.FirstOrDefault(x => x.FolderPath.OrdinalEquals(folderPath));
            if (layout == null)
                layout = new FolderLayout { Name = Path.GetFileName(folderPath), FolderPath = folderPath };

            using(MemoryStream layoutStream = new MemoryStream())
            {
                SaveLayoutToStream(layoutStream);

                layout.ApplyToSubFolders = applyToSubFolders;
                layout.LayoutData = layoutStream.ToArray();
            }

            App.Repository.FolderLayouts.Add(layout);
            ShowManageLayoutsDialog();
        }        

        public void LoadFolderLayout(string folderPath)
        {
            if (!String.IsNullOrEmpty(HighlightedText) && LastLoadedFolderLayout != null)
            {
                LoadDefaultLayout();
                return;
            }

            if (FileSystemHelper.RecycleBinPath.OrdinalEquals(folderPath))
            {
                LoadDefaultLayout();
                Columns["DateDeleted"].Visible = true;
                Columns["OriginalLocation"].Visible = true;

                return;
            }
            else
            {
                Columns["DateDeleted"].Visible = false;
                Columns["OriginalLocation"].Visible = false;
            }

            FolderLayout layout = App.Repository.FolderLayouts.FirstOrDefault(x => x.FolderPath.OrdinalEquals(folderPath));
            if (layout == null)
                layout = App.Repository.FolderLayouts.Where(x => x.ApplyToSubFolders && folderPath.OrdinalStartsWith(x.FolderPath)).
                    DefaultIfEmpty().Aggregate((x, y) => x.FolderPath.Length > y.FolderPath.Length ? x : y);

            bool hasSavedLayout = layout?.LayoutStream != null;
            if (hasSavedLayout)
            {
                if (layout != LastLoadedFolderLayout)
                {
                    layout.LayoutStream.Position = 0;
                    RestoreLayout(layout.LayoutStream);

                    LastLoadedFolderLayout = layout;
                }

                return;
            }

            TryRestoreCachedSortState(folderPath);
        }

        public void SaveSelection()
        {
            SaveSelection(CurrentFolderPath, false);
        }

        public void SaveSelection(string folderPath, bool autoRestore = true)
        {
            Dictionary<string, IList> selectedItemsDictionary = autoRestore ? AutoRestoreItemsDictionary : ManuelRestoreItemsDictionary;

            if (!String.IsNullOrEmpty(folderPath) && SelectedItems.Count > 0)
                selectedItemsDictionary[folderPath] = new ArrayList(SelectedItems);
        }

        public void RestoreSelection()
        {
            RestoreSelection(CurrentFolderPath, false);   
        }

        public void RestoreSelection(string folderPath, bool autoRestore = true)
        {
            Dictionary<string, IList> selectedItemsDictionary = autoRestore ? AutoRestoreItemsDictionary : ManuelRestoreItemsDictionary;

            if (!String.IsNullOrEmpty(folderPath) && selectedItemsDictionary.TryGetValue(folderPath, out IList selectedItems))
            {
                if (View is TreeListView treeView)
                {
                    TreeListNodeIterator nodeIterator = new TreeListNodeIterator(treeView.Nodes, false);
                    while (nodeIterator.MoveNext())
                    {
                        FileModel fileModel = nodeIterator.Current.Content as FileModel;
                        if (fileModel?.Folders != null)
                            nodeIterator.Current.IsExpanded = true;
                    }
                }

                SelectedItems.Clear();
                foreach (object item in selectedItems)
                    SelectedItems.Add(item);
            }
        }

        public void LoadDefaultLayout()
        {
            DefaultLayoutStream.Position = 0;
            RestoreLayout(DefaultLayoutStream);

            LastLoadedFolderLayout = null;
        }

        private void RestoreFolderPresentationStateIfReady()
        {
            string folderPath = CurrentFolderPath;
            if (ItemsSource == null || String.IsNullOrWhiteSpace(folderPath))
                return;

            bool folderChangedWithFreshItems =
                folderPresentationPathVersion > lastAppliedFolderPresentationPathVersion &&
                folderPresentationItemsVersion > lastAppliedFolderPresentationItemsVersion;
            bool sameFolderItemsRefreshed =
                folderPath.OrdinalEquals(lastAppliedFolderPresentationPath) &&
                folderPresentationItemsVersion > lastAppliedFolderPresentationItemsVersion;

            if (!folderChangedWithFreshItems && !sameFolderItemsRefreshed)
                return;

            LoadFolderLayout(folderPath);

            lastAppliedFolderPresentationPath = folderPath;
            lastAppliedFolderPresentationPathVersion = folderPresentationPathVersion;
            lastAppliedFolderPresentationItemsVersion = folderPresentationItemsVersion;
            IsFolderPresentationRestorePending = false;
        }

        private void CacheFolderSortState(string folderPath = null)
        {
            folderPath = folderPath ?? CurrentFolderPath;
            if (!CanCacheFolderSortState(folderPath))
                return;

            FolderSortState sortState = new FolderSortState
            {
                FolderPath = folderPath,
                Columns = Columns
                .Where(x => x != null && !String.IsNullOrWhiteSpace(x.FieldName) && x.SortOrder != ColumnSortOrder.None)
                .OrderBy(x => x.SortIndex)
                .Select(x => new FolderSortColumnState { FieldName = x.FieldName, SortOrder = (int)x.SortOrder })
                .ToList()
            };

            App.Repository?.QueueFolderSortStateSave(sortState);
        }

        private bool TryRestoreCachedSortState(string folderPath)
        {
            if (!CanCacheFolderSortState(folderPath))
                return false;

            FolderSortState sortState = App.Repository?.GetFolderSortState(folderPath);
            if (sortState == null)
                return false;

            bool previousSuppression = SuppressFolderSortCaching;
            SuppressFolderSortCaching = true;

            try
            {
                BeginDataUpdate();

                try
                {
                    ClearSorting();

                    int sortIndex = 0;
                    foreach (FolderSortColumnState columnState in sortState.Columns ?? Enumerable.Empty<FolderSortColumnState>())
                    {
                        if (!Enum.IsDefined(typeof(ColumnSortOrder), columnState.SortOrder))
                            continue;

                        GridColumn column = Columns[columnState.FieldName];
                        if (column == null)
                            continue;

                        column.SortOrder = (ColumnSortOrder)columnState.SortOrder;
                        column.SortIndex = sortIndex++;
                    }
                }
                finally
                {
                    EndDataUpdate();
                }
            }
            finally
            {
                SuppressFolderSortCaching = previousSuppression;
            }

            return true;
        }

        private bool CanCacheFolderSortState(string folderPath)
        {
            return !String.IsNullOrWhiteSpace(folderPath)
                && Path.IsPathRooted(folderPath)
                && String.IsNullOrEmpty(HighlightedText);
        }

        private void RestoreLayout(MemoryStream layoutStream)
        {
            bool previousSuppression = SuppressFolderSortCaching;
            SuppressFolderSortCaching = true;

            try
            {
                RestoreLayoutFromStream(layoutStream);
            }
            finally
            {
                SuppressFolderSortCaching = previousSuppression;
            }
        }

        public void ShowManageLayoutsDialog()
        {
            IDialogService dialogService = DataContext.GetService<IDialogService>();
            if (dialogService != null)
                dialogService.ShowDialog(MessageButton.OK, Properties.Resources.ManageSavedLayouts, "ManageLayoutView", App.Repository.FolderLayouts);
        }

        public void ShowCustomMenuDialog()
        {
            IDialogService dialogService = DataContext.GetService<IDialogService>();
            if (dialogService != null)
                dialogService.ShowDialog(MessageButton.OK, Properties.Resources.CustomMenuItems, "CustomMenuView", App.Repository.MenuItems);
        }

        protected override void OnPreviewMouseMove(System.Windows.Input.MouseEventArgs e)
        {
            base.OnPreviewMouseMove(e);

            if (IsQuickPreviewEnabled() == false || View?.IsEditing == true)
                return;

            FileModel fileModel = GetHoveredFileModel(e.OriginalSource as DependencyObject);
            UpdateQuickPreviewHover(fileModel, e.GetPosition(this));
        }

        protected override void OnMouseLeave(System.Windows.Input.MouseEventArgs e)
        {
            base.OnMouseLeave(e);
            Dispatcher.BeginInvoke(new Action(() =>
            {
                if (IsMouseWithinFileListBounds() == false)
                    ResetQuickPreviewSession();
            }), DispatcherPriority.Input);
        }

        protected override void InitiallyFocusedRowAfterFiltering(object row)
        {
            base.InitiallyFocusedRowAfterFiltering(row);

            if (SelectedItem != null)
                View.ScrollIntoView(SelectedItem);
        }

        protected override void OnPreviewMouseLeftButtonDown(MouseButtonEventArgs e)
        {
            OldClickedItem = CurrentItem;
            base.OnPreviewMouseLeftButtonDown(e);
            NewClickedItem = CurrentItem;            

            if (View.IsEditing || Keyboard.Modifiers != ModifierKeys.None)
            {
                StopClickTimer();
                return;
            }

            DependencyObject target = e.OriginalSource as DependencyObject;
            GridViewHitInfoBase hitInfo = View.CalcHitInfo(target);
            if (hitInfo?.IsDataArea == true)
            {
                UnselectAll();
                return;
            }
            else if (e.ClickCount == 2)
            {
                StopClickTimer();
                RowDoubleClickCommand?.Execute(CurrentItem);
            }
        }

        protected override void OnPreviewMouseLeftButtonUp(MouseButtonEventArgs e)
        {
            base.OnPreviewMouseLeftButtonUp(e);

            long currentTime = DateTimeOffset.Now.ToUnixTimeMilliseconds();
            if (currentTime - FocusTime < SystemInformation.DoubleClickTime)
                return;

            if (View.IsEditing || Keyboard.Modifiers != ModifierKeys.None)
            {
                StopClickTimer();
                return;
            }

            if (NewClickedItem != null && NewClickedItem == OldClickedItem)
            {
                DependencyObject target = e.OriginalSource as DependencyObject;
                GridViewHitInfoBase hitInfo = View.CalcHitInfo(target);

                if (hitInfo is CardViewHitInfo cardViewHitInfo && cardViewHitInfo.InRow)
                    ClickTimer.Start();

                if (hitInfo != null && hitInfo.InRow && hitInfo.Column == Columns[0])
                    ClickTimer.Start();
            }
        }

        protected override void OnIsKeyboardFocusWithinChanged(DependencyPropertyChangedEventArgs e)
        {
            base.OnIsKeyboardFocusWithinChanged(e);

            bool hasFocus = Convert.ToBoolean(e.NewValue);
            FocusTime = hasFocus ? DateTimeOffset.Now.ToUnixTimeMilliseconds() : Int64.MaxValue;
        }

        private void StopClickTimer()
        {
            ClickTimer.Stop();

            OldClickedItem = null;
            NewClickedItem = null;
        }

        private static void OnCurrentFolderPathChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is FileListViewControl fileListViewControl)
            {
                fileListViewControl.folderPresentationPathVersion++;
                fileListViewControl.IsFolderPresentationRestorePending = true;
                fileListViewControl.ResetQuickPreviewSession();

                if (fileListViewControl.LocalSettings?.AutoRestoreSelection == true && e.OldValue != null)
                    fileListViewControl.SaveSelection(e.OldValue.ToString());

                if (e.NewValue != null)
                {
                    string newFolderPath = e.NewValue.ToString();
                    FolderLayout lastLoadedFolderLayout = fileListViewControl.LastLoadedFolderLayout;
                    if (lastLoadedFolderLayout != null &&
                        !newFolderPath.OrdinalEquals(lastLoadedFolderLayout.FolderPath) &&
                        !(newFolderPath.OrdinalStartsWith(lastLoadedFolderLayout.FolderPath) && lastLoadedFolderLayout.ApplyToSubFolders))
                    {
                        fileListViewControl.LoadDefaultLayout();
                    }

                    fileListViewControl.RestoreFolderPresentationStateIfReady();
                }
            }
        }

        private static void OnHighlightedTextChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is GridControl gridControl)
                gridControl.View.SearchString = e.NewValue == null ? null : e.NewValue.ToString();
        }

        private static void OnLocalSettingsChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is FileListViewControl fileListViewControl)
            {
                fileListViewControl.DetachLocalSettings(e.OldValue as Settings);
                fileListViewControl.AttachLocalSettings(e.NewValue as Settings);
            }
        }

        private void AttachLocalSettings(Settings settings)
        {
            if (settings == null)
                return;

            settings.PropertyChanged += OnLocalSettingsPropertyChanged;

            if (IsQuickPreviewEnabled() == false)
                ResetQuickPreviewSession();
        }

        private void DetachLocalSettings(Settings settings)
        {
            if (settings == null)
                return;

            settings.PropertyChanged -= OnLocalSettingsPropertyChanged;
        }

        private void OnLocalSettingsPropertyChanged(object sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(Settings.ShowLayoutImagePreviews))
            {
                Settings.Default.ShowLayoutImagePreviews = LocalSettings?.ShowLayoutImagePreviews == true;
                Dispatcher.BeginInvoke(new Action(RefreshData), DispatcherPriority.DataBind);
                return;
            }

            if (e.PropertyName != nameof(Settings.ShowQuickPreview) && e.PropertyName != nameof(Settings.ShowQuickPreviewImages))
                return;

            if (IsQuickPreviewEnabled() == false || (QuickPreviewPopup?.IsOpen == true && CanQuickPreview(ActiveQuickPreviewFile) == false))
                ResetQuickPreviewSession();
        }

        private void InitializeQuickPreviewHost()
        {
            QuickPreviewControl = new QuickPreviewControl { IsHitTestVisible = false };
            QuickPreviewControl.SizeChanged += (s, e) =>
            {
                if (QuickPreviewPopup?.IsOpen == true)
                    UpdateQuickPreviewPlacement(HoverAnchor);
            };
            QuickPreviewPopup = new Popup
            {
                AllowsTransparency = true,
                Child = QuickPreviewControl,
                Placement = PlacementMode.RelativePoint,
                PlacementTarget = this,
                PopupAnimation = PopupAnimation.Fade,
                StaysOpen = true
            };
            QuickPreviewPopup.Opened += OnQuickPreviewPopupOpened;
            QuickPreviewPopup.Closed += OnQuickPreviewPopupClosed;
            UpdateQuickPreviewBounds();

            HoverGateTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(Math.Max(SystemInformation.MouseHoverTime, 300)) };
            HoverGateTimer.Tick += async (s, e) =>
            {
                HoverGateTimer.Stop();

                FileModel hoveredFile = GetHoveredFileModel(Mouse.DirectlyOver as DependencyObject);
                if (!IsSameFile(hoveredFile, HoverCandidate))
                    return;

                await StartQuickPreviewAsync(HoverCandidate, HoverAnchor);
            };
        }

        private void OnQuickPreviewPopupOpened(object sender, EventArgs e)
        {
            DetachQuickPreviewPopupHook();
            QuickPreviewPopupSource = PresentationSource.FromVisual(QuickPreviewControl) as HwndSource;
            QuickPreviewPopupSource?.AddHook(QuickPreviewPopupWndProc);
        }

        private void OnQuickPreviewPopupClosed(object sender, EventArgs e)
        {
            DetachQuickPreviewPopupHook();
        }

        private IntPtr QuickPreviewPopupWndProc(IntPtr hwnd, int message, IntPtr wParam, IntPtr lParam, ref bool handled)
        {
            if (message != WindowMessageNonClientHitTest)
                return IntPtr.Zero;

            handled = true;
            return new IntPtr(HitTestTransparent);
        }

        private void DetachQuickPreviewPopupHook()
        {
            QuickPreviewPopupSource?.RemoveHook(QuickPreviewPopupWndProc);
            QuickPreviewPopupSource = null;
        }

        private void UpdateQuickPreviewHover(FileModel fileModel, Point anchor)
        {
            if (CanQuickPreview(fileModel) == false)
            {
                ResetQuickPreviewSession();
                return;
            }

            HoverAnchor = anchor;

            if (IsSameFile(fileModel, ActiveQuickPreviewFile))
            {
                if (QuickPreviewPopup.IsOpen)
                    UpdateQuickPreviewPlacement(anchor);

                HoverCandidate = fileModel;
                return;
            }

            if (HoverGateTimer.IsEnabled && IsSameFile(fileModel, HoverCandidate))
                return;

            HoverCandidate = fileModel;

            if (CurrentPreviewSessionState == PreviewSessionState.Warm && QuickPreviewPopup.IsOpen)
            {
                _ = StartQuickPreviewAsync(fileModel, anchor);
                return;
            }

            HoverGateTimer.Stop();
            CancelQuickPreviewRequest();
            HoverGateTimer.Start();
        }

        private async Task StartQuickPreviewAsync(FileModel fileModel, Point anchor)
        {
            if (CanQuickPreview(fileModel) == false)
                return;

            HoverGateTimer.Stop();
            CancelQuickPreviewRequest();

            CurrentPreviewSessionState = PreviewSessionState.Warm;
            ActiveQuickPreviewFile = fileModel;
            UpdateQuickPreviewBounds();
            UpdateQuickPreviewControl(fileModel, null, true);
            UpdateQuickPreviewPlacement(anchor);
            QuickPreviewPopup.IsOpen = true;

            int requestId = ++PreviewRequestId;
            CancellationTokenSource cancellationTokenSource = new CancellationTokenSource();
            QuickPreviewCancellationTokenSource = cancellationTokenSource;
            CancellationToken cancellationToken = cancellationTokenSource.Token;

            try
            {
                QuickPreviewSnapshot snapshot = await PreviewSnapshotHelper.GetSnapshotAsync(fileModel.FullPath, fileModel.Extension, QuickPreviewSnapshotSize, cancellationToken);
                bool shouldUpgrade = snapshot.SourceKind != QuickPreviewSourceKind.ShellCache && ThumbnailHelper.ThumbnailExists(fileModel.FullPath);

                if (!IsCurrentQuickPreviewRequest(requestId, fileModel, cancellationToken))
                    return;

                UpdateQuickPreviewControl(fileModel, snapshot, shouldUpgrade);

                if (!shouldUpgrade)
                    return;

                QuickPreviewSnapshot upgradedSnapshot = await PreviewSnapshotHelper.TryUpgradeSnapshotAsync(fileModel.FullPath, QuickPreviewSnapshotSize, cancellationToken);
                if (!IsCurrentQuickPreviewRequest(requestId, fileModel, cancellationToken))
                    return;

                UpdateQuickPreviewControl(fileModel, upgradedSnapshot ?? snapshot, false);
            }
            catch (OperationCanceledException)
            {
            }
            catch (Exception exception)
            {
                Journal.WriteLog(exception);

                if (IsCurrentQuickPreviewRequest(requestId, fileModel, cancellationToken))
                    UpdateQuickPreviewControl(fileModel, new QuickPreviewSnapshot(null, QuickPreviewSourceKind.Unavailable, false), false);
            }
            finally
            {
                if (QuickPreviewCancellationTokenSource == cancellationTokenSource)
                {
                    QuickPreviewCancellationTokenSource = null;
                    cancellationTokenSource.Dispose();
                }
            }
        }

        private void UpdateQuickPreviewControl(FileModel fileModel, QuickPreviewSnapshot snapshot, bool loading)
        {
            QuickPreviewControl.FileName = fileModel?.FullName;
            QuickPreviewControl.Snapshot = snapshot;
            QuickPreviewControl.Loading = loading;

            if (QuickPreviewPopup?.IsOpen == true)
                UpdateQuickPreviewPlacement(HoverAnchor);
        }

        private void UpdateQuickPreviewPlacement(Point anchor)
        {
            double popupWidth = QuickPreviewPopupFallbackWidth;
            double popupHeight = QuickPreviewPopupFallbackHeight;

            if (QuickPreviewControl != null)
            {
                popupWidth = QuickPreviewControl.ActualWidth;
                popupHeight = QuickPreviewControl.ActualHeight;

                if (popupWidth <= 0 || popupHeight <= 0)
                {
                    QuickPreviewControl.Measure(new Size(Double.PositiveInfinity, Double.PositiveInfinity));
                    Size desiredSize = QuickPreviewControl.DesiredSize;

                    if (popupWidth <= 0 && desiredSize.Width > 0)
                        popupWidth = desiredSize.Width;

                    if (popupHeight <= 0 && desiredSize.Height > 0)
                        popupHeight = desiredSize.Height;
                }
            }

            double horizontalOffset = CalculateQuickPreviewOffset(anchor.X, popupWidth, ActualWidth);
            double verticalOffset = CalculateQuickPreviewOffset(anchor.Y, popupHeight, ActualHeight);

            QuickPreviewPopup.HorizontalOffset = horizontalOffset;
            QuickPreviewPopup.VerticalOffset = verticalOffset;
        }

        private static double CalculateQuickPreviewOffset(double anchor, double popupSize, double availableSize)
        {
            double afterAnchor = anchor + QuickPreviewPointerGap;
            if (afterAnchor + popupSize <= availableSize)
                return afterAnchor;

            double beforeAnchor = anchor - popupSize - QuickPreviewPointerGap;
            if (beforeAnchor >= 0)
                return beforeAnchor;

            return Math.Max(0, Math.Min(afterAnchor, availableSize - popupSize));
        }

        private bool IsMouseWithinFileListBounds()
        {
            if (IsLoaded == false || ActualWidth <= 0 || ActualHeight <= 0)
                return false;

            Point mousePosition = Mouse.GetPosition(this);
            return mousePosition.X >= 0 && mousePosition.X < ActualWidth
                && mousePosition.Y >= 0 && mousePosition.Y < ActualHeight;
        }

        private void UpdateQuickPreviewBounds()
        {
            if (QuickPreviewControl == null)
                return;

            double maxPreviewWidth = ActualWidth > 0 ? Math.Min(QuickPreviewMaxWidth, Math.Max(QuickPreviewMinWidth, ActualWidth - 40)) : QuickPreviewMaxWidth;
            double maxPreviewHeight = ActualHeight > 0 ? Math.Min(QuickPreviewMaxHeight, Math.Max(QuickPreviewMinHeight, ActualHeight - 40)) : QuickPreviewMaxHeight;

            QuickPreviewControl.MaxPreviewWidth = maxPreviewWidth;
            QuickPreviewControl.MaxPreviewHeight = maxPreviewHeight;
        }

        private void OnFileListViewSizeChanged(object sender, SizeChangedEventArgs e)
        {
            UpdateQuickPreviewBounds();

            if (QuickPreviewPopup?.IsOpen == true)
                UpdateQuickPreviewPlacement(HoverAnchor);
        }

        private FileModel GetHoveredFileModel(DependencyObject target)
        {
            if (target == null || View == null)
                return null;

            GridViewHitInfoBase hitInfo = View.CalcHitInfo(target);
            if (hitInfo?.InRow != true || hitInfo.RowHandle < 0)
                return null;

            return GetRow(hitInfo.RowHandle) as FileModel;
        }

        private void ResetQuickPreviewSession()
        {
            HoverCandidate = null;
            ActiveQuickPreviewFile = null;
            CurrentPreviewSessionState = PreviewSessionState.Cold;

            HoverGateTimer?.Stop();
            CancelQuickPreviewRequest();

            if (QuickPreviewPopup != null)
                QuickPreviewPopup.IsOpen = false;

            if (QuickPreviewControl != null)
                UpdateQuickPreviewControl(null, null, false);
        }

        private void CancelQuickPreviewRequest()
        {
            CancellationTokenSource cancellationTokenSource = QuickPreviewCancellationTokenSource;
            QuickPreviewCancellationTokenSource = null;

            if (cancellationTokenSource == null)
                return;

            if (!cancellationTokenSource.IsCancellationRequested)
                cancellationTokenSource.Cancel();

            cancellationTokenSource.Dispose();
        }

        private bool CanQuickPreview(FileModel fileModel)
        {
            return IsQuickPreviewEnabled()
                && fileModel?.IsDirectory == false
                && !String.IsNullOrWhiteSpace(fileModel.FullPath)
                && CanQuickPreviewFileType(fileModel);
        }

        private bool IsQuickPreviewEnabled()
        {
            return LocalSettings?.ShowQuickPreview == true && HasEnabledQuickPreviewTypes();
        }

        private bool HasEnabledQuickPreviewTypes()
        {
            return LocalSettings?.ShowQuickPreviewImages == true;
        }

        private bool CanQuickPreviewFileType(FileModel fileModel)
        {
            return LocalSettings?.ShowQuickPreviewImages == true && fileModel?.IsImage == true;
        }

        private bool IsCurrentQuickPreviewRequest(int requestId, FileModel fileModel, CancellationToken cancellationToken)
        {
            return !cancellationToken.IsCancellationRequested && requestId == PreviewRequestId && IsSameFile(fileModel, ActiveQuickPreviewFile);
        }

        private static bool IsSameFile(FileModel fileModel1, FileModel fileModel2)
        {
            if (ReferenceEquals(fileModel1, fileModel2))
                return true;

            if (fileModel1 == null || fileModel2 == null)
                return false;

            return fileModel1.FullPath.OrdinalEquals(fileModel2.FullPath);
        }

        private Dictionary<string, IList> AutoRestoreItemsDictionary = new Dictionary<string, IList>();

        private Dictionary<string, IList> ManuelRestoreItemsDictionary = new Dictionary<string, IList>();

        private static MemoryStream DefaultLayoutStream;

        private FolderLayout LastLoadedFolderLayout;

        private bool SuppressFolderSortCaching;

        private bool IsFolderPresentationRestorePending;

        private long folderPresentationPathVersion;

        private long folderPresentationItemsVersion;

        private long lastAppliedFolderPresentationPathVersion;

        private long lastAppliedFolderPresentationItemsVersion;

        private string lastAppliedFolderPresentationPath;

        private DispatcherTimer ClickTimer;

        private object NewClickedItem;

        private object OldClickedItem;

        private long FocusTime;

        private DispatcherTimer HoverGateTimer;

        private Popup QuickPreviewPopup;

        private HwndSource QuickPreviewPopupSource;

        private QuickPreviewControl QuickPreviewControl;

        private CancellationTokenSource QuickPreviewCancellationTokenSource;

        private FileModel HoverCandidate;

        private FileModel ActiveQuickPreviewFile;

        private Point HoverAnchor;

        private PreviewSessionState CurrentPreviewSessionState;

        private int PreviewRequestId;

        private const int QuickPreviewSnapshotSize = 512;

        private const double QuickPreviewPopupFallbackWidth = 320;

        private const double QuickPreviewPopupFallbackHeight = 220;

        private const double QuickPreviewMinWidth = 240;

        private const double QuickPreviewMinHeight = 180;

        private const double QuickPreviewMaxWidth = 560;

        private const double QuickPreviewMaxHeight = 420;

        private const double QuickPreviewPointerGap = 20;

        private const int WindowMessageNonClientHitTest = 0x0084;

        private const int HitTestTransparent = -1;
    }

    public enum PreviewSessionState
    {
        Cold,
        Warm
    }

    public class TableViewEx : TableView
    {
        public TableViewEx()
        {
            RowEditStarting += (s, e) => { ScrollIntoView(e.RowHandle); };

            RowEditFinished += (s, e) => { DataControl.Focus(); };
        }

        protected override void UpdateAfterIncrementalSearch()
        {
            base.UpdateAfterIncrementalSearch();
            
            if (TextSearchEngineRoot.MatchedItemIndex != null && TextSearchEngineRoot.MatchedItemIndex.RowIndex == FocusedRowHandle)
                DataControl.SelectedItem = DataControl.CurrentItem;
        }
    }

    public class CardViewEx : CardView
    {
        protected override void UpdateAfterIncrementalSearch()
        {
            base.UpdateAfterIncrementalSearch();

            if (TextSearchEngineRoot.MatchedItemIndex != null && TextSearchEngineRoot.MatchedItemIndex.RowIndex == FocusedRowHandle)
                DataControl.SelectedItem = DataControl.CurrentItem;
        }
    }

    public class TreeViewEx : TreeListView
    {
        public TreeViewEx()
        {
            NodeEditStarting += (s, e) => { ScrollIntoView(e.Node.RowHandle); };

            NodeEditFinished += (s, e) => { DataControl.Focus(); };

            CustomColumnSort += (s, e) =>
            {
                if (DataControl is FileListViewControl fileListViewControl)
                    this.NaturalSort(e, fileListViewControl.LocalSettings.UnifiedSorting);
            };
        }

        protected override void UpdateAfterIncrementalSearch()
        {
            base.UpdateAfterIncrementalSearch();

            if (TextSearchEngineRoot.MatchedItemIndex != null && TextSearchEngineRoot.MatchedItemIndex.RowIndex == FocusedRowHandle)
                DataControl.SelectedItem = DataControl.CurrentItem;
        }

        public async Task ExpandToLevelAsync(int level)
        {
            TreeListNode[] nodes = Nodes.ToArray();

            IList<TreeListRowInfo> selectedNodes = GetSelectedRows();
            if (selectedNodes.Count > 0)
                nodes = selectedNodes.Select(x => x.Node).ToArray();

            try
            {
                BeginDataUpdate(false);

                foreach (TreeListNode node in nodes)
                    await LoadChildren(node.Content as FileModel, level);
            }
            finally
            {
                EndDataUpdate();
            }

            ExpandToLevel(nodes, level);
        }

        private async Task LoadChildren(FileModel fileModel, int level)
        {
            if (fileModel == null)
                return;

            if (fileModel.Content == null)
                await fileModel.EnumerateChildren();

            if (level > 0)
            {
                foreach (FileModel childModel in fileModel.Folders)
                    await LoadChildren(childModel, level - 1);
            }
        }

        private void ExpandToLevel(IEnumerable nodes, int level)
        {
            List<TreeListNode> treeListNodes = nodes.OfType<TreeListNode>().ToList();
            foreach (TreeListNode node in treeListNodes)
            {
                node.IsExpanded = true;

                ExpandToLevel(node.Nodes, level);
                node.IsExpanded = level > node.Level;
            }
        }
    }

    public static class FileModelSorter
    {
        public static void NaturalSort(this TreeListView treeListView, TreeListCustomColumnSortEventArgs e, bool unifiedSorting)
        {
            FileModel value1 = e.Node1.Content as FileModel;
            FileModel value2 = e.Node2.Content as FileModel;

            if (value1 == null || value2 == null)
                return;

            if (e.Column.FieldName == nameof(FileModel.Name))
            {
                if (value1.IsDrive == true && value2.IsDrive == true)
                {
                    e.Result = value1.FullPath.CompareTo(value2.FullPath);
                    e.Handled = true;
                }
                else if (unifiedSorting || value1.IsDirectory == value2.IsDirectory)
                {
                    e.Result = Utilities.NaturalCompare(value1.FullName, value2.FullName);
                    e.Handled = true;
                }
            }
            else if (e.Column.FieldName == nameof(FileModel.ParentName))
            {
                if (unifiedSorting || value1.IsDirectory == value2.IsDirectory)
                {
                    e.Result = Utilities.NaturalCompare(value1.ParentName, value2.ParentName);
                    e.Handled = true;
                }
            }
            else if (e.Column.UnboundType != UnboundColumnType.Bound)
            {
                object nodeValue1 = treeListView.GetNodeValue(e.Node1, e.Column);
                object nodeValue2 = treeListView.GetNodeValue(e.Node2, e.Column);

                if (nodeValue1 is UnboundErrorObject)
                    e.Result = e.SortOrder == ColumnSortOrder.Ascending ? 1 : -1;
                else if (nodeValue2 is UnboundErrorObject)
                    e.Result = e.SortOrder == ColumnSortOrder.Ascending ? -1 : 1;
                else
                    e.Result = Comparer.Default.Compare(nodeValue1, nodeValue2);

                e.Handled = true;
            }

            if (unifiedSorting)
                return;

            if (value1.IsDirectory == true && value2.IsDirectory == false)
            {
                e.Result = e.SortOrder == ColumnSortOrder.Ascending ? -1 : 1;
                e.Handled = true;
            }
            else if (value2.IsDirectory == true && value1.IsDirectory == false)
            {
                e.Result = e.SortOrder == ColumnSortOrder.Ascending ? 1 : -1;
                e.Handled = true;
            }
        }

        public static void NaturalSort(this GridControl gridControl, CustomColumnSortEventArgs e, bool unifiedSorting)
        {
            FileModel value1 = e.Row1 as FileModel;
            FileModel value2 = e.Row2 as FileModel;

            if (value1 == null || value2 == null)
                return;

            if (e.Column.FieldName == nameof(FileModel.Name))
            {
                if (value1.IsDrive == true && value2.IsDrive == true)
                {
                    e.Result = value1.FullPath.CompareTo(value2.FullPath);
                    e.Handled = true;
                }
                else if (unifiedSorting || value1.IsDirectory == value2.IsDirectory)
                {
                    e.Result = Utilities.NaturalCompare(value1.FullName, value2.FullName);
                    e.Handled = true;
                }
            }
            else if (e.Column.FieldName == nameof(FileModel.ParentName))
            {
                if (unifiedSorting || value1.IsDirectory == value2.IsDirectory)
                {
                    e.Result = Utilities.NaturalCompare(value1.ParentName, value2.ParentName);
                    e.Handled = true;
                }
            }
            else if (e.Column.UnboundType != UnboundColumnType.Bound)
            {
                int rowHandle1 = gridControl.GetRowHandleByListIndex(e.ListSourceRowIndex1);
                int rowHandle2 = gridControl.GetRowHandleByListIndex(e.ListSourceRowIndex2);

                object cellValue1 = gridControl.GetCellValue(rowHandle1, e.Column);
                object cellValue2 = gridControl.GetCellValue(rowHandle2, e.Column);

                if (cellValue1 is UnboundErrorObject)
                    e.Result = e.SortOrder == ColumnSortOrder.Ascending ? 1 : -1;
                else if (cellValue2 is UnboundErrorObject)
                    e.Result = e.SortOrder == ColumnSortOrder.Ascending ? -1 : 1;
                else
                    e.Result = Comparer.Default.Compare(cellValue1, cellValue2);

                e.Handled = true;
            }

            if (unifiedSorting)
                return;

            if (value1.IsDirectory == true && value2.IsDirectory == false)
            {
                e.Result = e.SortOrder == ColumnSortOrder.Ascending ? -1 : 1;
                e.Handled = true;
            }
            else if (value2.IsDirectory == true && value1.IsDirectory == false)
            {
                e.Result = e.SortOrder == ColumnSortOrder.Ascending ? 1 : -1;
                e.Handled = true;
            }
        }
    }
}
