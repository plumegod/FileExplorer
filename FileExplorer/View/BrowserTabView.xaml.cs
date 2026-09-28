using System.Windows.Controls;

namespace FileExplorer.View
{
    public partial class BrowserTabView : UserControl
    {
        public BrowserTabView()
        {
            InitializeComponent();
            Loaded += (sender, args) => (System.Windows.Window.GetWindow(this) as MainView)?.RegisterBrowserTabView(this);
        }
    }
}
