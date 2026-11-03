using System;
using System.Windows;
using System.Windows.Media;

namespace TrayChrome
{
    internal static class AppTheme
    {
        private static bool initialized;

        public static void EnsureInitialized()
        {
            if (initialized) return;
            Application.Current.Resources.MergedDictionaries.Add(new ResourceDictionary
            {
                Source = new Uri("/TrayChrome;component/Resources/AppTheme.xaml", UriKind.Relative)
            });
            initialized = true;
            EventManager.RegisterClassHandler(typeof(Window), FrameworkElement.LoadedEvent,
                new RoutedEventHandler(WindowLoaded));
            SetDarkMode(false);
        }

        public static void SetDarkMode(bool dark)
        {
            EnsureInitialized();
            var resources = Application.Current.Resources;
            void Set(string key, string color) => resources[key] =
                new SolidColorBrush((Color)ColorConverter.ConvertFromString(color));
            Set("ThemeBackground", dark ? "#252526" : "#F5F5F5");
            Set("ThemeSurface", dark ? "#333337" : "#FFFFFF");
            Set("ThemeForeground", dark ? "#EEEEEE" : "#252525");
            Set("ThemeMuted", dark ? "#B0B0B0" : "#666666");
            Set("ThemeBorder", dark ? "#606064" : "#CCCCCC");
            Set("ThemeHover", dark ? "#494950" : "#E5EAF0");
            Set("ThemeSelection", dark ? "#235784" : "#CCE4FA");
            resources[SystemColors.WindowBrushKey] = resources["ThemeSurface"];
            resources[SystemColors.WindowTextBrushKey] = resources["ThemeForeground"];
            resources[SystemColors.ControlBrushKey] = resources["ThemeBackground"];
            resources[SystemColors.ControlTextBrushKey] = resources["ThemeForeground"];
            resources[SystemColors.GrayTextBrushKey] = resources["ThemeMuted"];
            resources[SystemColors.HighlightBrushKey] = resources["ThemeSelection"];
            resources[SystemColors.HighlightTextBrushKey] = resources["ThemeForeground"];
            foreach (Window window in Application.Current.Windows) ApplyWindow(window);
        }

        private static void WindowLoaded(object sender, RoutedEventArgs e)
        {
            if (sender is Window window && e.OriginalSource == window) ApplyWindow(window);
        }

        private static void ApplyWindow(Window window)
        {
            if (window is MainWindow) return;
            window.SetResourceReference(Window.BackgroundProperty, "ThemeBackground");
            window.SetResourceReference(Window.ForegroundProperty, "ThemeForeground");
        }
    }
}
