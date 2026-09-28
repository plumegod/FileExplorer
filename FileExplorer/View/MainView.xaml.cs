using System.Windows;
using System.Collections.Generic;
using System.ComponentModel;
using System.Windows.Data;
using DevExpress.Xpf.Core;
using FileExplorer.Controls;
using FileExplorer.Properties;
using FileExplorer.ViewModel;

namespace FileExplorer.View
{
    public partial class MainView : DXTabbedWindow
    {
        private static readonly DependencyPropertyKey ActiveTabPropertyKey = DependencyProperty.RegisterReadOnly(
            nameof(ActiveTab), typeof(BrowserTabViewModel), typeof(MainView), new PropertyMetadata(null));

        public static readonly DependencyProperty ActiveTabProperty = ActiveTabPropertyKey.DependencyProperty;

        private static readonly DependencyPropertyKey ActiveFileListPropertyKey = DependencyProperty.RegisterReadOnly(
            nameof(ActiveFileList), typeof(FileListViewControl), typeof(MainView), new PropertyMetadata(null));

        public static readonly DependencyProperty ActiveFileListProperty = ActiveFileListPropertyKey.DependencyProperty;

        private static readonly DependencyPropertyKey ActiveTabContentPropertyKey = DependencyProperty.RegisterReadOnly(
            nameof(ActiveTabContent), typeof(BrowserTabView), typeof(MainView), new PropertyMetadata(null));

        public static readonly DependencyProperty ActiveTabContentProperty = ActiveTabContentPropertyKey.DependencyProperty;

        public BrowserTabViewModel ActiveTab => (BrowserTabViewModel)GetValue(ActiveTabProperty);

        public FileListViewControl ActiveFileList => (FileListViewControl)GetValue(ActiveFileListProperty);

        public BrowserTabView ActiveTabContent => (BrowserTabView)GetValue(ActiveTabContentProperty);

        private readonly Dictionary<BrowserTabViewModel, BrowserTabContentView> browserTabViews = new Dictionary<BrowserTabViewModel, BrowserTabContentView>();

        internal int RegisteredBrowserTabContentViewCount => browserTabViews.Count;

        public MainView()
        {
            if (Settings.Default.WindowState != WindowState.Minimized)
                WindowState = Settings.Default.WindowState;

            Top = Settings.Default.WindowTop;
            Left = Settings.Default.WindowLeft;
            Width = Settings.Default.WindowWidth;
            Height = Settings.Default.WindowHeight;

            InitializeComponent();

            selectedItemContentDescriptor = DependencyPropertyDescriptor.FromProperty(
                DXTabControl.SelectedItemContentProperty,
                typeof(DXTabControl));
            selectedItemContentDescriptor.AddValueChanged(TabControl, OnSelectedItemContentChanged);
            Closed += (s, e) => selectedItemContentDescriptor.RemoveValueChanged(TabControl, OnSelectedItemContentChanged);

            Loaded += (s, e) =>
            {
                SetWindowIcon();
                SetWindowTitle();
            };

            Settings.Default.PropertyChanged += (s, e) =>
            {
                if (e.PropertyName == nameof(Settings.Default.StaticTaskbarIcon))
                    SetWindowIcon();
                if (e.PropertyName == nameof(Settings.Default.StaticTaskbarTitle))
                    SetWindowTitle();
            };

            StateChanged += (s, e) =>
            {
                Settings.Default.WindowState = WindowState;
            };

            SizeChanged += (s, e) =>
            {
                Settings.Default.WindowWidth = Width > 0 ? Width : MinWidth;
                Settings.Default.WindowHeight = Height > 0 ? Height : MinHeight;
            };

            LocationChanged += (s, e) =>
            {
                Settings.Default.WindowTop = Top > 0 ? Top : 0;
                Settings.Default.WindowLeft = Left > 0 ? Left : 0;
            };
        }

        private void SetWindowIcon()
        {
            if (Settings.Default.StaticTaskbarIcon)
                SetBinding(IconProperty, new Binding() { Source = "pack://application:,,,/FileExplorer;component/Assets/ICO/Explorer.ico" });
            else
                SetBinding(IconProperty, new Binding("SelectedItem.DataContext.HeaderFolder.MediumIcon") { Source = Content });
        }

        private void SetWindowTitle()
        {
            if (Settings.Default.StaticTaskbarTitle)
                SetBinding(TitleProperty, new Binding() { Source = "File Explorer" });
            else
                SetBinding(TitleProperty, new Binding("SelectedItem.DataContext.Title") { Source = Content });
        }

        private void OnTabDragOver(object sender, DragEventArgs e)
        {
            if (sender is DXTabItem tabItem)
                tabItem.IsSelected = true;
        }

        private void OnSelectedTabChanged(object sender, TabControlSelectionChangedEventArgs e)
        {
            UpdateActiveTabContext();
        }

        private void OnSelectedItemContentChanged(object sender, System.EventArgs args)
        {
            UpdateActiveTabContext();
        }

        private void UpdateActiveTabContext()
        {
            BrowserTabView tabView = TabControl.SelectedItemContent as BrowserTabView
                ?? TabControl.SelectedContainer?.Content as BrowserTabView;
            BrowserTabViewModel tabViewModel = TabControl.SelectedContainer?.DataContext as BrowserTabViewModel
                ?? tabView?.DataContext as BrowserTabViewModel
                ?? TabControl.SelectedItemContent as BrowserTabViewModel;
            if (!ReferenceEquals(tabView?.DataContext, tabViewModel))
                tabView = null;

            BrowserTabContentView tabContentView = null;
            if (tabViewModel != null)
                browserTabViews.TryGetValue(tabViewModel, out tabContentView);

            SetValue(ActiveTabPropertyKey, tabViewModel);
            SetValue(ActiveFileListPropertyKey, tabContentView?.FileListControl);
            SetValue(ActiveTabContentPropertyKey, tabView);

            if (DataContext is MainViewModel mainViewModel)
                mainViewModel.SetActiveTab(tabViewModel);
        }

        internal void RegisterBrowserTabContentView(BrowserTabContentView tabView)
        {
            if (!(tabView?.DataContext is BrowserTabViewModel tabViewModel))
                return;

            if (!browserTabViews.ContainsKey(tabViewModel))
                tabViewModel.Destroyed += OnBrowserTabDestroyed;
            browserTabViews[tabViewModel] = tabView;
            if (ReferenceEquals(tabViewModel, ActiveTab))
                SetValue(ActiveFileListPropertyKey, tabView.FileListControl);
        }

        internal void RegisterBrowserTabView(BrowserTabView tabView)
        {
            if (tabView?.DataContext is BrowserTabViewModel tabViewModel
                && ReferenceEquals(tabViewModel, ActiveTab))
                SetValue(ActiveTabContentPropertyKey, tabView);
        }

        private void OnBrowserTabDestroyed(object sender, System.EventArgs args)
        {
            if (!(sender is BrowserTabViewModel tabViewModel))
                return;

            tabViewModel.Destroyed -= OnBrowserTabDestroyed;
            browserTabViews.Remove(tabViewModel);
            if (ReferenceEquals(tabViewModel, ActiveTab))
                SetValue(ActiveFileListPropertyKey, null);
        }

        private readonly DependencyPropertyDescriptor selectedItemContentDescriptor;
    }
}
