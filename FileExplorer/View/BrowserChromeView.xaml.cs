using System.Windows;
using System.Windows.Controls;
using FileExplorer.Controls;

namespace FileExplorer.View
{
    public partial class BrowserChromeView : UserControl
    {
        public static readonly DependencyProperty ActiveFileListProperty = DependencyProperty.Register(
            nameof(ActiveFileList), typeof(FileListViewControl), typeof(BrowserChromeView), new PropertyMetadata(null));

        public BrowserChromeView()
        {
            InitializeComponent();
        }

        public FileListViewControl ActiveFileList
        {
            get { return (FileListViewControl)GetValue(ActiveFileListProperty); }
            set { SetValue(ActiveFileListProperty, value); }
        }
    }
}
