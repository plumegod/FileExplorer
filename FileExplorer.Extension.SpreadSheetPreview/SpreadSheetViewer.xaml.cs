using System.ComponentModel.Composition;
using System.IO;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;

namespace FileExplorer.Extension.SpreadSheetPreview
{
    [Export(typeof(IPreviewExtension))]
    [ExportMetadata(nameof(IPreviewExtensionMetadata.DisplayName), "Spread Sheet Viewer")]
    [ExportMetadata(nameof(IPreviewExtensionMetadata.SupportedFileTypes), "csv|xls|xlt|xlsx|xltx|xlsb|xlsm|xltm")]
    [ExportMetadata(nameof(IPreviewExtensionMetadata.Version), "2.0")]
    public partial class SpreadSheetViewer : UserControl, IPreviewExtension
    {
        public Stream Document
        {
            get { return (Stream)GetValue(DocumentProperty); }
            set { SetValue(DocumentProperty, value); }
        }
        public static readonly DependencyProperty DocumentProperty =
            DependencyProperty.Register(nameof(Document), typeof(Stream), typeof(SpreadSheetViewer));

        public SpreadSheetViewer()
        {
            InitializeComponent();
        }

        public Task PreviewFile(string filePath)
        {
            Document = File.Open(filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);

            return Task.CompletedTask;
        }

        public Task UnloadFile()
        {
            if (Document != null)
            {
                Stream stream = Document;
                Document = null;
                stream.Dispose();
            }

            return Task.CompletedTask;
        }
    }
}
