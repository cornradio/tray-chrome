using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;

namespace TrayChrome
{
    internal static class ToolbarLayout
    {
        public static readonly List<string> DefaultItems = new() { "back", "forward", "refresh", "bookmarks", "pin", "zoom", "settings" };
        public static readonly (string Id, string Label, string Button, string Menu)[] Items =
        {
            ("back", "后退", "BackButton", "BackMenuItem"),
            ("forward", "前进", "ForwardButton", "ForwardMenuItem"),
            ("refresh", "刷新", "RefreshButton", "RefreshMenuItem"),
            ("bookmarks", "收藏夹", "BookmarkButton", "BookmarkMenuItem"),
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
                button.Width = item.Id == "zoom" ? 38 : 30;
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
            foreach (var item in ToolbarLayout.Items)
                if (((Button)FindName(item.Button)).Visibility == Visibility.Visible) empty = false;
            TopMoreButton.Visibility = empty ? Visibility.Visible : Visibility.Collapsed;
            BottomToolbar.Visibility = empty || isSuperMinimalMode ? Visibility.Collapsed : Visibility.Visible;
            MainContentGrid.RowDefinitions[2].Height = new GridLength(empty || isSuperMinimalMode ? 0 : 40);
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
