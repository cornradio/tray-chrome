using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Markup;
using Microsoft.Web.WebView2.Core;

namespace TrayChrome
{
    public class HistoryItem
    {
        public string Title { get; set; } = string.Empty;
        public string Url { get; set; } = string.Empty;
    }

    public partial class MainWindow : Window
    {
        // Windows API 常量
        private const int WM_NCHITTEST = 0x84;
        private const int HTCLIENT = 1;
        private const int HTCAPTION = 2;
        private const int HTLEFT = 10;
        private const int HTRIGHT = 11;
        private const int HTTOP = 12;
        private const int HTTOPLEFT = 13;
        private const int HTTOPRIGHT = 14;
        private const int HTBOTTOM = 15;
        private const int HTBOTTOMLEFT = 16;
        private const int HTBOTTOMRIGHT = 17;

        // 用于给 WebView2 原生窗口设置圆角区域（解决 WPF 裁剪对 HwndHost 无效的问题）
        [DllImport("gdi32.dll")]
        private static extern IntPtr CreateRoundRectRgn(int nLeftRect, int nTopRect, int nRightRect, int nBottomRect, int nWidthEllipse, int nHeightEllipse);

        [DllImport("user32.dll")]
        private static extern int SetWindowRgn(IntPtr hWnd, IntPtr hRgn, bool bRedraw);

