using System;
using System.ComponentModel;
using System.Windows.Media;
using FileExplorer.Core;
using FileExplorer.Helpers;
using FileExplorer.Properties;

namespace FileExplorer.Model
{
    public partial class FileModel
    {
        public ImageSource SmallIcon
        {
            get
            {
                if (smallIcon == null)
                {
                    smallIcon = NotifyTask.Create(IconHelper.GetIconAsync(FullPath, Extension, 16));
                    smallIcon.PropertyChanged += (s, e) =>
                    {
                        if (e.PropertyName == nameof(NotifyTask.IsCompleted) && smallIcon.IsCompleted)
                            RaisePropertyChanged(SmallIconChangedEventArgs);
                    };
                }

                if (smallIcon.IsFaulted)
                    return null;

                if (smallIcon.IsCompleted && smallIcon.Result == null)
                {
                    smallIcon = null;
                    return null;
                }

                return smallIcon.IsCompleted ? smallIcon.Result : null;
            }
        }
        private NotifyTask<ImageSource> smallIcon;

        public ImageSource MediumIcon
        {
            get
            {
                if (mediumIcon == null)
                {
                    mediumIcon = NotifyTask.Create(IconHelper.GetIconAsync(FullPath, Extension, 32));
                    mediumIcon.PropertyChanged += (s, e) =>
                    {
                        if (e.PropertyName == nameof(NotifyTask.IsCompleted) && mediumIcon.IsCompleted)
                        {
                            RaisePropertyChanged(MediumIconChangedEventArgs);
                            RaisePropertyChanged(MediumLayoutPreviewImageChangedEventArgs);
                            RaisePropertyChanged(MediumDisplayImageChangedEventArgs);
                        }
                    };
                }

                if (mediumIcon.IsFaulted)
                    return null;

                if (mediumIcon.IsCompleted && mediumIcon.Result == null)
                {
                    mediumIcon = null;
                    return null;
                }

                return mediumIcon.IsCompleted ? mediumIcon.Result : null;
            }
        }
        private NotifyTask<ImageSource> mediumIcon;

        public ImageSource LargeIcon
        {
            get
            {
                if (largeIcon == null)
                {
                    largeIcon = NotifyTask.Create(IconHelper.GetIconAsync(FullPath, Extension, 48));
                    largeIcon.PropertyChanged += (s, e) =>
                    {
                        if (e.PropertyName == nameof(NotifyTask.IsCompleted) && largeIcon.IsCompleted)
                        {
                            RaisePropertyChanged(LargeIconChangedEventArgs);
                            RaisePropertyChanged(LargeLayoutPreviewImageChangedEventArgs);
                            RaisePropertyChanged(LargeDisplayImageChangedEventArgs);
                        }
                    };
                }

                if (largeIcon.IsFaulted)
                    return null;

                if (largeIcon.IsCompleted && largeIcon.Result == null)
                {
                    largeIcon = null;
                    return null;
                }

                return largeIcon.IsCompleted ? largeIcon.Result : null;
            }
        }
        private NotifyTask<ImageSource> largeIcon;

        public ImageSource ThumbnailImage
        {
            get
            {
                if (ThumbnailHelper.ThumbnailExists(FullPath) == false)
                    return ExtraLargeIcon;

                if (thumbnailImage == null)
                {
                    thumbnailImage = NotifyTask.Create(ThumbnailHelper.GetThumbnailImage(FullPath));
                    thumbnailImage.PropertyChanged += (s, e) =>
                    {
                        if (e.PropertyName == nameof(NotifyTask.IsCompleted) && thumbnailImage.IsCompleted)
                            RaisePropertyChanged(ThumbnailImageChangedEventArgs);
                    };
                }

                return thumbnailImage.IsCompleted ? thumbnailImage.Result : ExtraLargeIcon;
            }
        }
        private NotifyTask<ImageSource> thumbnailImage;

