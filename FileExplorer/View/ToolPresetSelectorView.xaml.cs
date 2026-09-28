using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using FileExplorer.Tools;
using FileExplorer.ViewModel;
using Microsoft.Win32;

namespace FileExplorer.View
{
    public partial class ToolPresetSelectorView : UserControl
    {
        public ToolPresetSelectorView()
        {
            InitializeComponent();
        }

        private ToolPresetSelectorViewModel ViewModel => DataContext as ToolPresetSelectorViewModel;

        private void Browse_Click(object sender, RoutedEventArgs e)
        {
            if (!(sender is Button button) || !(button.Tag is ToolPresetSelectionItem item) || ViewModel == null)
                return;
            var dialog = new OpenFileDialog
            {
                CheckFileExists = true,
                FileName = item.ExpectedFileName,
                Filter = $"{item.ExpectedFileName}|{item.ExpectedFileName}|{Properties.Resources.Application} (*.exe)|*.exe"
            };
            if (dialog.ShowDialog() != true)
                return;
            try
            {
                ViewModel.SetExecutablePath(item, dialog.FileName);
                ResultText.Text = dialog.FileName;
                ResultText.Foreground = SystemColors.ControlTextBrush;
            }
            catch (ToolPresetException exception)
            {
                ResultText.Text = ToolPresetErrorText.Get(exception.ErrorCode);
                ResultText.Foreground = Brushes.IndianRed;
            }
        }

        private void Refresh_Click(object sender, RoutedEventArgs e)
        {
            ViewModel?.Refresh();
            ResultText.Text = String.Empty;
        }

        private void Clear_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button button && button.Tag is ToolPresetSelectionItem item && ViewModel != null)
            {
                ViewModel.ClearOverride(item);
                ResultText.Text = String.Empty;
            }
        }

        private void AddSelected_Click(object sender, RoutedEventArgs e)
        {
            int added = ViewModel?.ApplySelected() ?? 0;
            ResultText.Text = String.Format(Properties.Resources.AddedCountFormat, added);
            ResultText.Foreground = SystemColors.ControlTextBrush;
        }
    }
}
