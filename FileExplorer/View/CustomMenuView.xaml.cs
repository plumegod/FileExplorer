using System.Windows;
using System.Windows.Controls;
using FileExplorer.Tools;
using FileExplorer.Tools.ShellImport;
using FileExplorer.ViewModel;

namespace FileExplorer.View
{
    public partial class CustomMenuView : UserControl
    {
        public CustomMenuView()
        {
            InitializeComponent();
            Loaded += CustomMenuView_Loaded;
            Unloaded += CustomMenuView_Unloaded;
        }

        private void CustomMenuView_Loaded(object sender, RoutedEventArgs e)
        {
            EnsureToolPresetViewModel();
            EnsureShellMenuImportViewModel();
        }

        private void ToolPresetsButton_Click(object sender, RoutedEventArgs e)
        {
            EnsureToolPresetViewModel();
            bool showSelector = ToolPresetSelector.Visibility != Visibility.Visible;
            ShowPanel(showSelector ? ToolPresetSelector : null);
        }

        private void ShellMenuImportButton_Click(object sender, RoutedEventArgs e)
        {
            EnsureShellMenuImportViewModel();
            bool showImporter = ShellMenuImporter.Visibility != Visibility.Visible;
            ShowPanel(showImporter ? ShellMenuImporter : null);
        }

        private void CustomMenuView_Unloaded(object sender, RoutedEventArgs e)
        {
            (ToolPresetSelector.DataContext as ToolPresetSelectorViewModel)?.ApplySelected();
            (ShellMenuImporter.DataContext as ShellMenuImportViewModel)?.CancelScan();
        }

        private void EnsureToolPresetViewModel()
        {
            if (!(ToolPresetSelector.DataContext is ToolPresetSelectorViewModel) && App.Repository != null)
            {
                InitializeToolPresetSelector(
                    App.Repository.MenuItems,
                    new ToolLocator(App.Repository.ToolLocationOverrides));
            }
        }

        public void InitializeToolPresetSelector(FileExplorer.Persistence.PersistentCollection<FileExplorer.Persistence.MenuItem> menuItems, IToolLocator locator)
        {
            ToolPresetSelector.DataContext = new ToolPresetSelectorViewModel(menuItems, locator);
        }

        public void InitializeShellMenuImporter(
            FileExplorer.Persistence.PersistentCollection<FileExplorer.Persistence.MenuItem> menuItems,
            IShellMenuDiscoveryService discoveryService)
        {
            ShellMenuImporter.DataContext = new ShellMenuImportViewModel(menuItems, discoveryService);
        }

        private void EnsureShellMenuImportViewModel()
        {
            if (ShellMenuImporter.DataContext is ShellMenuImportViewModel || App.Repository == null)
                return;
            var reader = new WindowsShellRegistryReader();
            InitializeShellMenuImporter(
                App.Repository.MenuItems,
                new ShellMenuDiscoveryService(
                    new WindowsShellMenuScopeProvider(reader),
                    new ShellMenuInventory(reader, new HkcrMergePolicy()),
                    new ShellMenuAnalyzer(reader),
                    new ShellMenuConverter()));
        }

        private void ShowPanel(UIElement panel)
        {
            CustomMenuGrid.Visibility = panel == null ? Visibility.Visible : Visibility.Collapsed;
            ToolPresetSelector.Visibility = panel == ToolPresetSelector ? Visibility.Visible : Visibility.Collapsed;
            ShellMenuImporter.Visibility = panel == ShellMenuImporter ? Visibility.Visible : Visibility.Collapsed;
            if (panel != ShellMenuImporter)
                (ShellMenuImporter.DataContext as ShellMenuImportViewModel)?.CancelScan();
        }
    }
}