        public ImageSource ExtraLargeIcon
        {
            get
            {
                if (extraLargeIcon == null)
                {
                    extraLargeIcon = NotifyTask.Create(IconHelper.GetIconAsync(FullPath, Extension, 256));
                    extraLargeIcon.PropertyChanged += (s, e) =>
                    {
                        if (e.PropertyName == nameof(NotifyTask.IsCompleted) && extraLargeIcon.IsCompleted)
                        {
                            RaisePropertyChanged(ExtraLargeIconChangedEventArgs);
                            RaisePropertyChanged(ThumbnailImageChangedEventArgs);
                            RaisePropertyChanged(ExtraLargeLayoutPreviewImageChangedEventArgs);
                            RaisePropertyChanged(ThumbnailLayoutPreviewImageChangedEventArgs);
                            RaisePropertyChanged(ExtraLargeDisplayImageChangedEventArgs);
                        }
                    };
                }

                if (extraLargeIcon.IsFaulted)
                    return null;

                if (extraLargeIcon.IsCompleted && extraLargeIcon.Result == null)
                {
                    extraLargeIcon = null;
                    return null;
                }

                return extraLargeIcon.IsCompleted ? extraLargeIcon.Result : null;
            }
        }
        private NotifyTask<ImageSource> extraLargeIcon;

        public ImageSource MediumLayoutPreviewImage
        {
            get
            {
                return GetLayoutPreviewImage(
                    ref mediumLayoutPreviewImage,
                    120,
                    90,
                    () => MediumIcon,
                    MediumLayoutPreviewImageChangedEventArgs,
                    MediumDisplayImageChangedEventArgs);
            }
        }
        private NotifyTask<ImageSource> mediumLayoutPreviewImage;

        public ImageSource MediumDisplayImage => Settings.Default.ShowLayoutImagePreviews ? MediumLayoutPreviewImage : MediumIcon;

        public ImageSource LargeLayoutPreviewImage
        {
            get
            {
                return GetLayoutPreviewImage(
                    ref largeLayoutPreviewImage,
                    180,
                    135,
                    () => LargeIcon,
                    LargeLayoutPreviewImageChangedEventArgs,
                    LargeDisplayImageChangedEventArgs);
            }
        }
        private NotifyTask<ImageSource> largeLayoutPreviewImage;

        public ImageSource LargeDisplayImage => Settings.Default.ShowLayoutImagePreviews ? LargeLayoutPreviewImage : LargeIcon;

        public ImageSource ExtraLargeLayoutPreviewImage
        {
            get
            {
                return GetLayoutPreviewImage(
                    ref extraLargeLayoutPreviewImage,
                    400,
                    300,
                    () => ExtraLargeIcon,
                    ExtraLargeLayoutPreviewImageChangedEventArgs,
                    ExtraLargeDisplayImageChangedEventArgs);
            }
        }
        private NotifyTask<ImageSource> extraLargeLayoutPreviewImage;

        public ImageSource ExtraLargeDisplayImage => Settings.Default.ShowLayoutImagePreviews ? ExtraLargeLayoutPreviewImage : ExtraLargeIcon;

        public ImageSource ThumbnailLayoutPreviewImage
        {
            get
            {
                return GetLayoutPreviewImage(
                    ref thumbnailLayoutPreviewImage,
                    Settings.Default.ThumbnailWidth,
                    Settings.Default.ThumbnailHeight,
                    () => ExtraLargeIcon,
                    ThumbnailLayoutPreviewImageChangedEventArgs);
            }
        }
        private NotifyTask<ImageSource> thumbnailLayoutPreviewImage;

        private void ResetIconPreviewState()
        {
            smallIcon = null;
            mediumIcon = null;
            largeIcon = null;
            extraLargeIcon = null;

            RaisePropertyChanged(SmallIconChangedEventArgs);
            RaisePropertyChanged(MediumIconChangedEventArgs);
            RaisePropertyChanged(LargeIconChangedEventArgs);
            RaisePropertyChanged(ExtraLargeIconChangedEventArgs);
            RaisePropertyChanged(MediumDisplayImageChangedEventArgs);
            RaisePropertyChanged(LargeDisplayImageChangedEventArgs);
            RaisePropertyChanged(ExtraLargeDisplayImageChangedEventArgs);
        }

        private void InvalidateThumbnailPreviewState()
        {
            thumbnailImage = null;
            RaisePropertyChanged(ThumbnailImageChangedEventArgs);
        }

