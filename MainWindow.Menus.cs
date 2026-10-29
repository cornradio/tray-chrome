using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace TrayChrome
{
    public partial class MainWindow
    {
        private ContextMenu CreateBrowserMenu()
        {
            var menu = new ContextMenu { Style = (Style)FindResource("BrowserContextMenuStyle") };
            menu.Opened += BrowserMenu_Opened;
            return menu;
        }

        private void BrowserMenu_Opened(object sender, RoutedEventArgs e)
        {
            if (sender is not ContextMenu menu) return;
            menu.Background = TopToolbar.Background;
            menu.Foreground = isDarkMode ? Brushes.White : new SolidColorBrush(Color.FromRgb(0x33, 0x33, 0x33));
            menu.BorderBrush = TopToolbar.BorderBrush;
            menu.Resources["BrowserMenuHover"] = new SolidColorBrush(isDarkMode
                ? Color.FromRgb(0x50, 0x50, 0x50) : Color.FromRgb(0xE5, 0xE5, 0xE5));
            menu.Resources["BrowserMenuDivider"] = TopToolbar.BorderBrush;
            menu.Resources["BrowserMenuBackground"] = TopToolbar.Background;
        }
    }
}
