using System;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using FileExplorer.Controls;
using FileExplorer.ViewModel;

namespace FileExplorer.View
{
    public partial class BrowserWorkspaceView : UserControl
    {
        public static readonly DependencyProperty ActiveTabContentProperty = DependencyProperty.Register(
            nameof(ActiveTabContent), typeof(object), typeof(BrowserWorkspaceView), new PropertyMetadata(null));

        public static readonly DependencyProperty ActiveTabProperty = DependencyProperty.Register(
            nameof(ActiveTab), typeof(BrowserTabViewModel), typeof(BrowserWorkspaceView),
            new PropertyMetadata(null, OnActiveTabChanged));

        public static readonly DependencyProperty ActiveFileListProperty = DependencyProperty.Register(
            nameof(ActiveFileList), typeof(FileListViewControl), typeof(BrowserWorkspaceView), new PropertyMetadata(null));

        public BrowserWorkspaceView()
        {
            InitializeComponent();
        }

        public object ActiveTabContent
        {
            get { return GetValue(ActiveTabContentProperty); }
            set { SetValue(ActiveTabContentProperty, value); }
        }

        public BrowserTabViewModel ActiveTab
        {
            get { return (BrowserTabViewModel)GetValue(ActiveTabProperty); }
            set { SetValue(ActiveTabProperty, value); }
        }

        public FileListViewControl ActiveFileList
        {
            get { return (FileListViewControl)GetValue(ActiveFileListProperty); }
            set { SetValue(ActiveFileListProperty, value); }
        }

        public bool IsNavigationCreated => NavigationHost.Content != null;

        public bool IsPreviewCreated => PreviewHost.Content != null;

        public bool IsDetailsCreated => DetailsHost.Content != null;

        internal object PresentedContent => ActiveContentPresenter.Content;

        private static void OnActiveTabChanged(DependencyObject sender, DependencyPropertyChangedEventArgs args)
        {
            var workspace = (BrowserWorkspaceView)sender;
            workspace.ChangeActiveTab(args.OldValue as BrowserTabViewModel, args.NewValue as BrowserTabViewModel);
        }

        private void ChangeActiveTab(BrowserTabViewModel oldTab, BrowserTabViewModel newTab)
        {
            if (oldTab != null)
                oldTab.Settings.PropertyChanged -= OnTabSettingsChanged;
            if (newTab != null)
                newTab.Settings.PropertyChanged += OnTabSettingsChanged;

            if (IsNavigationCreated)
                NavigationHost.Content = newTab;
            if (IsPreviewCreated)
                PreviewHost.Content = newTab;
            if (IsDetailsCreated)
                DetailsHost.Content = newTab;

            EnsureVisiblePanes();
        }

        private void OnTabSettingsChanged(object sender, PropertyChangedEventArgs args)
        {
            if (args.PropertyName == nameof(ActiveTab.Settings.ShowNavigationPane)
                || args.PropertyName == nameof(ActiveTab.Settings.ShowPreviewPane)
                || args.PropertyName == nameof(ActiveTab.Settings.ShowDetailsPane))
                EnsureVisiblePanes();
        }

        private void EnsureVisiblePanes()
        {
            BrowserTabViewModel tab = ActiveTab;
            if (tab == null)
                return;

            if (tab.Settings.ShowNavigationPane && !IsNavigationCreated)
                NavigationHost.Content = tab;
            if (tab.Settings.ShowPreviewPane && !IsPreviewCreated)
                PreviewHost.Content = tab;
            if (tab.Settings.ShowDetailsPane && !IsDetailsCreated)
                DetailsHost.Content = tab;
        }
    }
}
