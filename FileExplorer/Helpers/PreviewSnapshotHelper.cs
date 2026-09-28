using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Media;
using FileExplorer.Common.Helper;
using FileExplorer.Properties;
using PhotoSauce.MagicScaler;
using Vanara.PInvoke;
using Vanara.Windows.Shell;
using static Vanara.PInvoke.Gdi32;

namespace FileExplorer.Helpers
{
    public enum QuickPreviewSourceKind
    {
        ShellCache,
        ImageCache,
        IconFallback,
        Unavailable
    }

    public sealed class QuickPreviewSnapshot
    {
        public QuickPreviewSnapshot(ImageSource imageSource, QuickPreviewSourceKind sourceKind, bool isUpgraded)
        {
            ImageSource = imageSource;
            SourceKind = sourceKind;
            IsUpgraded = isUpgraded;
        }

        public ImageSource ImageSource { get; }

        public QuickPreviewSourceKind SourceKind { get; }

        public bool IsUpgraded { get; }
    }

    public static class PreviewSnapshotHelper
    {
        public static async Task<QuickPreviewSnapshot> GetSnapshotAsync(string filePath, string extension, int requestedSize, CancellationToken cancellationToken)
        {
            if (String.IsNullOrWhiteSpace(filePath))
                return new QuickPreviewSnapshot(null, QuickPreviewSourceKind.Unavailable, false);

            cancellationToken.ThrowIfCancellationRequested();

            QuickPreviewSnapshot shellSnapshot = await TryGetShellSnapshotAsync(filePath, requestedSize, cancellationToken);
            if (shellSnapshot != null)
                return shellSnapshot;

            cancellationToken.ThrowIfCancellationRequested();

            ImageSource icon = null;
            try
            {
                icon = await IconHelper.GetIconAsync(filePath, extension, requestedSize);
            }
            catch
            {
                icon = null;
            }
            if (icon != null)
                return new QuickPreviewSnapshot(icon, QuickPreviewSourceKind.IconFallback, false);

            return new QuickPreviewSnapshot(null, QuickPreviewSourceKind.Unavailable, false);
        }

        public static async Task<QuickPreviewSnapshot> TryUpgradeSnapshotAsync(string filePath, int requestedSize, CancellationToken cancellationToken)
        {
            if (ThumbnailHelper.ThumbnailExists(filePath) == false)
                return null;

            cancellationToken.ThrowIfCancellationRequested();

            ProcessImageSettings settings = CreateProcessImageSettings(requestedSize);
            ImageSource image = await ImageCache.TryGetValue(filePath, settings);

            cancellationToken.ThrowIfCancellationRequested();

            if (image == null)
            {
                try
                {
                    using (FileStream fileStream = File.Open(filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
                    {
                        image = await ImageCache.GetOrAddValue(filePath, fileStream, settings);
                    }
                }
                catch
                {
                    image = null;
                }
            }

            cancellationToken.ThrowIfCancellationRequested();

            return image == null ? null : new QuickPreviewSnapshot(image, QuickPreviewSourceKind.ImageCache, true);
        }

        private static async Task<QuickPreviewSnapshot> TryGetShellSnapshotAsync(string filePath, int requestedSize, CancellationToken cancellationToken)
        {
            ImageSource image = await Task.Run(() =>
            {
                try
                {
                    int shellSize = Convert.ToInt32(Math.Ceiling(requestedSize * App.Dpi));
                    if (shellSize > 512)
                        shellSize = 512;

                    using (ShellItem shellItem = new ShellItem(filePath))
                    using (SafeHBITMAP hBitmap = shellItem.GetImage(new SIZE(shellSize, shellSize), ShellItemGetImageOptions.ThumbnailOnly | ShellItemGetImageOptions.InCacheOnly))
                    {
                        if (hBitmap?.IsInvalid != false)
                            return null;

                        ImageSource imageSource = hBitmap.ToBitmapSource();
                        imageSource.Freeze();

                        return imageSource;
                    }
                }
                catch
                {
                    return null;
                }
            }, cancellationToken);

            return image == null ? null : new QuickPreviewSnapshot(image, QuickPreviewSourceKind.ShellCache, false);
        }

        private static ProcessImageSettings CreateProcessImageSettings(int requestedSize)
        {
            return new ProcessImageSettings
            {
                Anchor = (CropAnchor)Settings.Default.ThumbnailAnchor,
                ResizeMode = (CropScaleMode)Settings.Default.ThumbnailMode,
                Width = requestedSize,
                Height = requestedSize
            };
        }
    }
}
