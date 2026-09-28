using System;
using DevExpress.Xpf.Core;

namespace FileExplorer.Core
{
    public static class UiRuntimeBootstrap
    {
        public static IDisposable Initialize(string themeName, Action<string> onThemeChanged = null)
        {
            if (!runtimeInitialized)
            {
                CompatibilitySettings.UseLightweightThemes = true;
                Theme.RegisterPredefinedPaletteThemes();
                runtimeInitialized = true;
            }

            ApplicationThemeHelper.ApplicationThemeName = themeName;
            return onThemeChanged == null
                ? EmptySubscription.Instance
                : new ThemeSubscription(onThemeChanged);
        }

        private static bool runtimeInitialized;

        private sealed class ThemeSubscription : IDisposable
        {
            public ThemeSubscription(Action<string> onThemeChanged)
            {
                this.onThemeChanged = onThemeChanged;
                LightweightThemeManager.CurrentThemeChanged += CurrentThemeChanged;
            }

            public void Dispose()
            {
                if (disposed)
                    return;

                LightweightThemeManager.CurrentThemeChanged -= CurrentThemeChanged;
                disposed = true;
            }

            private void CurrentThemeChanged(object sender, ValueChangedEventArgs<LightweightTheme> e)
            {
                onThemeChanged(ApplicationThemeHelper.ApplicationThemeName);
            }

            private readonly Action<string> onThemeChanged;
            private bool disposed;
        }

        private sealed class EmptySubscription : IDisposable
        {
            public static readonly EmptySubscription Instance = new EmptySubscription();

            public void Dispose()
            {
            }
        }
    }
}
