using System;
using System.IO;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Windows.Media;
using FileExplorer.Common.Helper;
using FileExplorer.Properties;
using PhotoSauce.MagicScaler;
using Vanara.PInvoke;
using Vanara.Windows.Shell;

namespace FileExplorer.Helpers
{
    public class ThumbnailHelper
    {
        public static async Task<ImageSource> GetThumbnailImage(string path)
        {
            return await GetThumbnailImage(path, Settings.Default.ThumbnailHeight, Settings.Default.ThumbnailHeight);
        }

        public static async Task<ImageSource> GetThumbnailImage(string path, int width, int height)
        {
            using (MemoryStream stream = new MemoryStream())
            {
                try
                {
                    ProcessImageSettings settings = CreateProcessImageSettings(width, height);

                    ImageSource imageSource = await ImageCache.TryGetValue(path, settings);
                    if (imageSource == null)
                    {
                        using (FileStream fileStream = File.Open(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
                        {
                            imageSource = await ImageCache.GetOrAddValue(path, fileStream, settings);
                        }
                    }

                    return imageSource;
                }
                catch
                {
                    return null;
                }
            }
        }

        public static bool ThumbnailExists(string path)
        {
            return Regex.Match(path, SupportedImageFormats, RegexOptions.CultureInvariant | RegexOptions.IgnoreCase).Success;
        }

        public static async Task<ImageSource> GetLayoutPreviewImage(string path, int width, int height)
        {
            if (ThumbnailExists(path))
            {
                ImageSource imageSource = await GetThumbnailImage(path, width, height);
                if (imageSource != null)
                    return imageSource;
            }

            return await GetShellThumbnailImage(path, Math.Max(width, height));
        }

        private static ProcessImageSettings CreateProcessImageSettings(int width, int height)
        {
            ProcessImageSettings settings = new ProcessImageSettings();
            settings.Anchor = (CropAnchor)Settings.Default.ThumbnailAnchor;
            settings.ResizeMode = (CropScaleMode)Settings.Default.ThumbnailMode;
            settings.Width = width;
            settings.Height = height;
            return settings;
        }

        private static async Task<ImageSource> GetShellThumbnailImage(string path, int requestedSize)
        {
            return await Task.Run(() =>
            {
                try
                {
                    int shellSize = Convert.ToInt32(Math.Ceiling(requestedSize * App.Dpi));
                    if (shellSize > 512)
                        shellSize = 512;

                    using (ShellItem shellItem = new ShellItem(path))
                    using (var hBitmap = shellItem.GetImage(new SIZE(shellSize, shellSize), ShellItemGetImageOptions.ThumbnailOnly))
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
            });
        }

        private const string SupportedImageFormats = @"^.+\.(?:(?:avif)|(?:bmp)|(?:dip)|(?:gif)|(?:heic)|(?:heif)|(?:jfif)|(?:jpe)|(?:jpe?g)|(?:jxr)|(?:png)|(?:rle)|(?:tiff?)|(?:wdp)|(?:webp))$";
    }
}
