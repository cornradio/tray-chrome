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
        private BrowserTab tabDragCandidate;
        private Point tabDragStart;
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
                Style = (Style)FindResource("TabHeaderStyle"), AllowDrop = true, Tag = tab };
            tab.Header.Click += (_, _) => SelectTab(tab);
            tab.Header.PreviewMouseLeftButtonDown += (_, args) =>
            {
                tabDragCandidate = tab;
                tabDragStart = args.GetPosition(tab.Header);
            };
            tab.Header.MouseMove += TabHeader_MouseMove;
            tab.Header.DragOver += TabHeader_DragOver;
            tab.Header.Drop += TabHeader_Drop;
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
            AddAction("用新窗口打开", () => OpenTabInNewWindow(tab));
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

        private void TabHeader_MouseMove(object sender, MouseEventArgs e)
        {
            if (sender is not Button header || tabDragCandidate == null
                || header != tabDragCandidate.Header || e.LeftButton != MouseButtonState.Pressed)
                return;
            var delta = e.GetPosition(header) - tabDragStart;
            if (Math.Abs(delta.X) < SystemParameters.MinimumHorizontalDragDistance
                && Math.Abs(delta.Y) < SystemParameters.MinimumVerticalDragDistance)
                return;
            var dragging = tabDragCandidate;
            tabDragCandidate = null;
            DragDrop.DoDragDrop(header, dragging, DragDropEffects.Move);
        }

        private void TabHeader_DragOver(object sender, DragEventArgs e)
        {
            e.Effects = sender is Button target
                && e.Data.GetData(typeof(BrowserTab)) is BrowserTab source
                && source != target.Tag
                && tabs.Contains(source)
                && tabs.Contains(target.Tag as BrowserTab)
                    ? DragDropEffects.Move : DragDropEffects.None;
            e.Handled = true;
        }

        private void TabHeader_Drop(object sender, DragEventArgs e)
        {
            e.Handled = true;
            if (sender is not Button target
                || e.Data.GetData(typeof(BrowserTab)) is not BrowserTab source
                || target.Tag is not BrowserTab destination
                || source == destination)
                return;

            int sourceIndex = tabs.IndexOf(source);
            int targetIndex = tabs.IndexOf(destination);
            if (sourceIndex < 0 || targetIndex < 0) return;
            int insertionIndex = targetIndex + (e.GetPosition(target).X >= target.ActualWidth / 2 ? 1 : 0);
            if (sourceIndex < insertionIndex) insertionIndex--;
            tabs.RemoveAt(sourceIndex);
            tabs.Insert(Math.Clamp(insertionIndex, 0, tabs.Count), source);

            var panels = tabs.Select(tab => TabHeaders.Children
                .OfType<StackPanel>().Single(panel => panel.Tag == tab)).ToArray();
            TabHeaders.Children.Clear();
            foreach (var panel in panels) TabHeaders.Children.Add(panel);
            UpdateTabTheme(isDarkMode);
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
            addressBarEdited = false;
            SetAddressBarText(webView.CoreWebView2?.Source ?? tab.Url);
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

        public async Task OpenTabAsync(string url = "https://tva.cornradio.org/?name=search", bool focusAddressBar = false)
        {
            if (tabInitializing)
            {
                MessageBox.Show("标签页正在初始化，请稍后再试。", "提示", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }
            var view = new WebView2();
            BrowserHost.Children.Add(view);
            RegisterTab(view, url);
            var tab = tabs.Last();
            TabsReady = false;
            try { await InitializeWebViewInternal(url); }
            finally { TabsReady = tabs.All(item => item.View.CoreWebView2 != null); }
            if (focusAddressBar && tab == activeTab)
            {
                addressBarEdited = false;
                FocusAddressBar();
                // WebView2 创建原生窗口后可能抢走焦点，输入阶段再聚焦一次。
                _ = Dispatcher.BeginInvoke(new Action(() =>
                {
                    if (tab == activeTab && !addressBarEdited) FocusAddressBar();
                }), System.Windows.Threading.DispatcherPriority.Input);
            }
        }

        private async void NewTab_Click(object sender, RoutedEventArgs e) => await OpenTabAsync(focusAddressBar: true);

        private void FocusAddressBar()
        {
            if (isSuperMinimalMode) ToggleSuperMinimalMode(false);
            AddressBar.Focus();
            Keyboard.Focus(AddressBar);
            AddressBar.SelectAll();
        }

        private void OpenTabInNewWindow(BrowserTab tab)
        {
            string url = tab.View.CoreWebView2?.Source ?? tab.Url;
            try { (Application.Current as App)?.OpenUrlInNewInstance(url); }
            catch (Exception ex)
            {
                (Application.Current as App)?.WriteDiagnostic("Open tab in new window failed", ex);
                MessageBox.Show($"用新窗口打开失败: {ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

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

        // 按「当前页 + 其他标签页」顺序对齐标签：同位置地址不同则导航，多出的新开，缺少的关闭。
        public async Task ApplyTabUrlsAsync(IReadOnlyList<string> urls)
        {
            if (urls.Count == 0) throw new ArgumentException("至少需要一个网页。", nameof(urls));
            if (tabInitializing) throw new InvalidOperationException("标签页正在初始化，请稍后再试。");
            var current = activeTab;
            var ordered = new[] { current }.Concat(tabs.Where(tab => tab != current)).ToList();
            for (int i = 0; i < Math.Min(ordered.Count, urls.Count); i++)
            {
                var tab = ordered[i];
                string source = tab.View.CoreWebView2?.Source ?? tab.Url;
                if (string.Equals(source, urls[i], StringComparison.Ordinal)) continue;
                tab.Url = urls[i];
                tab.View.CoreWebView2?.Navigate(urls[i]);
                if (tab == current && !(AddressBar.IsKeyboardFocused && addressBarEdited)) SetAddressBarText(urls[i]);
            }
            foreach (var tab in ordered.Skip(urls.Count)) CloseTab(tab);
            foreach (string url in urls.Skip(ordered.Count))
            {
                await OpenTabAsync(url);
                SelectTab(current);
            }
        }

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
