using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Input;
using Microsoft.Web.WebView2.Wpf;

namespace TrayChrome
{
    public partial class MainWindow
    {
        private sealed class BrowserTab
        {
            public WebView2 View { get; set; }
            public string Url { get; set; }
            public Button Header { get; set; }
            public TextBlock Divider { get; set; }
            public List<HistoryItem> History { get; set; } = new();
            public int HistoryIndex { get; set; } = -1;
            public List<Microsoft.Web.WebView2.Core.CoreWebView2Frame> Frames { get; } = new();
            public string ScriptId { get; set; }
            public AdBlocker Blocker { get; } = new();
        }

        private readonly List<BrowserTab> tabs = new();
        private BrowserTab activeTab;
        private bool tabInitializing;
        public int ActiveTabIndex => tabs.IndexOf(activeTab);
        private bool startupTabsRestoring = true;
        private bool tabsReady;
        public bool TabsReady { get => !startupTabsRestoring && tabsReady; private set => tabsReady = value; }

        private void ApplyTabSettings()
        {
            foreach (var tab in tabs)
            {
                if (tab.View.CoreWebView2 == null) continue;
                tab.View.ZoomFactor = currentZoomFactor;
                tab.View.CoreWebView2.Settings.UserAgent = isMobileUA ? MobileUA : DesktopUA;
                tab.Blocker.BlockRules = adBlocker.BlockRules;
                tab.Blocker.AllowRules = adBlocker.AllowRules;
                tab.Blocker.IsEnabled = adBlocker.IsEnabled;
            }
        }

        private void RegisterTab(WebView2 view, string url)
        {
            view.SizeChanged += (_, _) =>
                Dispatcher.BeginInvoke(new Action(ApplyRoundedCorners), System.Windows.Threading.DispatcherPriority.Loaded);
            var tab = new BrowserTab { View = view, Url = url };
            tab.Header = new Button { Content = "新标签页", MaxWidth = 160,
                Style = (Style)FindResource("TabHeaderStyle") };
            tab.Header.Click += (_, _) => SelectTab(tab);
            tab.Header.PreviewMouseDown += (_, args) =>
            {
                if (args.ChangedButton != MouseButton.Middle) return;
                args.Handled = true;
                CloseTab(tab);
            };
            var menu = CreateBrowserMenu();
            void AddAction(string title, Action action)
            {
                var item = new MenuItem { Header = title };
                item.Click += (_, _) => action();
                menu.Items.Add(item);
            }
            AddAction("关闭标签页", () => CloseTab(tab));
            AddAction("关闭其他标签页", () =>
            {
                foreach (var other in tabs.Where(item => item != tab).ToArray()) CloseTab(other);
            });
            AddAction("关闭右侧标签页", () =>
            {
                foreach (var other in tabs.Skip(tabs.IndexOf(tab) + 1).ToArray()) CloseTab(other);
            });
            menu.Items.Add(new Separator());
            AddAction("刷新", () => tab.View.CoreWebView2?.Reload());
            tab.Header.ContextMenu = menu;
            var panel = new StackPanel { Orientation = Orientation.Horizontal, Tag = tab };
            panel.Children.Add(tab.Header);
            tab.Divider = new TextBlock { Text = "|", VerticalAlignment = VerticalAlignment.Center, Opacity = 0.3 };
            panel.Children.Add(tab.Divider);
            TabHeaders.Children.Add(panel);
            tabs.Add(tab);
            UpdateTabBarVisibility();
            SelectTab(tab);
        }

        private void UpdateTabBarVisibility()
        {
            TabToolbar.Visibility = !isSuperMinimalMode && tabs.Count > 1
                ? Visibility.Visible : Visibility.Collapsed;
        }