        private void InvalidateLayoutPreviewState()
        {
            mediumLayoutPreviewImage = null;
            largeLayoutPreviewImage = null;
            extraLargeLayoutPreviewImage = null;
            thumbnailLayoutPreviewImage = null;

            RaisePropertyChanged(MediumLayoutPreviewImageChangedEventArgs);
            RaisePropertyChanged(LargeLayoutPreviewImageChangedEventArgs);
            RaisePropertyChanged(ExtraLargeLayoutPreviewImageChangedEventArgs);
            RaisePropertyChanged(ThumbnailLayoutPreviewImageChangedEventArgs);
            RaisePropertyChanged(MediumDisplayImageChangedEventArgs);
            RaisePropertyChanged(LargeDisplayImageChangedEventArgs);
            RaisePropertyChanged(ExtraLargeDisplayImageChangedEventArgs);
        }

        private ImageSource GetLayoutPreviewImage(
            ref NotifyTask<ImageSource> previewTask,
            int width,
            int height,
            Func<ImageSource> fallbackFactory,
            PropertyChangedEventArgs propertyChangedEventArgs,
            PropertyChangedEventArgs displayPropertyChangedEventArgs = null)
        {
            ImageSource fallbackImage = fallbackFactory();

            if (IsDirectory || IsImage == false)
                return fallbackImage;

            if (previewTask == null)
            {
                previewTask = NotifyTask.Create(ThumbnailHelper.GetLayoutPreviewImage(FullPath, width, height));
                NotifyTask<ImageSource> currentPreviewTask = previewTask;
                currentPreviewTask.PropertyChanged += (s, e) =>
                {
                    if (e.PropertyName == nameof(NotifyTask.IsCompleted) && currentPreviewTask.IsCompleted)
                    {
                        RaisePropertyChanged(propertyChangedEventArgs);
                        if (displayPropertyChangedEventArgs != null)
                            RaisePropertyChanged(displayPropertyChangedEventArgs);
                    }
                };
            }

            return previewTask.IsCompleted ? previewTask.Result ?? fallbackImage : fallbackImage;
        }

        private static PropertyChangedEventArgs SmallIconChangedEventArgs = new PropertyChangedEventArgs(nameof(SmallIcon));

        private static PropertyChangedEventArgs MediumIconChangedEventArgs = new PropertyChangedEventArgs(nameof(MediumIcon));

        private static PropertyChangedEventArgs LargeIconChangedEventArgs = new PropertyChangedEventArgs(nameof(LargeIcon));

        private static PropertyChangedEventArgs ExtraLargeIconChangedEventArgs = new PropertyChangedEventArgs(nameof(ExtraLargeIcon));

        private static PropertyChangedEventArgs ThumbnailImageChangedEventArgs = new PropertyChangedEventArgs(nameof(ThumbnailImage));

        private static PropertyChangedEventArgs MediumLayoutPreviewImageChangedEventArgs = new PropertyChangedEventArgs(nameof(MediumLayoutPreviewImage));

        private static PropertyChangedEventArgs MediumDisplayImageChangedEventArgs = new PropertyChangedEventArgs(nameof(MediumDisplayImage));

        private static PropertyChangedEventArgs LargeLayoutPreviewImageChangedEventArgs = new PropertyChangedEventArgs(nameof(LargeLayoutPreviewImage));

        private static PropertyChangedEventArgs LargeDisplayImageChangedEventArgs = new PropertyChangedEventArgs(nameof(LargeDisplayImage));

        private static PropertyChangedEventArgs ExtraLargeLayoutPreviewImageChangedEventArgs = new PropertyChangedEventArgs(nameof(ExtraLargeLayoutPreviewImage));

        private static PropertyChangedEventArgs ExtraLargeDisplayImageChangedEventArgs = new PropertyChangedEventArgs(nameof(ExtraLargeDisplayImage));

        private static PropertyChangedEventArgs ThumbnailLayoutPreviewImageChangedEventArgs = new PropertyChangedEventArgs(nameof(ThumbnailLayoutPreviewImage));
    }
}