        private List<Bookmark> bookmarks = new List<Bookmark>();
        private string bookmarksFilePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "bookmarks.json");
        private string settingsFilePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "settings.json");
        private bool isBookmarkPanelVisible = false;
        private bool isMobileUA = true;
        private const string MobileUA = "Mozilla/5.0 (iPhone; CPU iPhone OS 17_0 like Mac OS X) AppleWebKit/605.1.15 (KHTML, like Gecko) Version/17.0 Mobile/15E148 Safari/604.1";
        private const string DesktopUA = "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0.0.0 Safari/537.36";
        private double currentZoomFactor = 1.0;
        private AppSettings appSettings = new AppSettings();
        private bool isResizing = false;
        private Point resizeStartPoint;
        private bool isDarkMode = false;
        private bool isTopMost = true; // 默认置顶
        private bool isSuperMinimalMode = false; // 超级极简模式状态
        private bool isAnimationEnabled = true; // 动画启用状态
        private bool hasSavedPosition = false; // 是否存在保存的位置
        private double lastVisibleLeft = double.NaN; // 上次隐藏前的可见位置
        private double lastVisibleTop = double.NaN;
        private AdBlocker adBlocker = new AdBlocker(); // 广告拦截器
        
        // 历史记录追踪
        private List<HistoryItem> historyList = new List<HistoryItem>();
        private int historyIndex = -1;
        private bool isNavigatingFromHistory = false;
        
        // 用于更新托盘图标提示的事件
        public event Action<string> TitleChanged;

        public MainWindow(string? startupUrl = null, bool useCleanMode = false, bool forceUncleanMode = false, double? customWidth = null, double? customHeight = null)
        {
            InitializeComponent();
            LoadSettings();
            
            // 应用自定义窗口大小（如果提供）
            if (customWidth.HasValue && customHeight.HasValue)
            {
                this.Width = customWidth.Value;
                this.Height = customHeight.Value;
                
                // 同时更新设置中的窗口大小，以便保存
                appSettings.WindowWidth = customWidth.Value;
                appSettings.WindowHeight = customHeight.Value;
            }
            
            // 处理超级极简模式设置的优先级：
            // 1. 如果指定了 --unclean，强制禁用超级极简模式
            // 2. 如果指定了 --clean，启用超级极简模式
            // 3. 否则使用保存的设置
            if (forceUncleanMode)
            {
                isSuperMinimalMode = false;
            }
            else if (useCleanMode)
            {
                isSuperMinimalMode = true;
            }
            
            InitializeWebView(startupUrl);
            LoadBookmarks();

            // 设置初始置顶状态
            this.Topmost = isTopMost;
            UpdateTopMostButtonAppearance();
            
            // 应用超级极简模式设置
            if (isSuperMinimalMode)
            {
                ToggleSuperMinimalMode(true);
            }
            
            // 添加汉堡菜单拖拽功能
            HamburgerMenu.MouseLeftButtonDown += HamburgerMenu_MouseLeftButtonDown;
            
            // 添加汉堡菜单右键调整窗口大小功能
            HamburgerMenu.MouseRightButtonDown += HamburgerMenu_MouseRightButtonDown;
            HamburgerMenu.MouseRightButtonUp += HamburgerMenu_MouseRightButtonUp;
            HamburgerMenu.MouseMove += HamburgerMenu_MouseMove;
            
            // 添加窗口调整按钮的拖拽功能
            ResizeButton.MouseLeftButtonDown += ResizeButton_MouseLeftButtonDown;
            ResizeButton.MouseLeftButtonUp += ResizeButton_MouseLeftButtonUp;
            ResizeButton.MouseMove += ResizeButton_MouseMove;
            
            // 添加拖动按钮的拖拽功能
            DragButton.MouseLeftButtonDown += DragButton_MouseLeftButtonDown;
            
            // 窗口关闭时保存设置并清理资源
            this.Closing += (sender, e) => 
            {
                StopMemoryCleanupTimer();
                SaveSettings();
            };
            
            // 启用窗口边缘调整大小功能
            this.SourceInitialized += MainWindow_SourceInitialized;
            
            // 初始化托盘提示
            UpdateTrayTooltip();
            
            // 初始化暗色模式按钮外观
            UpdateDarkModeButtonAppearance();
            // 应用UI外观
            UpdateUIAppearance(isDarkMode);

            // 应用圆角设置（随窗口尺寸变化重新裁剪）
            MainContentGrid.SizeChanged += (s, e) => ApplyRoundedCorners();
            webView.SizeChanged += (s, e) => ApplyRoundedCorners();
            ApplyRoundedCorners();

            // 启动内存清理定时器
            StartMemoryCleanupTimer();
            
            // 应用UI外观已经在上面调用过了，包含了代理环外观的更新
        }

        private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (Keyboard.Modifiers.HasFlag(ModifierKeys.Control) && e.Key == Key.L)
            {
                AddressBar.Focus();
                AddressBar.SelectAll();
                e.Handled = true;
                return;
            }

            if ((Keyboard.Modifiers & (ModifierKeys.Control | ModifierKeys.Shift)) == (ModifierKeys.Control | ModifierKeys.Shift)
                && e.Key == Key.O)
            {
                try
                {
                    // 在书签按钮下方显示书签菜单
                    if (BookmarkContextMenu != null && BookmarkButton != null)
                    {
                        BookmarkContextMenu.PlacementTarget = BookmarkButton;
                        BookmarkContextMenu.Placement = System.Windows.Controls.Primitives.PlacementMode.Bottom;
                        BookmarkContextMenu.IsOpen = true;
                        e.Handled = true;
                        return;
                    }
                }
                catch { }
            }

            if ((Keyboard.Modifiers & (ModifierKeys.Control | ModifierKeys.Shift)) == (ModifierKeys.Control | ModifierKeys.Shift)
                && e.Key == Key.B)
            {
                ToggleSuperMinimalMode(!isSuperMinimalMode);
                e.Handled = true;
                return;
            }
        }

        private async void InitializeWebView(string? startupUrl = null)
        {
            await InitializeWebViewInternal(startupUrl);
        }

        private async Task InitializeWebViewInternal(string? startupUrl = null)
        {
            try
            {
                // 检查 WebView2 是否已经初始化
                if (webView.CoreWebView2 != null)
                {
                    // 如果已经初始化，直接使用现有实例
                    System.Diagnostics.Debug.WriteLine("WebView2 已经初始化，跳过环境创建");
                }
                else
                {
                    // 委托给专门的 Helper 类处理环境创建及 0x8007139F 错误的回退连接
                    await WebView2EnvironmentHelper.EnsureCoreWebView2WithFallbackAsync(webView, appSettings);
                }
                
                // 优化WebView2设置以减少内存占用
                var settings = webView.CoreWebView2.Settings;
                
                // 设置用户代理
                settings.UserAgent = isMobileUA ? MobileUA : DesktopUA;
                
                // 启用开发者工具
                settings.AreDevToolsEnabled = true;
                
                // 禁用不必要的功能以节省内存
                settings.IsSwipeNavigationEnabled = false;
                settings.AreBrowserAcceleratorKeysEnabled = false;
                settings.IsGeneralAutofillEnabled = false;
                settings.IsPasswordAutosaveEnabled = false;
                
                // 应用缩放设置
                webView.ZoomFactor = currentZoomFactor;
                
                // 初始化时设置浏览器外观模式
                ApplyBrowserAppearance(isDarkMode);
                // 应用UI外观
                UpdateUIAppearance(isDarkMode);
                
                // 监听导航事件
                webView.CoreWebView2.NavigationStarting += CoreWebView2_NavigationStarting;
                webView.CoreWebView2.NavigationCompleted += CoreWebView2_NavigationCompleted;
                webView.CoreWebView2.SourceChanged += CoreWebView2_SourceChanged;
                
                // 监听文档标题变化事件
                webView.CoreWebView2.DocumentTitleChanged += CoreWebView2_DocumentTitleChanged;
                
                // 拦截新窗口打开请求，在当前窗口中打开
                webView.CoreWebView2.NewWindowRequested += CoreWebView2_NewWindowRequested;
                
                // 初始化广告拦截器（在 CoreWebView2 准备好后）
                InitializeAdBlocker();
                
                // 监听右键菜单请求
                webView.CoreWebView2.ContextMenuRequested += CoreWebView2_ContextMenuRequested;
                
                // 导航到启动URL或默认URL
                string urlToNavigate = !string.IsNullOrEmpty(startupUrl) 
                    ? startupUrl 
                    : "https://tva.cornradio.org/?name=search";
                
                webView.CoreWebView2.Navigate(urlToNavigate);
                AddressBar.Text = urlToNavigate;
            }
            catch (Exception ex)
            {
                MessageBox.Show($"WebView2初始化失败: {ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void CoreWebView2_SourceChanged(object? sender, CoreWebView2SourceChangedEventArgs e)
        {
            Dispatcher.Invoke(() =>
            {
                AddressBar.Text = webView.CoreWebView2.Source;
            });
        }

        private void CoreWebView2_NavigationStarting(object? sender, CoreWebView2NavigationStartingEventArgs e)
        {
            // 在页面导航开始前清理前一个页面的资源
            _ = Task.Run(async () =>
            {
                try
                {
                    // 立即清理前一个页面的资源
                    await Dispatcher.InvokeAsync(() =>
                    {
                        CleanupWebViewMemory();
                    });
                    
                    // 短暂延迟确保清理完成
                    await Task.Delay(500);
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"导航前清理异常: {ex.Message}");
                }
            });
        }

        private void CoreWebView2_NavigationCompleted(object? sender, CoreWebView2NavigationCompletedEventArgs e)
        {
            Dispatcher.Invoke(() =>
            {
                BackButton.IsEnabled = webView.CoreWebView2.CanGoBack;
                ForwardButton.IsEnabled = webView.CoreWebView2.CanGoForward;
                
                if (e.IsSuccess)
                {
                    UpdateHistory(webView.Source.ToString(), webView.CoreWebView2.DocumentTitle);
                }

                // 确保每个页面都使用相同的缩放比例
                webView.ZoomFactor = currentZoomFactor;
                
                // 外观模式已在初始化时设置
                
                // 更新托盘图标提示
                UpdateTrayTooltip();
                
                // 页面导航完成后清理内存
                _ = Task.Run(async () =>
                {
                    try
                    {
                        // 等待页面完全加载
                        await Task.Delay(2000);
                        
                        // 在UI线程中执行内存清理
                        await Dispatcher.InvokeAsync(() =>
                        {
                            CleanupWebViewMemory();
                        });
                    }
                    catch (Exception ex)
                    {
                        // 忽略清理过程中的异常
                        System.Diagnostics.Debug.WriteLine($"内存清理异常: {ex.Message}");
                    }
                });
            });
        }
        
        
        private void CoreWebView2_DocumentTitleChanged(object? sender, object e)
        {
            Dispatcher.Invoke(() =>
            {
                UpdateTrayTooltip();
            });
        }
        
        private void UpdateTrayTooltip()
        {
            try
            {
                string title = webView.CoreWebView2?.DocumentTitle ?? "未知页面";
                string url = webView.CoreWebView2?.Source ?? "";
                
                // 如果标题为空或只是URL，使用URL作为标题
                if (string.IsNullOrWhiteSpace(title) || title == url)
                {
                    if (!string.IsNullOrEmpty(url))
                    {
                        Uri uri = new Uri(url);
                        title = uri.Host;
                    }
                    else
                    {
                        title = "Tray Chrome";
                    }
                }
                
                // 触发标题变化事件，通知App更新托盘图标提示
                TitleChanged?.Invoke(title);
            }
            catch (Exception ex)
            {
                // 如果出现异常，使用默认标题
                TitleChanged?.Invoke("Tray Chrome");
            }
        }
        
        private void ApplyBrowserAppearance(bool darkMode)
        {
            try
            {
                if (webView.CoreWebView2 == null) return;
                
                // 设置浏览器的外观模式
                webView.CoreWebView2.Profile.PreferredColorScheme = darkMode 
                    ? Microsoft.Web.WebView2.Core.CoreWebView2PreferredColorScheme.Dark 
                    : Microsoft.Web.WebView2.Core.CoreWebView2PreferredColorScheme.Light;
            }
            catch (Exception ex)
            {
                MessageBox.Show($"设置浏览器外观失败: {ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }
        
        private void CoreWebView2_ContextMenuRequested(object? sender, CoreWebView2ContextMenuRequestedEventArgs e)
        {
            try
            {
                // 创建一个子菜单
                var subMenu = webView.CoreWebView2.Environment.CreateContextMenuItem(
                    "Tray Chrome 选项", 
                    null, 
                    CoreWebView2ContextMenuItemKind.Submenu);
                
                // 1. 切换代理
                var proxyToggleItem = webView.CoreWebView2.Environment.CreateContextMenuItem(
                    appSettings.IsProxyEnabled ? "禁用代理 (ON)" : "启用代理 (OFF)",
                    null,
                    CoreWebView2ContextMenuItemKind.Command);
                proxyToggleItem.CustomItemSelected += (s, args) => ToggleProxy();
                subMenu.Children.Add(proxyToggleItem);
                
                // 2. 代理设置
                var proxySettingsItem = webView.CoreWebView2.Environment.CreateContextMenuItem(
                    "代理地址设置...",
                    null,
                    CoreWebView2ContextMenuItemKind.Command);
                proxySettingsItem.CustomItemSelected += (s, args) => ShowProxySettingsDialog();
                subMenu.Children.Add(proxySettingsItem);
                
                subMenu.Children.Add(webView.CoreWebView2.Environment.CreateContextMenuItem("", null, CoreWebView2ContextMenuItemKind.Separator));
                
                // 3. 极简模式切换
                var minimalModeItem = webView.CoreWebView2.Environment.CreateContextMenuItem(
                    isSuperMinimalMode ? "退出极简模式" : "进入极简模式",
                    null,
                    CoreWebView2ContextMenuItemKind.Command);
                minimalModeItem.CustomItemSelected += (s, args) => ToggleSuperMinimalMode(!isSuperMinimalMode);
                subMenu.Children.Add(minimalModeItem);
                
                // 将 Tray Chrome 选项添加到右键菜单顶部
                e.MenuItems.Insert(0, subMenu);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"创建右键菜单失败: {ex.Message}");
            }
        }
        
        private async void ToggleProxy()
        {
            appSettings.IsProxyEnabled = !appSettings.IsProxyEnabled;
            SaveSettings();
            
            await UpdateProxyConfig();
            

            if (ProxyToggleMenuItem2 != null)
            {
                ProxyToggleMenuItem2.IsChecked = appSettings.IsProxyEnabled;
            }
            
            UpdateUIAppearance(isDarkMode);
            
            string status = appSettings.IsProxyEnabled ? $"已启用代理: {appSettings.ProxyServer}" : "已禁用代理";
            System.Diagnostics.Debug.WriteLine(status);
        }
        

        
        private async Task UpdateProxyConfig()
        {
            if (webView != null && webView.CoreWebView2 != null)
            {
                try
                {
                    // 尝试通过反射调用 SetHttpProxyConfigAsync
                    var profile = webView.CoreWebView2.Profile;
                    var method = profile.GetType().GetMethod("SetHttpProxyConfigAsync", new[] { typeof(string) });
                    
                    if (method != null)
                    {
                        string proxyConfig = (appSettings.IsProxyEnabled && !string.IsNullOrEmpty(appSettings.ProxyServer)) 
                            ? appSettings.ProxyServer 
                            : "";
                        
                        var task = (Task?)method.Invoke(profile, new object[] { proxyConfig });
                        if (task != null) await task;
                        
                        System.Diagnostics.Debug.WriteLine($"动态代理配置已应用: {proxyConfig}");
                    }
                    else
                    {
                        // 如果不支持反射调用，且设置发生了变化，建议重置环境
                        System.Diagnostics.Debug.WriteLine("当前 SDK 不支持 SetHttpProxyConfigAsync，如需应用代理更改请重置环境或重启。");
                    }
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"动态切换代理失败: {ex.Message}");
                }
            }
        }
        
        private void ShowProxySettingsDialog()
        {
            // 改为打开统一的设置窗口，并定位到浏览器设置页
            var settingsWindow = new SettingsWindow(appSettings, this, Application.Current as App);
            if (settingsWindow.MainTabControl != null)
            {
                settingsWindow.MainTabControl.SelectedIndex = 2;
            }
            settingsWindow.ShowDialog();
        }

        private void CoreWebView2_NewWindowRequested(object? sender, Microsoft.Web.WebView2.Core.CoreWebView2NewWindowRequestedEventArgs e)
        {
            // 检查是否按住了Ctrl键
            bool isCtrlPressed = (Keyboard.Modifiers & ModifierKeys.Control) == ModifierKeys.Control;
            
            if (isCtrlPressed)
            {
                // 按住Ctrl键时，在新的WebView2窗口中打开链接
                e.Handled = false; // 允许WebView2创建新窗口
            }
            else
            {
                // 默认情况下，在当前窗口中打开链接
                e.Handled = true;
                if (!string.IsNullOrEmpty(e.Uri))
                {
                    webView.CoreWebView2?.Navigate(e.Uri);
                }
            }
        }
         
         private void UpdateDarkModeButtonAppearance()
         {
             if (DarkModeButton != null)
             {
                 DarkModeButton.Content = isDarkMode ? "⏾" : "☼";
                 DarkModeButton.ToolTip = isDarkMode ? "切换到亮色模式" : "切换到暗色模式";
             }
         }
         
         private void UpdateUIAppearance(bool darkMode)
         {
             try
             {
                 var buttonForeground = darkMode 
                     ? new System.Windows.Media.SolidColorBrush(System.Windows.Media.Colors.White)
                     : new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0x66, 0x66, 0x66)); // 灰色
                 
                 if (darkMode)
                 {
                     // 暗色模式
                     if (TopToolbar != null)
                     {
                         TopToolbar.Background = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0x3C, 0x3C, 0x3C));
                         TopToolbar.BorderBrush = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0x55, 0x55, 0x55));
                     }
                     
                     if (BottomToolbar != null)
                     {
                         BottomToolbar.Background = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0x3C, 0x3C, 0x3C));
                         BottomToolbar.BorderBrush = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0x55, 0x55, 0x55));
                     }
                     
                     if (AddressBar != null)
                     {
                         AddressBar.Background = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0x44, 0x44, 0x44));
                         AddressBar.Foreground = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Colors.White);
                     }
                     
                     if (HamburgerMenu != null)
                     {
                         HamburgerMenu.Foreground = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Colors.White);
                     }
                     
                     if (DragButton != null)
                     {
                         DragButton.Foreground = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Colors.White);
                     }
                     
                     if (ResizeButton != null)
                     {
                         ResizeButton.Foreground = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Colors.White);
                     }
                     
                     if (ProxyRing != null)
                     {
                         ProxyRing.Stroke = appSettings.IsProxyEnabled 
                             ? new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0x87, 0xCE, 0xFA)) 
                             : new System.Windows.Media.SolidColorBrush(System.Windows.Media.Colors.White);
                     }
                 }
                 else
                 {
                     // 亮色模式 - 雅灰白色
                     if (TopToolbar != null)
                     {
                         TopToolbar.Background = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0xF5, 0xF5, 0xF5));
                         TopToolbar.BorderBrush = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0xD0, 0xD0, 0xD0));
                     }
                     
                     if (BottomToolbar != null)
                     {
                         BottomToolbar.Background = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0xF5, 0xF5, 0xF5));
                         BottomToolbar.BorderBrush = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0xD0, 0xD0, 0xD0));
                     }
                     
                     if (AddressBar != null)
                     {
                         AddressBar.Background = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Colors.White);
                         AddressBar.Foreground = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0x33, 0x33, 0x33));
                     }
                     
                     if (HamburgerMenu != null)
                     {
                         HamburgerMenu.Foreground = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0x33, 0x33, 0x33));
                     }
                     
                     if (DragButton != null)
                     {
                         DragButton.Foreground = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0x33, 0x33, 0x33));
                     }
                     
                     if (ResizeButton != null)
                     {
                         ResizeButton.Foreground = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0x33, 0x33, 0x33));
                     }
                     
                     if (ProxyRing != null)
                     {
                         ProxyRing.Stroke = appSettings.IsProxyEnabled 
                             ? new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0x87, 0xCE, 0xFA)) 
                             : new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0x33, 0x33, 0x33));
                     }
                 }
                 
                 // 更新所有工具栏按钮的颜色
                 UpdateButtonColors(darkMode, buttonForeground);
             }
             catch (Exception ex)
             {
                 System.Diagnostics.Debug.WriteLine($"更新UI外观失败: {ex.Message}");
             }
         }
         
         private void UpdateButtonColors(bool darkMode, System.Windows.Media.Brush foreground)
         {
             try
             {
                 // 定义所有工具栏按钮
                 var buttons = new[]
                 {
                     CloseButton, BackButton, ForwardButton, RefreshButton, BookmarkButton,
                     DarkModeButton, PopupButton, UAButton, TopMostButton, ZoomOutButton, ZoomInButton, ProxyButton
                 };
                 
                 // 创建新的样式，根据暗色/亮色模式设置不同的悬停和按下颜色
                 var hoverBackground = darkMode 
                     ? new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0x55, 0x55, 0x55))
                     : new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0xE0, 0xE0, 0xE0));
                 
                 var pressedBackground = darkMode
                     ? new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0x66, 0x66, 0x66))
                     : new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0xD0, 0xD0, 0xD0));
                 
                 var pressedForeground = darkMode
                     ? new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0xDD, 0xDD, 0xDD))
                     : new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0x33, 0x33, 0x33));
                 
                 var disabledForeground = darkMode
                     ? new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0x88, 0x88, 0x88))
                     : new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0xAA, 0xAA, 0xAA));
                 
                 foreach (var button in buttons)
                 {
                     if (button != null)
                     {
                         button.Foreground = foreground;
                         
                         // 创建新的样式
                         var newStyle = new Style(typeof(Button));
                         newStyle.Setters.Add(new Setter(Button.BackgroundProperty, System.Windows.Media.Brushes.Transparent));
                         newStyle.Setters.Add(new Setter(Button.ForegroundProperty, foreground));
                         newStyle.Setters.Add(new Setter(Button.BorderThicknessProperty, new Thickness(0)));
                         newStyle.Setters.Add(new Setter(Button.FontSizeProperty, 16.0));
                         newStyle.Setters.Add(new Setter(Button.CursorProperty, System.Windows.Input.Cursors.Hand));
                         
                         // 创建模板
                         var template = new ControlTemplate(typeof(Button));
                         var border = new FrameworkElementFactory(typeof(Border));
                         border.SetValue(Border.BackgroundProperty, new TemplateBindingExtension(Button.BackgroundProperty));
                         border.SetValue(Border.BorderBrushProperty, new TemplateBindingExtension(Button.BorderBrushProperty));
                         border.SetValue(Border.BorderThicknessProperty, new TemplateBindingExtension(Button.BorderThicknessProperty));
                         border.SetValue(Border.CornerRadiusProperty, new CornerRadius(3));
                         
                         var contentPresenter = new FrameworkElementFactory(typeof(ContentPresenter));
                         contentPresenter.SetValue(ContentPresenter.HorizontalAlignmentProperty, HorizontalAlignment.Center);
                         contentPresenter.SetValue(ContentPresenter.VerticalAlignmentProperty, VerticalAlignment.Center);
                         border.AppendChild(contentPresenter);
                         template.VisualTree = border;
                         
                         // 添加触发器
                         var hoverTrigger = new Trigger { Property = Button.IsMouseOverProperty, Value = true };
                         hoverTrigger.Setters.Add(new Setter(Button.BackgroundProperty, hoverBackground));
                         newStyle.Triggers.Add(hoverTrigger);
                         
                         var pressedTrigger = new Trigger { Property = Button.IsPressedProperty, Value = true };
                         pressedTrigger.Setters.Add(new Setter(Button.BackgroundProperty, pressedBackground));
                         pressedTrigger.Setters.Add(new Setter(Button.ForegroundProperty, pressedForeground));
                         newStyle.Triggers.Add(pressedTrigger);
                         
                         var disabledTrigger = new Trigger { Property = Button.IsEnabledProperty, Value = false };
                         disabledTrigger.Setters.Add(new Setter(Button.ForegroundProperty, disabledForeground));
                         newStyle.Triggers.Add(disabledTrigger);
                         
                         newStyle.Setters.Add(new Setter(Button.TemplateProperty, template));
                         
                         button.Style = newStyle;
                     }
                 }
             }
             catch (Exception ex)
             {
                 System.Diagnostics.Debug.WriteLine($"更新按钮颜色失败: {ex.Message}");
             }
         }
         
         private void UpdateTopMostButtonAppearance()
         {
             if (TopMostButton != null)
             {
                 TopMostButton.Content = isTopMost ? "📌" : "⚲";
                 TopMostButton.ToolTip = isTopMost ? "取消置顶" : "窗口置顶";
             }
         }

        private void ApplyRoundedCorners()
        {
            if (MainBorder == null || MainContentGrid == null) return;

            double radius = appSettings.EnableRoundedCorners ? appSettings.CornerRadiusPx : 0;
            if (radius < 0) radius = 0;

            MainBorder.CornerRadius = new CornerRadius(radius);
            MainBorder.BorderThickness = new Thickness(0);
            MainBorder.BorderBrush = null;

            // 裁剪内容到圆角，保证 WPF 元素（工具栏等）四周呈现圆角
            MainContentGrid.Clip = (radius > 0 && MainContentGrid.ActualWidth > 0 && MainContentGrid.ActualHeight > 0)
                ? new RectangleGeometry(new Rect(0, 0, MainContentGrid.ActualWidth, MainContentGrid.ActualHeight), radius, radius)
                : null;

            // WebView2 是原生窗口，无法被 WPF 裁剪，需单独设置原生圆角区域
            ApplyWebViewRoundedCorners();
        }

        private void ApplyWebViewRoundedCorners()
        {
            if (webView == null) return;

            IntPtr hWnd;
            try { hWnd = webView.Handle; }
            catch { return; }

            if (hWnd == IntPtr.Zero) return;

            // 仅在极简模式（无工具栏、WebView 撑满窗口）下需要对 WebView 做圆角
            double radius = appSettings.EnableRoundedCorners ? appSettings.CornerRadiusPx : 0;
            bool applyToWebView = isSuperMinimalMode && radius > 0;

            if (!applyToWebView)
            {
                SetWindowRgn(hWnd, IntPtr.Zero, true);
                return;
            }

            double dpiScaleX = 1.0;
            double dpiScaleY = 1.0;
            try
            {
                var dpi = VisualTreeHelper.GetDpi(webView);
                dpiScaleX = dpi.DpiScaleX;
                dpiScaleY = dpi.DpiScaleY;
            }
            catch { }

            int width = (int)Math.Ceiling(webView.ActualWidth * dpiScaleX);
            int height = (int)Math.Ceiling(webView.ActualHeight * dpiScaleY);
            int r = (int)Math.Ceiling(radius * dpiScaleX);

            if (width <= 0 || height <= 0 || r <= 0) return;

            IntPtr region = CreateRoundRectRgn(0, 0, width + 1, height + 1, r * 2, r * 2);
            if (region == IntPtr.Zero) return;

            // SetWindowRgn 成功后系统拥有该 region，不能再 DeleteObject
            SetWindowRgn(hWnd, region, true);
        }

        private void BackButton_Click(object sender, RoutedEventArgs e)
        {
            if (webView.CoreWebView2?.CanGoBack == true)
            {
                isNavigatingFromHistory = true;
                webView.CoreWebView2.GoBack();
            }
        }

        private void ForwardButton_Click(object sender, RoutedEventArgs e)
        {
            if (webView.CoreWebView2?.CanGoForward == true)
            {
                isNavigatingFromHistory = true;
                webView.CoreWebView2.GoForward();
            }
        }

        private void UpdateHistory(string url, string title)
        {
            if (string.IsNullOrEmpty(url) || url == "about:blank") return;

            // 如果是历史导航，尝试定位索引
            if (isNavigatingFromHistory)
            {
                isNavigatingFromHistory = false;
                
                // 检查是否是回到前一个或后一个
                if (historyIndex > 0 && historyList[historyIndex - 1].Url == url)
                {
                    historyIndex--;
                    if (!string.IsNullOrEmpty(title)) historyList[historyIndex].Title = title;
                    return;
                }
                if (historyIndex < historyList.Count - 1 && historyList[historyIndex + 1].Url == url)
                {
                    historyIndex++;
                    if (!string.IsNullOrEmpty(title)) historyList[historyIndex].Title = title;
                    return;
                }
            }

            // 检查是否重复（比如点击了当前页面的链接或刷新）
            if (historyIndex >= 0 && historyList[historyIndex].Url == url)
            {
                if (!string.IsNullOrEmpty(title)) historyList[historyIndex].Title = title;
                return;
            }

            // 新导航：清除前进历史
            if (historyIndex < historyList.Count - 1)
            {
                historyList.RemoveRange(historyIndex + 1, historyList.Count - (historyIndex + 1));
            }

            historyList.Add(new HistoryItem { Url = url, Title = string.IsNullOrEmpty(title) ? url : title });
            historyIndex++;

            // 限制长度
            if (historyList.Count > 100)
            {
                historyList.RemoveAt(0);
                historyIndex--;
            }
        }

        private void BackButton_RightClick(object sender, MouseButtonEventArgs e)
        {
            ShowHistoryMenu(BackButton, true);
        }

        private void ForwardButton_RightClick(object sender, MouseButtonEventArgs e)
        {
            ShowHistoryMenu(ForwardButton, false);
        }

        private void ShowHistoryMenu(FrameworkElement anchor, bool isBack)
        {
            try
            {
                if (historyList.Count == 0) return;

                ContextMenu menu = new ContextMenu();
                
                if (isBack)
                {
                    // 显示当前索引之前的所有项
                    for (int i = historyIndex - 1; i >= 0; i--)
                    {
                        AddHistoryMenuItem(menu, historyList[i], i);
                    }
                }
                else
                {
                    // 显示当前索引之后的所有项
                    for (int i = historyIndex + 1; i < historyList.Count; i++)
                    {
                        AddHistoryMenuItem(menu, historyList[i], i);
                    }
                }

                if (menu.Items.Count == 0) return;

                menu.PlacementTarget = anchor;
                menu.Placement = System.Windows.Controls.Primitives.PlacementMode.Bottom;
                menu.IsOpen = true;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"显示历史菜单失败: {ex.Message}");
            }
        }

        private void AddHistoryMenuItem(ContextMenu menu, HistoryItem item, int index)
        {
            MenuItem menuItem = new MenuItem
            {
                Header = item.Title,
                ToolTip = item.Url,
                MaxWidth = 300
            };
            
            menuItem.Click += (s, e) => {
                try
                {
                    // 计算需要跳转的步数
                    int steps = index - historyIndex;
                    if (steps == 0) return;

                    isNavigatingFromHistory = true;
                    if (steps < 0)
                    {
                        for (int i = 0; i < Math.Abs(steps); i++)
                        {
                            if (webView.CoreWebView2.CanGoBack) webView.CoreWebView2.GoBack();
                        }
                    }
                    else
                    {
                        for (int i = 0; i < steps; i++)
                        {
                            if (webView.CoreWebView2.CanGoForward) webView.CoreWebView2.GoForward();
                        }
                    }
                    
                    historyIndex = index;
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"跳转历史失败: {ex.Message}");
                }
            };
            
            menu.Items.Add(menuItem);
        }

        private void RefreshButton_Click(object sender, RoutedEventArgs e)
        {
            webView.CoreWebView2?.Reload();
        }

        private void CloseButton_Click(object sender, RoutedEventArgs e)
        {
            HideWithAnimation();
        }

        private void PopupButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                string currentUrl = webView.CoreWebView2?.Source ?? AddressBar.Text;
                if (!string.IsNullOrEmpty(currentUrl))
                {
                    // 在默认浏览器中打开当前页面（相当于_blank）
                    Process.Start(new ProcessStartInfo
                    {
                        FileName = currentUrl,
                        UseShellExecute = true
                    });
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"打开页面失败: {ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void BookmarkButton_Click(object sender, RoutedEventArgs e)
        {
            // 显示右键菜单
            BookmarkContextMenu.IsOpen = true;
        }

        private void UAButton_Click(object sender, RoutedEventArgs e)
        {
            ToggleUserAgent();
        }
        
        private void DarkModeButton_Click(object sender, RoutedEventArgs e)
        {
            isDarkMode = !isDarkMode;
            ApplyBrowserAppearance(isDarkMode);
            UpdateDarkModeButtonAppearance();
            UpdateUIAppearance(isDarkMode);
            SaveSettings();
            
            // 刷新当前页面以立即应用外观模式
            if (webView.CoreWebView2 != null)
            {
                webView.CoreWebView2.Reload();
            }
        }
        
        private void TopMostButton_Click(object sender, RoutedEventArgs e)
        {
            isTopMost = !isTopMost;
            this.Topmost = isTopMost;
            UpdateTopMostButtonAppearance();
            SaveSettings();
        }

        private void AddressBar_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter)
            {
                NavigateToUrl(AddressBar.Text);
            }
        }

        private void NavigateToUrl(string url)
        {
            try
            {
                if (!string.IsNullOrWhiteSpace(url))
                {
                    // 如果不是完整URL，添加https://
                    if (!url.StartsWith("http://") && !url.StartsWith("https://"))
                    {
                        url = "https://" + url;
                    }
                    
                    webView.CoreWebView2?.Navigate(url);
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"导航失败: {ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        protected override void OnSourceInitialized(EventArgs e)
        {
            base.OnSourceInitialized(e);
            
            // 如果没有保存的位置，才基于当前屏幕工作区定位
            if (!hasSavedPosition)
            {
                var workingArea = GetCurrentScreenWorkingAreaInWpfUnits();
                Left = workingArea.Right - Width - 20;
                Top = workingArea.Bottom - Height - 20;
            }
        }

        // 防止窗口在任务栏显示
        protected override void OnStateChanged(EventArgs e)
        {
            if (WindowState == WindowState.Minimized)
            {
                Hide();
            }
            base.OnStateChanged(e);
        }

        // 自定义流畅的缓动函数，专为高刷新率屏幕优化
        private class SmoothEase : EasingFunctionBase
        {
            protected override double EaseInCore(double normalizedTime)
            {
                // 使用改进的贝塞尔曲线，提供更自然的动画效果
                return normalizedTime * normalizedTime * (3.0 - 2.0 * normalizedTime);
            }

            protected override Freezable CreateInstanceCore()
            {
                return new SmoothEase();
            }
        }

        public void ShowWithAnimation()
        {
            var workingArea = GetCurrentScreenWorkingAreaInWpfUnits();

            // 目标位置：优先恢复上次隐藏前的位置，其次使用当前 Left/Top，最后默认右下角
            double targetLeft = lastVisibleLeft;
            double targetTop = lastVisibleTop;

            if (double.IsNaN(targetLeft) || double.IsInfinity(targetLeft))
                targetLeft = Left;
            if (double.IsNaN(targetTop) || double.IsInfinity(targetTop))
                targetTop = Top;

            if (double.IsNaN(targetLeft) || double.IsInfinity(targetLeft))
                targetLeft = workingArea.Right - Width - 20;
            if (double.IsNaN(targetTop) || double.IsInfinity(targetTop) || targetTop >= workingArea.Bottom)
                targetTop = workingArea.Bottom - Height - 20;

            // 钳制到当前屏幕工作区，允许任意位置但保证不出屏幕
            double minLeft = workingArea.Left;
            double maxLeft = workingArea.Right - Width;
            if (maxLeft < minLeft) maxLeft = minLeft; // 防御：窗口宽度大于工作区
            targetLeft = Math.Max(minLeft, Math.Min(targetLeft, maxLeft));

            double minTop = workingArea.Top;
            double maxTop = workingArea.Bottom - Height;
            if (maxTop < minTop) maxTop = minTop; // 防御：窗口高度大于工作区
            targetTop = Math.Max(minTop, Math.Min(targetTop, maxTop));

            // 先清除可能残留的动画，避免旧动画值覆盖位置
            BeginAnimation(TopProperty, null);
            BeginAnimation(LeftProperty, null);

            Left = targetLeft;

            // 检查是否应该禁用动画
            if (SystemAnimationHelper.ShouldDisableAnimation(isAnimationEnabled))
            {
                Top = targetTop;
                Show();
                Activate();
                return;
            }

            // 从屏幕下方滑入到目标位置
            double startTop = workingArea.Bottom + 50;
            Top = startTop;
            Show();
            Activate();

            var animation = new DoubleAnimation
            {
                From = startTop,
                To = targetTop,
                Duration = TimeSpan.FromMilliseconds(120),
                EasingFunction = new SmoothEase { EasingMode = EasingMode.EaseOut }
            };
            animation.Completed += (s, e) =>
            {
                BeginAnimation(TopProperty, null);
                Top = targetTop;
            };
            BeginAnimation(TopProperty, animation);
        }

        public void HideWithAnimation()
        {
            // 先清除可能残留的动画，确保读取到正确的当前位置
            BeginAnimation(TopProperty, null);
            BeginAnimation(LeftProperty, null);

            // 记住当前可见位置，供下次显示时恢复
            RememberVisiblePosition();

            // 检查是否应该禁用动画
            if (SystemAnimationHelper.ShouldDisableAnimation(isAnimationEnabled))
            {
                Hide();
                return;
            }

            var workingArea = GetCurrentScreenWorkingAreaInWpfUnits();
            double currentTop = Top;

            var animation = new DoubleAnimation
            {
                From = currentTop,
                To = workingArea.Bottom + 50,
                Duration = TimeSpan.FromMilliseconds(120),
                EasingFunction = new SmoothEase { EasingMode = EasingMode.EaseIn }
            };
            animation.Completed += (s, e) =>
            {
                BeginAnimation(TopProperty, null);
                Hide();
            };
            BeginAnimation(TopProperty, animation);
        }

        private void RememberVisiblePosition()
        {
            lastVisibleLeft = Left;
            lastVisibleTop = Top;
        }

        public void ResetWindowPosition()
        {
            // 清除已记录的位置，恢复到默认右下角
            lastVisibleLeft = double.NaN;
            lastVisibleTop = double.NaN;
            appSettings.WindowLeft = null;
            appSettings.WindowTop = null;
            hasSavedPosition = false;

            // 停止任何进行中的动画，避免位置/尺寸被动画覆盖
            BeginAnimation(TopProperty, null);
            BeginAnimation(LeftProperty, null);
            BeginAnimation(WidthProperty, null);
            BeginAnimation(HeightProperty, null);

            var workingArea = GetCurrentScreenWorkingAreaInWpfUnits();
            double targetLeft = workingArea.Right - Width - 20;
            double targetTop = workingArea.Bottom - Height - 20;

            Left = targetLeft;
            Top = targetTop;

            // 显示窗口，确保用户能找到
            Show();
            WindowState = WindowState.Normal;
            Activate();

            SaveSettings();
        }



        public void LoadBookmarks()
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(bookmarksFilePath));
                
                if (File.Exists(bookmarksFilePath))
                {
                    var json = File.ReadAllText(bookmarksFilePath);
                    bookmarks = JsonSerializer.Deserialize<List<Bookmark>>(json) ?? new List<Bookmark>();
                }
                else
                {
                    // 添加一些默认收藏夹
                    bookmarks = new List<Bookmark>
                    {
                        new Bookmark { Title = "Google", Url = "https://www.google.com" },
                        new Bookmark { Title = "GitHub", Url = "https://github.com" },
                        new Bookmark { Title = "Stack Overflow", Url = "https://stackoverflow.com" }
                    };
                    SaveBookmarks();
                }
                
                // 加载收藏夹到菜单
                RefreshBookmarkMenu();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"加载收藏夹失败: {ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        private void SaveBookmarks()
        {
            try
            {
                var json = JsonSerializer.Serialize(bookmarks, new JsonSerializerOptions { WriteIndented = true });
                File.WriteAllText(bookmarksFilePath, json);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"保存收藏夹失败: {ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        public void RefreshBookmarkMenu()
        {
            // 查找分隔符位置
            int startIndex = -1;
            int endIndex = -1;
            
            for (int i = 0; i < BookmarkContextMenu.Items.Count; i++)
            {
                if (BookmarkContextMenu.Items[i] is FrameworkElement element)
                {
                    if (element.Name == "BookmarkSeparator") startIndex = i;
                    if (element.Name == "DynamicSeparatorEnd") endIndex = i;
                }
            }

            if (startIndex == -1 || endIndex == -1) return;

            // 清除两个分隔符之间的动态项
            while (endIndex > startIndex + 1)
            {
                BookmarkContextMenu.Items.RemoveAt(startIndex + 1);
                endIndex--;
            }
            
            // 插入书签
            int insertPos = startIndex + 1;
            foreach (var bookmark in bookmarks)
            {
                MenuItem bookmarkItem = new MenuItem
                {
                    Header = bookmark.Title,
                    Tag = bookmark.Url,
                    ToolTip = bookmark.Url
                };
                
                bookmarkItem.Click += (s, args) => {
                    if (bookmarkItem.Tag != null)
                    {
                        webView.CoreWebView2?.Navigate(bookmarkItem.Tag.ToString());
                    }
                };
                
                // 中键点击删除
                bookmarkItem.MouseUp += (s, args) => {
                    if (args.ChangedButton == System.Windows.Input.MouseButton.Middle)
                    {
                        var result = MessageBox.Show($"确定要删除收藏夹 \"{bookmark.Title}\" 吗？", 
                            "删除收藏夹", MessageBoxButton.YesNo, MessageBoxImage.Question);
                        
                        if (result == MessageBoxResult.Yes)
                        {
                            bookmarks.Remove(bookmark);
                            SaveBookmarks();
                            RefreshBookmarkMenu();
                        }
                        args.Handled = true;
                    }
                };
                
                // 添加右键编辑
                ContextMenu itemContextMenu = new ContextMenu();
                MenuItem editItem = new MenuItem { Header = "编辑" };
                editItem.Click += (s, args) => EditBookmark(bookmark);
                itemContextMenu.Items.Add(editItem);
                bookmarkItem.ContextMenu = itemContextMenu;
                
                BookmarkContextMenu.Items.Insert(insertPos++, bookmarkItem);
            }
            
            // 更新代理菜单项状态

        }

        private void ShowSettings_Bookmarks_Click(object sender, RoutedEventArgs e)
        {
            var settingsWindow = new SettingsWindow(appSettings, this, Application.Current as App);
            // 收藏夹是第6个标签，索引为 5
            if (settingsWindow.MainTabControl != null)
            {
                settingsWindow.MainTabControl.SelectedIndex = 5;
            }
            settingsWindow.ShowDialog();
        }

        private void ProxyButton_Click(object sender, RoutedEventArgs e)
        {
            if (ProxyButton.ContextMenu != null)
            {
                ProxyButton.ContextMenu.PlacementTarget = ProxyButton;
                ProxyButton.ContextMenu.Placement = System.Windows.Controls.Primitives.PlacementMode.Bottom;
                ProxyToggleMenuItem2.IsChecked = appSettings.IsProxyEnabled;
                ProxyButton.ContextMenu.IsOpen = true;
            }
        }

        private void RestartApp_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                string args = "--open";
                string currentUrl = webView?.CoreWebView2?.Source ?? "";
                if (!string.IsNullOrEmpty(currentUrl))
                {
                    args += $" --url \"{currentUrl}\"";
                }

                // 启动新的应用程序实例
                string currentExecutable = System.Diagnostics.Process.GetCurrentProcess().MainModule?.FileName ?? "";
                if (!string.IsNullOrEmpty(currentExecutable))
                {
                    System.Diagnostics.Process.Start(currentExecutable, args);
                }
                
                // 关闭当前实例
                Application.Current.Shutdown();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"重启失败: {ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
        
        private void ProxyToggle_Click(object sender, RoutedEventArgs e)
        {
            ToggleProxy();

        }
        
        private void ProxySettings_Click(object sender, RoutedEventArgs e)
        {
            ShowProxySettingsDialog();
        }

        private void LoadSettings()
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(settingsFilePath));
                
                if (File.Exists(settingsFilePath))
                {
                    var json = File.ReadAllText(settingsFilePath);
                    appSettings = JsonSerializer.Deserialize<AppSettings>(json) ?? new AppSettings();
                }
                
                // 应用设置
                currentZoomFactor = appSettings.ZoomFactor;
                isMobileUA = appSettings.IsMobileUA;
                this.Width = appSettings.WindowWidth;
                this.Height = appSettings.WindowHeight;
                isDarkMode = appSettings.IsDarkMode;
                isTopMost = appSettings.IsTopMost;
                isSuperMinimalMode = appSettings.IsSuperMinimalMode;
                isAnimationEnabled = appSettings.IsAnimationEnabled;
                
                // 应用UI外观（需要在WebView初始化后调用，所以延迟到InitializeWebView之后）
                
                // 广告拦截设置
                if (appSettings.AdBlockRules != null && appSettings.AdBlockRules.Count > 0)
                {
                    adBlocker.BlockRules = appSettings.AdBlockRules;
                }
                else
                {
                    adBlocker.LoadDefaultRules();
                }
                if (appSettings.AdAllowRules != null)
                {
                    adBlocker.AllowRules = appSettings.AdAllowRules;
                }
                adBlocker.IsEnabled = appSettings.IsAdBlockEnabled;
                
                // 位置（如果有保存）
                if (appSettings.WindowLeft.HasValue && appSettings.WindowTop.HasValue)
                {
                    this.Left = appSettings.WindowLeft.Value;
                    this.Top = appSettings.WindowTop.Value;
                    hasSavedPosition = true;
                    lastVisibleLeft = appSettings.WindowLeft.Value;
                    lastVisibleTop = appSettings.WindowTop.Value;
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"加载设置失败: {ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        private void SaveSettings()
        {
            try
            {
                // 更新设置
                appSettings.ZoomFactor = currentZoomFactor;
                appSettings.IsMobileUA = isMobileUA;
                appSettings.WindowWidth = this.Width;
                appSettings.WindowHeight = this.Height;
                appSettings.IsDarkMode = isDarkMode;
                appSettings.IsTopMost = isTopMost;
                appSettings.IsSuperMinimalMode = isSuperMinimalMode;
                appSettings.IsAnimationEnabled = isAnimationEnabled;
                appSettings.WindowLeft = !double.IsNaN(lastVisibleLeft) ? lastVisibleLeft : this.Left;
                appSettings.WindowTop = !double.IsNaN(lastVisibleTop) ? lastVisibleTop : this.Top;
                appSettings.IsAdBlockEnabled = adBlocker.IsEnabled;
                appSettings.AdBlockRules = adBlocker.BlockRules;
                appSettings.AdAllowRules = adBlocker.AllowRules;
                
                var options = new JsonSerializerOptions 
                { 
                    WriteIndented = true,
                    Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
                };
                var json = JsonSerializer.Serialize(appSettings, options);
                File.WriteAllText(settingsFilePath, json);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"保存设置失败: {ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }





        private void AddBookmark_Click(object sender, RoutedEventArgs e)
        {
            string currentUrl = webView.CoreWebView2?.Source ?? AddressBar.Text;
            string title = webView.CoreWebView2?.DocumentTitle ?? "未知页面";
            
            if (!string.IsNullOrEmpty(currentUrl))
            {
                // 检查是否已经存在相同的收藏夹
                if (bookmarks.Any(b => b.Url.Equals(currentUrl, StringComparison.OrdinalIgnoreCase)))
                {
                    MessageBox.Show("该页面已经在收藏夹中了！", "提示", MessageBoxButton.OK, MessageBoxImage.Information);
                    return;
                }
                
                // 创建新的收藏夹对象
                var newBookmark = new Bookmark
                {
                    Title = title,
                    Url = currentUrl
                };
                
                // 添加到收藏夹列表
                bookmarks.Add(newBookmark);
                
                // 实时保存到JSON文件
                SaveBookmarks();
                
                // 刷新收藏夹菜单显示
                RefreshBookmarkMenu();
                
                MessageBox.Show($"已添加到收藏夹：{title}", "成功", MessageBoxButton.OK, MessageBoxImage.Information);
            }
        }

        private void EditBookmark(Bookmark bookmark)
        {
            // 创建编辑对话框
            var dialog = new Window
            {
                Title = "编辑收藏夹",
                Width = 400,
                Height = 200,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                Owner = this,
                ResizeMode = ResizeMode.NoResize
            };

            var grid = new Grid();
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            // 标题标签和文本框
            var titleLabel = new Label { Content = "标题:", Margin = new Thickness(10, 10, 5, 5) };
            Grid.SetRow(titleLabel, 0);
            Grid.SetColumn(titleLabel, 0);
            grid.Children.Add(titleLabel);

            var titleTextBox = new TextBox 
            { 
                Text = bookmark.Title, 
                Margin = new Thickness(5, 10, 10, 5),
                VerticalAlignment = VerticalAlignment.Center
            };
            Grid.SetRow(titleTextBox, 0);
            Grid.SetColumn(titleTextBox, 1);
            grid.Children.Add(titleTextBox);

            // URL标签和文本框
            var urlLabel = new Label { Content = "URL:", Margin = new Thickness(10, 5, 5, 5) };
            Grid.SetRow(urlLabel, 1);
            Grid.SetColumn(urlLabel, 0);
            grid.Children.Add(urlLabel);

            var urlTextBox = new TextBox 
            { 
                Text = bookmark.Url, 
                Margin = new Thickness(5, 5, 10, 5),
                VerticalAlignment = VerticalAlignment.Center
            };
            Grid.SetRow(urlTextBox, 1);
            Grid.SetColumn(urlTextBox, 1);
            grid.Children.Add(urlTextBox);

            // 按钮面板
            var buttonPanel = new StackPanel 
            { 
                Orientation = Orientation.Horizontal, 
                HorizontalAlignment = HorizontalAlignment.Right,
                Margin = new Thickness(10, 20, 10, 10)
            };
            Grid.SetRow(buttonPanel, 2);
            Grid.SetColumnSpan(buttonPanel, 2);

            var okButton = new Button 
            { 
                Content = "确定", 
                Width = 80, 
                Height = 30, 
                Margin = new Thickness(5, 0, 5, 0)
            };
            okButton.Click += (s, e) => {
                if (!string.IsNullOrWhiteSpace(titleTextBox.Text) && !string.IsNullOrWhiteSpace(urlTextBox.Text))
                {
                    bookmark.Title = titleTextBox.Text.Trim();
                    bookmark.Url = urlTextBox.Text.Trim();
                    SaveBookmarks();
                    RefreshBookmarkMenu();
                    dialog.DialogResult = true;
                    dialog.Close();
                }
                else
                {
                    MessageBox.Show("标题和URL不能为空！", "错误", MessageBoxButton.OK, MessageBoxImage.Warning);
                }
            };

            var cancelButton = new Button 
            { 
                Content = "取消", 
                Width = 80, 
                Height = 30, 
                Margin = new Thickness(5, 0, 5, 0)
            };
            cancelButton.Click += (s, e) => {
                dialog.DialogResult = false;
                dialog.Close();
            };

            buttonPanel.Children.Add(okButton);
            buttonPanel.Children.Add(cancelButton);
            grid.Children.Add(buttonPanel);

            dialog.Content = grid;
            dialog.ShowDialog();
        }

        private void EditBookmarkJson_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                // 确保收藏夹文件存在
                if (!File.Exists(bookmarksFilePath))
                {
                    SaveBookmarks(); // 创建文件
                }
                
                // 直接打开配置文件夹
                string configFolder = Path.GetDirectoryName(bookmarksFilePath);
                Process.Start(new ProcessStartInfo
                {
                    FileName = "explorer.exe",
                    Arguments = $"\"{configFolder}\"",
                    UseShellExecute = true
                });
            }
            catch (Exception ex)
            {
                MessageBox.Show($"打开配置文件夹失败: {ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void ToggleUserAgent()
        {
            if (webView.CoreWebView2 != null)
            {
                isMobileUA = !isMobileUA;
                
                if (isMobileUA)
                {
                    webView.CoreWebView2.Settings.UserAgent = MobileUA;
                    UAButton.Content = "▯";
                    UAButton.ToolTip = "切换用户代理 (当前: 手机)";
                }
                else
                {
                    webView.CoreWebView2.Settings.UserAgent = DesktopUA;
                    UAButton.Content = "🖳";
                    UAButton.ToolTip = "切换用户代理 (当前: 桌面)";
                }
                
                // 刷新当前页面以应用新的用户代理
                webView.CoreWebView2.Reload();
            }
        }
        
        private void ZoomIn_Click(object sender, RoutedEventArgs e)
        {
            currentZoomFactor += 0.1;
            if (currentZoomFactor > 3.0) currentZoomFactor = 3.0;
            webView.ZoomFactor = currentZoomFactor;
        }
        
        private void ZoomOut_Click(object sender, RoutedEventArgs e)
        {
            currentZoomFactor -= 0.1;
            if (currentZoomFactor < 0.3) currentZoomFactor = 0.3;
            webView.ZoomFactor = currentZoomFactor;
        }
        
        private void HamburgerMenu_MouseRightButtonDown(object sender, MouseButtonEventArgs e)
        {
            isResizing = true;
            resizeStartPoint = e.GetPosition(this);
            HamburgerMenu.CaptureMouse();
            e.Handled = true;
        }
        
        private void HamburgerMenu_MouseRightButtonUp(object sender, MouseButtonEventArgs e)
        {
            if (isResizing)
            {
                isResizing = false;
                HamburgerMenu.ReleaseMouseCapture();
                e.Handled = true;
            }
        }
        
        private void HamburgerMenu_MouseMove(object sender, MouseEventArgs e)
        {
            if (isResizing && e.RightButton == MouseButtonState.Pressed)
            {
                Point currentPoint = e.GetPosition(this);
                double deltaX = currentPoint.X - resizeStartPoint.X;
                double deltaY = currentPoint.Y - resizeStartPoint.Y;
                
                // 调整窗口大小
                double newWidth = this.Width + deltaX;
                double newHeight = this.Height + deltaY;
                
                // 设置最小尺寸限制
                if (newWidth >= 200)
                {
                    this.Width = newWidth;
                }
                
                if (newHeight >= 300)
                {
                    this.Height = newHeight;
                }
                
                // 更新起始点
                resizeStartPoint = currentPoint;
                e.Handled = true;
            }
        }

        private void HamburgerMenu_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            this.DragMove();
            RememberVisiblePosition();
        }

        private void DragButton_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            this.DragMove();
            RememberVisiblePosition();
        }
        
        // ResizeButton的窗口调整大小功能
        private void ResizeButton_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            isResizing = true;
            resizeStartPoint = e.GetPosition(this);
            ResizeButton.CaptureMouse();
            e.Handled = true;
        }
        
        private void ResizeButton_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            if (isResizing)
            {
                isResizing = false;
                ResizeButton.ReleaseMouseCapture();
                e.Handled = true;
            }
        }
        
        private void ResizeButton_MouseMove(object sender, MouseEventArgs e)
        {
            if (isResizing && e.LeftButton == MouseButtonState.Pressed)
            {
                Point currentPoint = e.GetPosition(this);
                double deltaX = currentPoint.X - resizeStartPoint.X;
                double deltaY = currentPoint.Y - resizeStartPoint.Y;
                
                // 调整窗口大小
                double newWidth = this.Width + deltaX;
                double newHeight = this.Height + deltaY;
                
                // 设置最小尺寸限制
                if (newWidth >= 200)
                {
                    this.Width = newWidth;
                }
                
                if (newHeight >= 300)
                {
                    this.Height = newHeight;
                }
                
                // 更新起始点
                resizeStartPoint = currentPoint;
                e.Handled = true;
            }
        }
        
        private void MainWindow_SourceInitialized(object sender, EventArgs e)
        {
            var hwndSource = HwndSource.FromHwnd(new WindowInteropHelper(this).Handle);
            hwndSource?.AddHook(WndProc);
        }
        
        private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
        {
            if (msg == WM_NCHITTEST)
            {
                var point = new Point(lParam.ToInt32() & 0xFFFF, lParam.ToInt32() >> 16);
                point = PointFromScreen(point);
                
                const int resizeBorder = 5;
                
                // 检查是否在边缘
                bool onLeft = point.X <= resizeBorder;
                bool onRight = point.X >= ActualWidth - resizeBorder;
                bool onTop = point.Y <= resizeBorder;
                bool onBottom = point.Y >= ActualHeight - resizeBorder;
                
                // 返回相应的调整大小区域
                if (onTop && onLeft) { handled = true; return new IntPtr(HTTOPLEFT); }
                if (onTop && onRight) { handled = true; return new IntPtr(HTTOPRIGHT); }
                if (onBottom && onLeft) { handled = true; return new IntPtr(HTBOTTOMLEFT); }
                if (onBottom && onRight) { handled = true; return new IntPtr(HTBOTTOMRIGHT); }
                if (onTop) { handled = true; return new IntPtr(HTTOP); }
                if (onBottom) { handled = true; return new IntPtr(HTBOTTOM); }
                if (onLeft) { handled = true; return new IntPtr(HTLEFT); }
                if (onRight) { handled = true; return new IntPtr(HTRIGHT); }
            }
            
            return IntPtr.Zero;
        }
        
        private Rect GetCurrentScreenWorkingAreaInWpfUnits()
        {
            try
            {
                var handle = new WindowInteropHelper(this).Handle;
                var screen = System.Windows.Forms.Screen.FromHandle(handle);
                var wa = screen.WorkingArea; // 像素坐标
                
                var source = PresentationSource.FromVisual(this);
                if (source?.CompositionTarget != null)
                {
                    var transform = source.CompositionTarget.TransformFromDevice;
                    var topLeft = transform.Transform(new System.Windows.Point(wa.Left, wa.Top));
                    var bottomRight = transform.Transform(new System.Windows.Point(wa.Right, wa.Bottom));
                    return new Rect(topLeft, bottomRight);
                }
                
                // 回退：假设96 DPI
                return new Rect(wa.Left, wa.Top, wa.Width, wa.Height);
            }
            catch
            {
                // 回退到主屏工作区
                var wa = SystemParameters.WorkArea;
                return new Rect(wa.Left, wa.Top, wa.Width, wa.Height);
            }
        }
        
        public void ToggleSuperMinimalMode(bool enabled)
        {
            isSuperMinimalMode = enabled;
            
            if (enabled)
            {
                // 隐藏底部工具栏
                BottomToolbar.Visibility = Visibility.Collapsed;
                // 隐藏顶部工具栏
                TopToolbar.Visibility = Visibility.Collapsed;
                
                // 让WebView2占用整个可用空间，将底部行高度设为0
                var mainGrid = (Grid)BottomToolbar.Parent;
                if (mainGrid != null && mainGrid.RowDefinitions.Count >= 3)
                {
                    mainGrid.RowDefinitions[2].Height = new GridLength(0);
                    // 同时将顶部行高度设为0
                    mainGrid.RowDefinitions[0].Height = new GridLength(0);
                }
            }
            else
            {
                // 显示底部工具栏
                BottomToolbar.Visibility = Visibility.Visible;
                // 显示顶部工具栏
                TopToolbar.Visibility = Visibility.Visible;
                
                // 恢复底部工具栏的高度
                var mainGrid = (Grid)BottomToolbar.Parent;
                if (mainGrid != null && mainGrid.RowDefinitions.Count >= 3)
                {
                    mainGrid.RowDefinitions[2].Height = new GridLength(40);
                    // 恢复顶部工具栏高度（默认35）
                    mainGrid.RowDefinitions[0].Height = new GridLength(35);
                }
            }

            // 切换极简模式后重新应用圆角（WebView 撑满/收起时需要更新原生圆角区域）
            // 延迟到布局完成后执行，确保拿到 WebView 的最新尺寸
            Dispatcher.BeginInvoke(new Action(ApplyRoundedCorners), System.Windows.Threading.DispatcherPriority.Loaded);

            // 保存设置
            SaveSettings();
        }

        public void ToggleAnimation(bool enabled)
        {
            isAnimationEnabled = enabled;
            SaveSettings();
        }
        
        public bool IsSuperMinimalMode => isSuperMinimalMode;
        
        private void CleanupWebViewMemory()
        {
            try
            {
                if (webView?.CoreWebView2 != null)
                {
                    // 清理浏览器缓存和内存
                    _ = Task.Run(async () =>
                    {
                        try
                        {
                            // 1. 清理所有类型的浏览数据
                            await webView.CoreWebView2.Profile.ClearBrowsingDataAsync(
                                Microsoft.Web.WebView2.Core.CoreWebView2BrowsingDataKinds.AllDomStorage |
                                Microsoft.Web.WebView2.Core.CoreWebView2BrowsingDataKinds.AllSite |
                                Microsoft.Web.WebView2.Core.CoreWebView2BrowsingDataKinds.DiskCache |
                                Microsoft.Web.WebView2.Core.CoreWebView2BrowsingDataKinds.Cookies |
                                Microsoft.Web.WebView2.Core.CoreWebView2BrowsingDataKinds.BrowsingHistory |
                                Microsoft.Web.WebView2.Core.CoreWebView2BrowsingDataKinds.DownloadHistory |
                                Microsoft.Web.WebView2.Core.CoreWebView2BrowsingDataKinds.GeneralAutofill |
                                Microsoft.Web.WebView2.Core.CoreWebView2BrowsingDataKinds.PasswordAutosave
                            );
                            
                            // 2. 尝试设置内存使用目标为低
                            try
                            {
                                webView.CoreWebView2.MemoryUsageTargetLevel = 
                                    Microsoft.Web.WebView2.Core.CoreWebView2MemoryUsageTargetLevel.Low;
                            }
                            catch (Exception memEx)
                            {
                                System.Diagnostics.Debug.WriteLine($"设置内存目标异常: {memEx.Message}");
                            }
                            
                            // 3. 强制垃圾回收（多次执行确保彻底清理）
                            for (int i = 0; i < 3; i++)
                            {
                                GC.Collect();
                                GC.WaitForPendingFinalizers();
                                await Task.Delay(100); // 短暂延迟让系统处理
                            }
                            
                            // 4. 尝试压缩大对象堆
                            GCSettings.LargeObjectHeapCompactionMode = GCLargeObjectHeapCompactionMode.CompactOnce;
                            GC.Collect();
                            
                        }
                        catch (Exception ex)
                        {
                            System.Diagnostics.Debug.WriteLine($"清理缓存异常: {ex.Message}");
                        }
                    });
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"内存清理异常: {ex.Message}");
            }
        }
        
        private System.Windows.Threading.DispatcherTimer? memoryCleanupTimer;
        
        private void StartMemoryCleanupTimer()
        {
            // 创建定时器，每2分钟清理一次内存（更频繁的清理）
            memoryCleanupTimer = new System.Windows.Threading.DispatcherTimer
            {
                Interval = TimeSpan.FromMinutes(2)
            };
            
            memoryCleanupTimer.Tick += (sender, e) =>
            {
                // 智能清理策略：根据内存使用情况决定清理强度
                _ = Task.Run(async () =>
                {
                    try
                    {
                        // 获取当前进程内存使用情况
                        var currentProcess = System.Diagnostics.Process.GetCurrentProcess();
                        var memoryUsage = currentProcess.WorkingSet64 / (1024 * 1024); // MB
                        
                        System.Diagnostics.Debug.WriteLine($"当前内存使用: {memoryUsage}MB");
                        
                        // 如果内存使用超过200MB，执行强力清理
                        if (memoryUsage > 200)
                        {
                            System.Diagnostics.Debug.WriteLine("执行强力内存清理");
                            await Dispatcher.InvokeAsync(() => CleanupWebViewMemory());
                            
                            // 如果内存使用超过400MB，考虑重置WebView环境
                            if (memoryUsage > 400)
                            {
                                System.Diagnostics.Debug.WriteLine("内存使用过高，考虑重置WebView环境");
                                await Dispatcher.InvokeAsync(async () => await ResetWebViewEnvironment());
                            }
                        }
                        else
                        {
                            // 轻量级清理
                            GC.Collect(0, GCCollectionMode.Optimized);
                        }
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine($"智能清理异常: {ex.Message}");
                    }
                });
            };
            
            memoryCleanupTimer.Start();
        }
        
        private void StopMemoryCleanupTimer()
        {
            memoryCleanupTimer?.Stop();
            memoryCleanupTimer = null;
        }
        
        // WebView2环境重置功能 - 用于彻底清理WebView2进程
        private async Task ResetWebViewEnvironment()
        {
            try
            {
                if (webView != null)
                {
                    // 获取父级容器
                    var parent = webView.Parent as Panel;
                    if (parent == null) return;
                    
                    // 记录索引
                    int index = parent.Children.IndexOf(webView);
                    
                    // 1. 停止内存清理定时器
                    StopMemoryCleanupTimer();
                    
                    // 2. 尝试停止当前操作
                    if (webView.CoreWebView2 != null)
                    {
                        webView.CoreWebView2.Stop();
                    }
                    
                    // 3. 从 UI 移除并释放 (WPF 中 Dispose 后不能重用)
                    parent.Children.RemoveAt(index);
                    webView.Dispose();
                    
                    // 4. 强制垃圾回收
                    GC.Collect();
                    GC.WaitForPendingFinalizers();
                    GC.Collect();
                    
                    // 5. 短暂延迟
                    await Task.Delay(1000);
                    
                    // 6. 创建新实例并放回原位
                    webView = new Microsoft.Web.WebView2.Wpf.WebView2();
                    webView.Name = "webView";
                    Grid.SetRow(webView, 1);
                    parent.Children.Insert(index, webView);
                    
                    // 7. 重新初始化
                    await InitializeWebViewInternal();
                    
                    // 8. 重新初始化广告拦截器
                    InitializeAdBlocker();
                    
                    // 9. 重新启动内存清理定时器
                    StartMemoryCleanupTimer();
                    
                    System.Diagnostics.Debug.WriteLine("WebView2环境重置完成");
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"WebView2环境重置异常: {ex.Message}");
            }
        }
        
        public bool IsAnimationEnabled => isAnimationEnabled;
        
        private void InitializeAdBlocker()
        {
            try
            {
                if (webView?.CoreWebView2 != null)
                {
                    adBlocker.Initialize(webView.CoreWebView2);
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"初始化广告拦截器失败: {ex.Message}");
            }
        }
        
        public void ToggleAdBlock(bool enabled)
        {
            adBlocker.IsEnabled = enabled;
            SaveSettings();
        }
        
        public bool IsAdBlockEnabled => adBlocker.IsEnabled;
        
        public void ApplySettings(AppSettings settings)
        {
            try
            {
                // 应用缩放
                if (settings.AutoZoomOutOnStartup)
                {
                    currentZoomFactor = 0.8;
                }
                else
                {
                    currentZoomFactor = settings.ZoomFactor;
                }

                if (webView?.CoreWebView2 != null)
                {
                    webView.ZoomFactor = currentZoomFactor;
                }
                
                // 应用UA
                isMobileUA = settings.IsMobileUA;
                if (webView?.CoreWebView2 != null)
                {
                    webView.CoreWebView2.Settings.UserAgent = isMobileUA ? MobileUA : DesktopUA;
                    UAButton.Content = isMobileUA ? "▯" : "🖳";
                    UAButton.ToolTip = isMobileUA ? "切换用户代理 (当前: 手机)" : "切换用户代理 (当前: 桌面)";
                }
                
                // 应用暗色模式
                isDarkMode = settings.IsDarkMode;
                ApplyBrowserAppearance(isDarkMode);
                UpdateDarkModeButtonAppearance();
                UpdateUIAppearance(isDarkMode);
                
                // 应用置顶
                isTopMost = settings.IsTopMost;
                this.Topmost = isTopMost;
                UpdateTopMostButtonAppearance();
                
                // 应用极简模式
                isSuperMinimalMode = settings.IsSuperMinimalMode;
                ToggleSuperMinimalMode(isSuperMinimalMode);
                
                // 应用动画
                isAnimationEnabled = settings.IsAnimationEnabled;
                
                // 应用广告拦截
                adBlocker.IsEnabled = settings.IsAdBlockEnabled;
                if (settings.AdBlockRules != null && settings.AdBlockRules.Count > 0)
                {
                    adBlocker.BlockRules = settings.AdBlockRules;
                }
                if (settings.AdAllowRules != null)
                {
                    adBlocker.AllowRules = settings.AdAllowRules;
                }

                // 应用代理设置
                bool proxyChanged = (appSettings.IsProxyEnabled != settings.IsProxyEnabled) || 
                                   (appSettings.ProxyServer != settings.ProxyServer);
                
                appSettings.IsProxyEnabled = settings.IsProxyEnabled;
                appSettings.ProxyServer = settings.ProxyServer;
                
                if (proxyChanged)
                {
                    _ = UpdateProxyConfig(); // 异步调用
                }

                // 应用圆角设置
                appSettings.EnableRoundedCorners = settings.EnableRoundedCorners;
                appSettings.CornerRadiusPx = settings.CornerRadiusPx;
                ApplyRoundedCorners();

                // 保存设置
                SaveSettings();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"应用设置失败: {ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        public void ShowAdBlockSettings()
        {
            var dialog = new Window
            {
                Title = "广告拦截设置",
                Width = 600,
                Height = 500,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                Owner = this,
                ResizeMode = ResizeMode.CanResize
            };

            var grid = new Grid();
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            // 启用开关
            var enableCheckBox = new CheckBox
            {
                Content = "启用广告拦截",
                IsChecked = adBlocker.IsEnabled,
                Margin = new Thickness(10, 10, 10, 5)
            };
            enableCheckBox.Checked += (s, e) => { adBlocker.IsEnabled = true; SaveSettings(); };
            enableCheckBox.Unchecked += (s, e) => { adBlocker.IsEnabled = false; SaveSettings(); };
            Grid.SetRow(enableCheckBox, 0);
            grid.Children.Add(enableCheckBox);

            // 拦截规则标签
            var blockLabel = new Label
            {
                Content = "拦截规则（每行一个，支持通配符 * 和域名匹配）：",
                Margin = new Thickness(10, 5, 10, 5)
            };
            Grid.SetRow(blockLabel, 1);
            grid.Children.Add(blockLabel);

            // 拦截规则文本框
            var blockTextBox = new TextBox
            {
                AcceptsReturn = true,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(10, 0, 10, 5),
                FontFamily = new FontFamily("Consolas")
            };
            blockTextBox.Text = string.Join("\r\n", adBlocker.BlockRules);
            Grid.SetRow(blockTextBox, 2);
            grid.Children.Add(blockTextBox);

            // 允许规则标签
            var allowLabel = new Label
            {
                Content = "允许规则（白名单，优先级高于拦截规则）：",
                Margin = new Thickness(10, 5, 10, 5)
            };
            Grid.SetRow(allowLabel, 3);
            grid.Children.Add(allowLabel);

            // 允许规则文本框
            var allowTextBox = new TextBox
            {
                AcceptsReturn = true,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(10, 0, 10, 5),
                FontFamily = new FontFamily("Consolas")
            };
            allowTextBox.Text = string.Join("\r\n", adBlocker.AllowRules);
            Grid.SetRow(allowTextBox, 4);
            grid.Children.Add(allowTextBox);

            // 按钮面板
            var buttonPanel = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Right,
                Margin = new Thickness(10, 10, 10, 10)
            };
            Grid.SetRow(buttonPanel, 5);

            var loadDefaultButton = new Button
            {
                Content = "加载默认规则",
                Width = 120,
                Height = 30,
                Margin = new Thickness(5, 0, 5, 0)
            };
            loadDefaultButton.Click += (s, e) =>
            {
                adBlocker.LoadDefaultRules();
                blockTextBox.Text = string.Join("\r\n", adBlocker.BlockRules);
            };

            var okButton = new Button
            {
                Content = "确定",
                Width = 80,
                Height = 30,
                Margin = new Thickness(5, 0, 5, 0)
            };
            okButton.Click += (s, e) =>
            {
                adBlocker.BlockRules = blockTextBox.Text.Split(new[] { "\r\n", "\r", "\n" }, StringSplitOptions.RemoveEmptyEntries)
                    .Select(r => r.Trim())
                    .Where(r => !string.IsNullOrEmpty(r))
                    .ToList();
                adBlocker.AllowRules = allowTextBox.Text.Split(new[] { "\r\n", "\r", "\n" }, StringSplitOptions.RemoveEmptyEntries)
                    .Select(r => r.Trim())
                    .Where(r => !string.IsNullOrEmpty(r))
                    .ToList();
                SaveSettings();
                dialog.DialogResult = true;
                dialog.Close();
            };

            var cancelButton = new Button
            {
                Content = "取消",
                Width = 80,
                Height = 30,
                Margin = new Thickness(5, 0, 5, 0)
            };
            cancelButton.Click += (s, e) =>
            {
                dialog.DialogResult = false;
                dialog.Close();
            };

            buttonPanel.Children.Add(loadDefaultButton);
            buttonPanel.Children.Add(okButton);
            buttonPanel.Children.Add(cancelButton);
            grid.Children.Add(buttonPanel);

            dialog.Content = grid;
            dialog.ShowDialog();
        }
    }

    public class Bookmark
    {
        public string Title { get; set; } = string.Empty;
        public string Url { get; set; } = string.Empty;
    }

    public class AppSettings
    {
        public double ZoomFactor { get; set; } = 1.0;
        public bool IsMobileUA { get; set; } = true;
        public double WindowWidth { get; set; } = 360;
        public double WindowHeight { get; set; } = 640;
        public double? WindowLeft { get; set; }
        public double? WindowTop { get; set; }
        public bool IsDarkMode { get; set; } = false;
        public bool IsTopMost { get; set; } = true;
        public bool IsSuperMinimalMode { get; set; } = false;
        public bool IsAnimationEnabled { get; set; } = true;
        
        // 广告拦截设置
        public bool IsAdBlockEnabled { get; set; } = false;
        public List<string> AdBlockRules { get; set; } = new List<string>();
        public List<string> AdAllowRules { get; set; } = new List<string>();
        
        // 代理设置
        public bool IsProxyEnabled { get; set; } = false;
        public string ProxyServer { get; set; } = "127.0.0.1:7890";
        
        // 圆角设置
        public bool EnableRoundedCorners { get; set; } = true;
        public double CornerRadiusPx { get; set; } = 8;

        // 全局快捷键设置
        public string Hotkey { get; set; } = "alt + x";
        public bool EnableGlobalHotKey { get; set; } = true;
        
        // 启动设置
        public bool AutoZoomOutOnStartup { get; set; } = true;
        
        // 内部使用的快捷键解析属性
        public uint HotKeyModifiers 
        { 
            get 
            {
                if (string.IsNullOrEmpty(Hotkey)) return 1;
                var lower = Hotkey.ToLower();
                uint modifiers = 0;
                if (lower.Contains("alt")) modifiers |= 1;
                if (lower.Contains("ctrl") || lower.Contains("control")) modifiers |= 2;
                if (lower.Contains("shift")) modifiers |= 4;
                if (lower.Contains("win") || lower.Contains("windows")) modifiers |= 8;
                return modifiers == 0 ? 1 : modifiers; // 默认Alt
            }
        }
        
        public uint HotKeyVirtualKey 
        { 
            get 
            {
                if (string.IsNullOrEmpty(Hotkey)) return 0x58;
                var lower = Hotkey.ToLower();
                // 提取最后一个字符作为按键
                var parts = lower.Split(new char[] { '+', ' ' }, StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length > 0)
                {
                    var key = parts[parts.Length - 1].Trim();
                    if (key.Length == 1)
                    {
                        char c = key[0];
                        if (c >= 'a' && c <= 'z')
                        {
                            return (uint)(c - 'a' + 0x41); // A-Z键码
                        }
                    }
                }
                return 0x58; // 默认X键
            }
        }
    }
}