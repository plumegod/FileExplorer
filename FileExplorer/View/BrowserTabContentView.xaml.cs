using System.Windows.Controls;
using FileExplorer.Controls;

namespace FileExplorer.View
{
    public partial class BrowserTabContentView : UserControl
    {
        public BrowserTabContentView()
        {
            InitializeComponent();
            Loaded += (sender, args) => (System.Windows.Window.GetWindow(this) as MainView)?.RegisterBrowserTabContentView(this);
        }

        public FileListViewControl FileListControl => FileList;
    }
}
