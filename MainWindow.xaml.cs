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
using Microsoft.Web.WebView2.Core;

namespace TrayChrome
{
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
        private AdBlocker adBlocker = new AdBlocker(); // 广告拦截器
        
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
            SetupWindowAnimation();
            
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
            
            // 启动内存清理定时器
            StartMemoryCleanupTimer();
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
                    // 创建环境选项以启用 FluentOverlay 滚动条
                    var options = new CoreWebView2EnvironmentOptions();
                    
                    // 尝试设置 FluentOverlay 滚动条
                    try
                    {
                        // 使用反射检查并设置 ScrollBarStyle 属性（较新版本的 WebView2 SDK 支持）
                        var optionsType = typeof(CoreWebView2EnvironmentOptions);
                        var scrollBarStyleProperty = optionsType.GetProperty("ScrollBarStyle");
                        if (scrollBarStyleProperty != null)
                        {
                            // 获取枚举类型并设置值
                            var enumType = scrollBarStyleProperty.PropertyType;
                            var fluentOverlayValue = Enum.Parse(enumType, "FluentOverlay");
                            scrollBarStyleProperty.SetValue(options, fluentOverlayValue);
                            System.Diagnostics.Debug.WriteLine("已设置 ScrollBarStyle 为 FluentOverlay");
                        }
                        else
                        {
                            // 如果属性不存在，使用浏览器标志方式
                            var additionalBrowserArgumentsProperty = optionsType.GetProperty("AdditionalBrowserArguments");
                            if (additionalBrowserArgumentsProperty != null)
                            {
                                var currentArgs = additionalBrowserArgumentsProperty.GetValue(options) as string ?? "";
                                var newArgs = string.IsNullOrEmpty(currentArgs) 
                                    ? "--enable-features=msEdgeFluentOverlayScrollbar" 
                                    : currentArgs + " --enable-features=msEdgeFluentOverlayScrollbar";
                                additionalBrowserArgumentsProperty.SetValue(options, newArgs);
                                System.Diagnostics.Debug.WriteLine("已使用浏览器标志启用 FluentOverlay 滚动条");
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        // 如果设置失败，尝试使用浏览器标志作为备选方案
                        System.Diagnostics.Debug.WriteLine($"设置 FluentOverlay 滚动条失败，尝试使用浏览器标志: {ex.Message}");
                        try
                        {
                            var optionsType = typeof(CoreWebView2EnvironmentOptions);
                            var additionalBrowserArgumentsProperty = optionsType.GetProperty("AdditionalBrowserArguments");
                            if (additionalBrowserArgumentsProperty != null)
                            {
                                var currentArgs = additionalBrowserArgumentsProperty.GetValue(options) as string ?? "";
                                var newArgs = string.IsNullOrEmpty(currentArgs) 
                                    ? "--enable-features=msEdgeFluentOverlayScrollbar" 
                                    : currentArgs + " --enable-features=msEdgeFluentOverlayScrollbar";
                                additionalBrowserArgumentsProperty.SetValue(options, newArgs);
                                System.Diagnostics.Debug.WriteLine("已使用浏览器标志启用 FluentOverlay 滚动条（备选方案）");
                            }
                        }
                        catch { }
                    }
                    
                    // 创建环境并初始化 WebView2（在第一次调用时传入自定义环境）
                    var environment = await CoreWebView2Environment.CreateAsync(null, null, options);
                    await webView.EnsureCoreWebView2Async(environment);
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
         
         private void UpdateTopMostButtonAppearance()
         {
             if (TopMostButton != null)
             {
                 TopMostButton.Content = isTopMost ? "📌" : "⚲";
                 TopMostButton.ToolTip = isTopMost ? "取消置顶" : "窗口置顶";
             }
         }

        private void BackButton_Click(object sender, RoutedEventArgs e)
        {
            if (webView.CoreWebView2?.CanGoBack == true)
            {
                webView.CoreWebView2.GoBack();
            }
        }

        private void ForwardButton_Click(object sender, RoutedEventArgs e)
        {
            if (webView.CoreWebView2?.CanGoForward == true)
            {
                webView.CoreWebView2.GoForward();
            }
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

        private void SetupWindowAnimation()
        {
            // 初始化窗口位置到当前屏幕下方（不改变 Left，仅调整 Top）
            var workingArea = GetCurrentScreenWorkingAreaInWpfUnits();
            Top = workingArea.Bottom + 50; // 隐藏在屏幕下方
        }

        public void ShowWithAnimation()
        {
            // 先显示窗口，使得 DPI/可视化源可用
            Show();
            Activate(); // 确保窗口获得焦点
            
            var workingArea = GetCurrentScreenWorkingAreaInWpfUnits();
            
            // 期望位置：右下角，留 20 边距
            double targetLeft = Left;
            // 如果 Left 还未设置，使用默认右下角
            if (double.IsNaN(targetLeft) || double.IsInfinity(targetLeft))
            {
                targetLeft = workingArea.Right - Width - 20;
            }
            double targetTop = workingArea.Bottom - Height - 20;
            
            // 钳制到当前屏幕工作区（考虑边距）
            double minLeft = workingArea.Left;
            double maxLeft = workingArea.Right - Width - 20;
            if (maxLeft < minLeft) maxLeft = minLeft; // 防御：窗口宽度大于工作区
            targetLeft = Math.Max(minLeft, Math.Min(targetLeft, maxLeft));
            
            double minTop = workingArea.Top;
            double maxTop = workingArea.Bottom - Height - 20;
            if (maxTop < minTop) maxTop = minTop; // 防御：窗口高度大于工作区
            targetTop = Math.Max(minTop, Math.Min(targetTop, maxTop));
            
            Left = targetLeft;
            
            // 检查是否应该禁用动画
            if (SystemAnimationHelper.ShouldDisableAnimation(isAnimationEnabled))
            {
                // 直接设置最终位置
                Top = targetTop;
                return;
            }
            
            var animation = new DoubleAnimation
            {
                From = Top, // 使用当前 Top 作为动画起点（通常为屏幕底部外 50）
                To = targetTop,
                Duration = TimeSpan.FromMilliseconds(100), // 缩短动画时间，提升流畅度
                EasingFunction = new SmoothEase { EasingMode = EasingMode.EaseOut } // 使用自定义流畅缓动函数
            };
            
            BeginAnimation(TopProperty, animation);
        }

        public void HideWithAnimation()
        {
            var workingArea = GetCurrentScreenWorkingAreaInWpfUnits();
            
            // 检查是否应该禁用动画
            if (SystemAnimationHelper.ShouldDisableAnimation(isAnimationEnabled))
            {
                // 直接隐藏，不使用动画
                Hide();
                return;
            }
            
            var animation = new DoubleAnimation
            {
                From = Top,
                To = workingArea.Bottom + 50,
                Duration = TimeSpan.FromMilliseconds(100), // 隐藏动画更快一些
                EasingFunction = new SmoothEase { EasingMode = EasingMode.EaseIn } // 使用自定义流畅缓动函数
            };
            
            animation.Completed += (s, e) => Hide();
            BeginAnimation(TopProperty, animation);
        }



        private void LoadBookmarks()
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
            // 清除现有的收藏夹菜单项（保留"添加到收藏夹"和分隔符）
            var itemsToRemove = BookmarkContextMenu.Items.Cast<object>().Skip(2).ToList();
            foreach (var item in itemsToRemove)
            {
                BookmarkContextMenu.Items.Remove(item);
            }
            
            // 添加所有收藏夹到菜单
            foreach (var bookmark in bookmarks)
            {
                MenuItem bookmarkItem = new MenuItem
                {
                    Header = bookmark.Title,
                    Tag = bookmark.Url,
                    ToolTip = bookmark.Url
                };
                
                // 左键点击导航
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
                
                // 添加右键上下文菜单
                ContextMenu itemContextMenu = new ContextMenu();
                
                MenuItem editItem = new MenuItem { Header = "编辑" };
                editItem.Click += (s, args) => EditBookmark(bookmark);
                
                MenuItem deleteItem = new MenuItem { Header = "删除" };
                deleteItem.Click += (s, args) => {
                    var result = MessageBox.Show($"确定要删除收藏夹 \"{bookmark.Title}\" 吗？", 
                        "删除收藏夹", MessageBoxButton.YesNo, MessageBoxImage.Question);
                    
                    if (result == MessageBoxResult.Yes)
                    {
                        bookmarks.Remove(bookmark);
                        SaveBookmarks();
                        RefreshBookmarkMenu();
                    }
                };
                
                itemContextMenu.Items.Add(editItem);
                itemContextMenu.Items.Add(deleteItem);
                bookmarkItem.ContextMenu = itemContextMenu;
                
                BookmarkContextMenu.Items.Add(bookmarkItem);
            }
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
                appSettings.WindowLeft = this.Left;
                appSettings.WindowTop = this.Top;
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
        }
        
        private void DragButton_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            this.DragMove();
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
                if (webView?.CoreWebView2 != null)
                {
                    // 1. 停止内存清理定时器
                    StopMemoryCleanupTimer();
                    
                    // 2. 清理所有浏览数据
                    await webView.CoreWebView2.Profile.ClearBrowsingDataAsync();
                    
                    // 3. 尝试关闭WebView2
                    webView.CoreWebView2.Stop();
                    
                    // 4. 释放WebView2资源
                    webView.Dispose();
                    
                    // 5. 强制垃圾回收
                    GC.Collect();
                    GC.WaitForPendingFinalizers();
                    GC.Collect();
                    
                    // 6. 短暂延迟让系统清理进程
                    await Task.Delay(2000);
                    
                    // 7. 重新初始化WebView2
                    await webView.EnsureCoreWebView2Async();
                    
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
                currentZoomFactor = settings.ZoomFactor;
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
        
        // 全局快捷键设置
        public string Hotkey { get; set; } = "alt + x";
        public bool EnableGlobalHotKey { get; set; } = true;
        
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