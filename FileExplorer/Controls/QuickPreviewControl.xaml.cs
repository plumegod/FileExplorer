using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using FileExplorer.Helpers;

namespace FileExplorer.Controls
{
    public partial class QuickPreviewControl : UserControl
    {
        public string FileName
        {
            get { return (string)GetValue(FileNameProperty); }
            set { SetValue(FileNameProperty, value); }
        }
        public static readonly DependencyProperty FileNameProperty =
            DependencyProperty.Register(nameof(FileName), typeof(string), typeof(QuickPreviewControl), new PropertyMetadata(null));

        public QuickPreviewSnapshot Snapshot
        {
            get { return (QuickPreviewSnapshot)GetValue(SnapshotProperty); }
            set { SetValue(SnapshotProperty, value); }
        }
        public static readonly DependencyProperty SnapshotProperty =
            DependencyProperty.Register(nameof(Snapshot), typeof(QuickPreviewSnapshot), typeof(QuickPreviewControl), new PropertyMetadata(null, OnSnapshotChanged));

        public bool Loading
        {
            get { return (bool)GetValue(LoadingProperty); }
            set { SetValue(LoadingProperty, value); }
        }
        public static readonly DependencyProperty LoadingProperty =
            DependencyProperty.Register(nameof(Loading), typeof(bool), typeof(QuickPreviewControl), new PropertyMetadata(false));

        public double MaxPreviewWidth
        {
            get { return (double)GetValue(MaxPreviewWidthProperty); }
            set { SetValue(MaxPreviewWidthProperty, value); }
        }
        public static readonly DependencyProperty MaxPreviewWidthProperty =
            DependencyProperty.Register(nameof(MaxPreviewWidth), typeof(double), typeof(QuickPreviewControl), new PropertyMetadata(DefaultMaxPreviewWidth, OnPreviewBoundsChanged));

        public double MaxPreviewHeight
        {
            get { return (double)GetValue(MaxPreviewHeightProperty); }
            set { SetValue(MaxPreviewHeightProperty, value); }
        }
        public static readonly DependencyProperty MaxPreviewHeightProperty =
            DependencyProperty.Register(nameof(MaxPreviewHeight), typeof(double), typeof(QuickPreviewControl), new PropertyMetadata(DefaultMaxPreviewHeight, OnPreviewBoundsChanged));

        public double PreviewWidth
        {
            get { return (double)GetValue(PreviewWidthProperty); }
            private set { SetValue(PreviewWidthPropertyKey, value); }
        }
        private static readonly DependencyPropertyKey PreviewWidthPropertyKey =
            DependencyProperty.RegisterReadOnly(nameof(PreviewWidth), typeof(double), typeof(QuickPreviewControl), new PropertyMetadata(DefaultPlaceholderWidth));
        public static readonly DependencyProperty PreviewWidthProperty = PreviewWidthPropertyKey.DependencyProperty;

        public double PreviewHeight
        {
            get { return (double)GetValue(PreviewHeightProperty); }
            private set { SetValue(PreviewHeightPropertyKey, value); }
        }
        private static readonly DependencyPropertyKey PreviewHeightPropertyKey =
            DependencyProperty.RegisterReadOnly(nameof(PreviewHeight), typeof(double), typeof(QuickPreviewControl), new PropertyMetadata(DefaultPlaceholderHeight));
        public static readonly DependencyProperty PreviewHeightProperty = PreviewHeightPropertyKey.DependencyProperty;

        public double ContentWidth
        {
            get { return (double)GetValue(ContentWidthProperty); }
            private set { SetValue(ContentWidthPropertyKey, value); }
        }
        private static readonly DependencyPropertyKey ContentWidthPropertyKey =
            DependencyProperty.RegisterReadOnly(nameof(ContentWidth), typeof(double), typeof(QuickPreviewControl), new PropertyMetadata(DefaultMinContentWidth));
        public static readonly DependencyProperty ContentWidthProperty = ContentWidthPropertyKey.DependencyProperty;

        public QuickPreviewControl()
        {
            InitializeComponent();
            UpdatePreviewMetrics();
        }

        private static void OnSnapshotChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is QuickPreviewControl quickPreviewControl)
                quickPreviewControl.UpdatePreviewMetrics();
        }

        private static void OnPreviewBoundsChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is QuickPreviewControl quickPreviewControl)
                quickPreviewControl.UpdatePreviewMetrics();
        }

        private void UpdatePreviewMetrics()
        {
            double maxPreviewWidth = MaxPreviewWidth > 0 ? MaxPreviewWidth : DefaultMaxPreviewWidth;
            double maxPreviewHeight = MaxPreviewHeight > 0 ? MaxPreviewHeight : DefaultMaxPreviewHeight;

            Size previewSize = CalculatePreviewSize(Snapshot?.ImageSource, maxPreviewWidth, maxPreviewHeight);
            PreviewWidth = previewSize.Width;
            PreviewHeight = previewSize.Height;
            ContentWidth = Math.Max(Math.Min(maxPreviewWidth, DefaultMinContentWidth), previewSize.Width);
        }

        private static Size CalculatePreviewSize(ImageSource imageSource, double maxPreviewWidth, double maxPreviewHeight)
        {
            if (imageSource?.Width > 0 && imageSource.Height > 0)
            {
                double scale = Math.Min(maxPreviewWidth / imageSource.Width, maxPreviewHeight / imageSource.Height);
                if (Double.IsNaN(scale) || Double.IsInfinity(scale) || scale <= 0)
                    scale = 1;

                return new Size(
                    Math.Max(1, Math.Round(imageSource.Width * scale)),
                    Math.Max(1, Math.Round(imageSource.Height * scale)));
            }

            return new Size(
                Math.Min(maxPreviewWidth, DefaultPlaceholderWidth),
                Math.Min(maxPreviewHeight, DefaultPlaceholderHeight));
        }

        private const double DefaultMaxPreviewWidth = 560;

        private const double DefaultMaxPreviewHeight = 420;

        private const double DefaultPlaceholderWidth = 420;

        private const double DefaultPlaceholderHeight = 252;

        private const double DefaultMinContentWidth = 220;
    }
}
