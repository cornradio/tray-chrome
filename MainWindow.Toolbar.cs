using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;

namespace TrayChrome
{
    internal static class ToolbarLayout
    {
        public static readonly List<string> DefaultItems = new() { "back", "forward", "refresh", "bookmarks", "history", "pin", "zoom", "settings" };
        public static readonly (string Id, string Label, string Button, string Menu)[] Items =
        {
            ("back", "后退", "BackButton", "BackMenuItem"),
            ("forward", "前进", "ForwardButton", "ForwardMenuItem"),
            ("refresh", "刷新", "RefreshButton", "RefreshMenuItem"),
            ("bookmarks", "收藏夹", "BookmarkButton", "BookmarkMenuItem"),
            ("history", "历史记录", "HistoryButton", "HistoryMenuItem"),
            ("pin", "置顶", "TopMostButton", "PinMenuItem"),
            ("zoom", "缩放", "ZoomButton", "ZoomMenuItem"),
            ("browser", "用默认浏览器打开", "OpenBrowserButton", "OpenBrowserMenuItem"),
            ("ua", "用户代理（UA）", "UserAgentButton", "UserAgentMenuItem"),
            ("theme", "黑白模式", "ThemeButton", "DarkModeMenuItem"),
            ("proxy", "代理", "ProxyButton", "ProxyMenuItem"),
            ("settings", "设置", "SettingsButton", "SettingsMenuItem")
        };
    }

    public partial class MainWindow
    {
        private HistoryWindow historyWindow;

        private void HistoryButton_Click(object sender, RoutedEventArgs e)
        {
            if (historyWindow == null)
            {
                historyWindow = new HistoryWindow(this);
                historyWindow.Closed += (_, _) => historyWindow = null;
            }
            historyWindow.Show();
            if (historyWindow.WindowState == WindowState.Minimized) historyWindow.WindowState = WindowState.Normal;
            historyWindow.Activate();
        }

        private void ApplyToolbarLayout()
        {
            ZoomPopup.IsOpen = false;
            MoreContextMenu.IsOpen = false;
            var selected = new HashSet<string>(appSettings.BottomToolbarItems ?? ToolbarLayout.DefaultItems);
            BottomToolbarGrid.ColumnDefinitions.Clear();
            int column = 0;
            foreach (var item in ToolbarLayout.Items)
            {
                var button = (Button)FindName(item.Button);
                var menu = (MenuItem)FindName(item.Menu);
                bool onToolbar = selected.Contains(item.Id);
                button.Visibility = onToolbar ? Visibility.Visible : Visibility.Collapsed;
                menu.Visibility = onToolbar ? Visibility.Collapsed : Visibility.Visible;
                if (!onToolbar) continue;
                BottomToolbarGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                button.Width = 30;
                Grid.SetColumn(button, column++);
            }
            BottomToolbarGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(30) });
            Grid.SetColumn(MoreButton, column++);
            BottomToolbarGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            Grid.SetColumn(DragButton, column++);
            BottomToolbarGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(20) });
            Grid.SetColumn(ResizeButton, column);
            UpdateToolbarVisibility();
        }

        private Button VisibleMoreButton => TopMoreButton.Visibility == Visibility.Visible ? TopMoreButton : MoreButton;

        private void UpdateToolbarVisibility()
        {
            bool empty = true;
            bool all = true;
            foreach (var item in ToolbarLayout.Items)
            {
                if (((Button)FindName(item.Button)).Visibility == Visibility.Visible) empty = false;
                else all = false;
            }
            MoreButton.Visibility = all ? Visibility.Collapsed : Visibility.Visible;
            BottomToolbarGrid.ColumnDefinitions[Grid.GetColumn(MoreButton)].Width = new GridLength(all ? 0 : 30);
            TopMoreButton.Visibility = empty ? Visibility.Visible : Visibility.Collapsed;
            BottomToolbar.Visibility = empty || isSuperMinimalMode ? Visibility.Collapsed : Visibility.Visible;
            MainContentGrid.RowDefinitions[2].Height = new GridLength(empty || isSuperMinimalMode ? 0 : 40);
        }

        private void UpdateToolbarIcons()
        {
            ThemeIcon.Data = System.Windows.Media.Geometry.Parse(isDarkMode
                ? "M 10,2 A 6,6 0 1 0 14,10 A 6,6 0 0 1 10,2 Z"
                : "M 11,8 A 3,3 0 1 1 5,8 A 3,3 0 1 1 11,8 M 8,1 L 8,3 M 8,13 L 8,15 M 1,8 L 3,8 M 13,8 L 15,8 M 3,3 L 4.5,4.5 M 11.5,11.5 L 13,13 M 3,13 L 4.5,11.5 M 11.5,4.5 L 13,3");
            ThemeButton.ToolTip = isDarkMode ? "切换到亮色模式" : "切换到暗色模式";
            UserAgentIcon.Data = System.Windows.Media.Geometry.Parse(isMobileUA
                ? "M 4.5,1.5 L 11.5,1.5 L 11.5,14.5 L 4.5,14.5 Z M 7,12 L 9,12"
                : "M 1.5,2.5 L 14.5,2.5 L 14.5,11 L 1.5,11 Z M 8,11 L 8,14 M 5,14 L 11,14");
            UserAgentButton.ToolTip = isMobileUA ? "当前：手机 UA（点击切换桌面）" : "当前：桌面 UA（点击切换手机）";
            ProxyButton.Foreground = appSettings.IsProxyEnabled ? System.Windows.Media.Brushes.DodgerBlue : ThemeButton.Foreground;
            ProxyButton.ToolTip = appSettings.IsProxyEnabled ? "代理已开启" : "代理未开启";
        }

        private void ToolbarBookmarks_Click(object sender, RoutedEventArgs e)
        {
            BookmarkContextMenu.PlacementTarget = VisibleMoreButton;
            BookmarkContextMenu.Placement = VisibleMoreButton == TopMoreButton ? PlacementMode.Bottom : PlacementMode.Top;
            BookmarkContextMenu.IsOpen = true;
        }

        private void SettingsButton_Click(object sender, RoutedEventArgs e)
        {
            (Application.Current as App)?.OpenSettings();
        }

        private void ToolbarProxy_Click(object sender, RoutedEventArgs e)
        {
            ProxyButton.ContextMenu.PlacementTarget = ProxyButton;
            ProxyButton.ContextMenu.Placement = PlacementMode.Top;
            ProxyButton.ContextMenu.IsOpen = true;
        }

        private void ToolbarProxyMenu_Opened(object sender, RoutedEventArgs e)
        {
            BrowserMenu_Opened(sender, e);
            ToolbarProxyToggle.IsChecked = appSettings.IsProxyEnabled;
        }
    }
}
