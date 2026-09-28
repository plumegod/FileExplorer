using System;
using System.Windows.Controls;
using FileExplorer.ViewModel;

namespace FileExplorer.View
{
    public partial class ShellMenuImportView : UserControl
    {
        public ShellMenuImportView()
        {
            InitializeComponent();
        }

        private ShellMenuImportViewModel ViewModel => DataContext as ShellMenuImportViewModel;

        private async void ScanButton_Click(object sender, System.Windows.RoutedEventArgs e)
        {
            if (ViewModel == null)
                return;
            ResultText.Text = String.Empty;
            try
            {
                await ViewModel.ScanAsync();
            }
            catch (Exception exception)
            {
                ResultText.Text = exception.Message;
            }
        }

        private void CancelButton_Click(object sender, System.Windows.RoutedEventArgs e)
        {
            ViewModel?.CancelScan();
        }

        private void AddSelectedButton_Click(object sender, System.Windows.RoutedEventArgs e)
        {
            int added = ViewModel?.AddSelected() ?? 0;
            ResultText.Text = String.Format(Properties.Resources.AddedCountFormat, added);
            if (ViewModel?.AddFailureCount > 0)
                ResultText.Text += " " + ViewModel.LastAddError;
        }

        private void CopyRawCommand_Click(object sender, System.Windows.RoutedEventArgs e)
        {
            if (sender is Button button && button.Tag is string command && !String.IsNullOrEmpty(command))
                System.Windows.Clipboard.SetText(command);
        }

        private void ShellMenuImportView_Loaded(object sender, System.Windows.RoutedEventArgs e)
        {
            SearchTextBox.Focus();
        }
    }
}