        private void SelectTab(BrowserTab tab)
        {
            if (tabInitializing || activeTab == tab) return;
            if (activeTab != null)
            {
                activeTab.HistoryIndex = historyIndex;
                activeTab.History = historyList;
                activeTab.View.Visibility = Visibility.Collapsed;
            }
            bool blockEnabled = adBlocker.IsEnabled;
            var blockRules = adBlocker.BlockRules;
            var allowRules = adBlocker.AllowRules;
            activeTab = tab;
            webView = tab.View;
            historyList = tab.History;
            historyIndex = tab.HistoryIndex;
            adBlocker = tab.Blocker;
            adBlocker.BlockRules = blockRules;
            adBlocker.AllowRules = allowRules;
            adBlocker.IsEnabled = blockEnabled;
            isNavigatingFromHistory = false;
            webView.Visibility = Visibility.Visible;
            Dispatcher.BeginInvoke(new Action(ApplyRoundedCorners), System.Windows.Threading.DispatcherPriority.Loaded);
            AddressBar.Text = webView.CoreWebView2?.Source ?? tab.Url;
            BackButton.IsEnabled = webView.CoreWebView2?.CanGoBack == true;
            ForwardButton.IsEnabled = webView.CoreWebView2?.CanGoForward == true;
            UpdateTabTheme(isDarkMode);
            if (webView.CoreWebView2 != null)
            {
                webView.ZoomFactor = currentZoomFactor;
                webView.CoreWebView2.Settings.UserAgent = isMobileUA ? MobileUA : DesktopUA;
                UpdateTrayTooltip();
                UpdateBrowserDrag();
                webView.Focus();
            }
        }

        private void UpdateTabTheme(bool darkMode)
        {
            var foreground = darkMode ? Brushes.White : new SolidColorBrush(Color.FromRgb(0x33, 0x33, 0x33));
            NewTabButton.Foreground = foreground;
            foreach (var item in tabs)
            {
                item.Header.Foreground = foreground;
                item.Header.FontWeight = item == activeTab ? FontWeights.SemiBold : FontWeights.Normal;
                item.Header.Opacity = item == activeTab ? 1 : 0.6;
                item.Divider.Foreground = foreground;
            }
        }

        public async Task OpenTabAsync(string url = "https://tva.cornradio.org/?name=search")
        {
            if (tabInitializing)
            {
                MessageBox.Show("标签页正在初始化，请稍后再试。", "提示", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }
            var view = new WebView2();
            BrowserHost.Children.Add(view);
            RegisterTab(view, url);
            TabsReady = false;
            try { await InitializeWebViewInternal(url); }
            finally { TabsReady = tabs.All(tab => tab.View.CoreWebView2 != null); }
        }

        private async void NewTab_Click(object sender, RoutedEventArgs e) => await OpenTabAsync();

        private async void CloseTab(BrowserTab tab)
        {
            await browserInputLock.WaitAsync();
            try
            {
                if (tabInitializing || !tabs.Contains(tab)) return;
                if (tabs.Count == 1) { HideWithAnimation(); return; }
                if (tab == activeTab)
                    SelectTab(tabs[tabs.IndexOf(tab) == 0 ? 1 : tabs.IndexOf(tab) - 1]);
                tabs.Remove(tab);
                UpdateTabBarVisibility();
                var panel = TabHeaders.Children.Cast<StackPanel>().Single(item => item.Tag == tab);
                TabHeaders.Children.Remove(panel);
                BrowserHost.Children.Remove(tab.View);
                tab.View.Dispose();
                TabsReady = tabs.All(item => item.View.CoreWebView2 != null);
            }
            catch (Exception ex)
            {
                (Application.Current as App)?.WriteDiagnostic("Closing browser tab failed", ex);
                MessageBox.Show($"关闭标签页失败: {ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
            finally { browserInputLock.Release(); }
        }

        public List<string> CaptureTabUrls() => tabs.Select(tab => tab.View.CoreWebView2?.Source ?? tab.Url).ToList();

        private void RefreshTabTitle(WebView2 view)
        {
            var tab = tabs.FirstOrDefault(item => item.View == view);
            if (tab == null) return;
            tab.Url = view.CoreWebView2.Source;
            tab.Header.Content = string.IsNullOrWhiteSpace(view.CoreWebView2.DocumentTitle)
                ? tab.Url : view.CoreWebView2.DocumentTitle;
            tab.Header.ToolTip = tab.Url;
        }
    }
}
