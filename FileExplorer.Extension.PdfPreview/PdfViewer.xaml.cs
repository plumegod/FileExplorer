using System.Collections.Generic;
using System.ComponentModel.Composition;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using DevExpress.Mvvm.UI;
using DevExpress.Xpf.DocumentViewer;

namespace FileExplorer.Extension.PdfPreview
{
    [Export(typeof(IPreviewExtension))]
    [ExportMetadata(nameof(IPreviewExtensionMetadata.DisplayName), "PDF Viewer")]
    [ExportMetadata(nameof(IPreviewExtensionMetadata.SupportedFileTypes), "pdf")]
    [ExportMetadata(nameof(IPreviewExtensionMetadata.Version), "2.0")]
    public partial class PdfViewer : UserControl, IPreviewExtension
    {
        public Stream Document
        {
            get { return (Stream)GetValue(DocumentProperty); }
            set { SetValue(DocumentProperty, value); }
        }
        public static readonly DependencyProperty DocumentProperty =
            DependencyProperty.Register(nameof(Document), typeof(Stream), typeof(PdfViewer));

        public double ZoomFactor
        {
            get { return (double)GetValue(ZoomFactorProperty); }
            set { SetValue(ZoomFactorProperty, value); }
        }
        public static readonly DependencyProperty ZoomFactorProperty =
            DependencyProperty.Register(nameof(ZoomFactor), typeof(double), typeof(PdfViewer), new PropertyMetadata(1.0));

        public List<double> ZoomLevels { get; } = new List<double> { 0.25, 0.50, 0.75, 1.00, 1.25, 1.50, 2.00, 4.00, 5.00 };

        public PdfViewer()
        {
            InitializeComponent();
        }

        public Task PreviewFile(string filePath)
        {
            ZoomFactor = 1;
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

        private void OnPdfViewerLoaded(object sender, RoutedEventArgs e)
        {
            DXScrollViewer viewer = LayoutTreeHelper.GetVisualChildren(this).OfType<DXScrollViewer>().FirstOrDefault();
            if (viewer != null)
            {
                viewer.HorizontalScrollBarVisibility = ScrollBarVisibility.Auto;
                viewer.VerticalScrollBarVisibility = ScrollBarVisibility.Auto;
            }
        }
    }
}
