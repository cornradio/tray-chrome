using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text.Json;
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
        private string bookmarksFilePath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "TrayChrome", "bookmarks.json");
        private string settingsFilePath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "TrayChrome", "settings.json");
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
        
        // 用于更新托盘图标提示的事件
        public event Action<string> TitleChanged;

        public MainWindow(string? startupUrl = null, bool useCleanMode = false, bool forceUncleanMode = false)
        {
            InitializeComponent();
            LoadSettings();
            
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
            
            // 窗口关闭时保存设置
            this.Closing += (sender, e) => SaveSettings();
            
            // 启用窗口边缘调整大小功能
            this.SourceInitialized += MainWindow_SourceInitialized;
            
            // 初始化托盘提示
            UpdateTrayTooltip();
            
            // 初始化暗色模式按钮外观
            UpdateDarkModeButtonAppearance();
        }

        private async void InitializeWebView(string? startupUrl = null)
        {
            try
            {
                await webView.EnsureCoreWebView2Async(null);
                
                // 设置用户代理
                webView.CoreWebView2.Settings.UserAgent = isMobileUA ? MobileUA : DesktopUA;
                
                // 应用缩放设置
                webView.ZoomFactor = currentZoomFactor;
                
                // 启用开发者工具
                webView.CoreWebView2.Settings.AreDevToolsEnabled = true;
                
                // 初始化时设置浏览器外观模式
                ApplyBrowserAppearance(isDarkMode);
                
                // 监听导航事件
                webView.CoreWebView2.NavigationCompleted += CoreWebView2_NavigationCompleted;
                webView.CoreWebView2.SourceChanged += CoreWebView2_SourceChanged;
                
                // 监听文档标题变化事件
                webView.CoreWebView2.DocumentTitleChanged += CoreWebView2_DocumentTitleChanged;
                
                // 拦截新窗口打开请求，在当前窗口中打开
                webView.CoreWebView2.NewWindowRequested += CoreWebView2_NewWindowRequested;
                
                // 如果提供了启动URL，导航到该URL
                if (!string.IsNullOrEmpty(startupUrl))
                {
                    webView.CoreWebView2.Navigate(startupUrl);
                    AddressBar.Text = startupUrl;
                }
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
            
            // 设置窗口位置到屏幕右下角
            var workingArea = SystemParameters.WorkArea;
            Left = workingArea.Right - Width - 20;
            Top = workingArea.Bottom - Height - 20;
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
            // 初始化窗口位置到屏幕下方
            var workingArea = SystemParameters.WorkArea;
            Left = workingArea.Right - Width - 20;
            Top = workingArea.Bottom + 50; // 隐藏在屏幕下方
        }

        public void ShowWithAnimation()
        {
            var workingArea = SystemParameters.WorkArea;
            var targetTop = workingArea.Bottom - Height - 20;
            
            Show();
            Activate(); // 确保窗口获得焦点
            
            // 检查是否应该禁用动画
            if (SystemAnimationHelper.ShouldDisableAnimation(isAnimationEnabled))
            {
                // 直接设置位置，不使用动画
                Top = targetTop;
                return;
            }
            
            var animation = new DoubleAnimation
            {
                From = workingArea.Bottom + 50,
                To = targetTop,
                Duration = TimeSpan.FromMilliseconds(100), // 缩短动画时间，提升流畅度
                EasingFunction = new SmoothEase { EasingMode = EasingMode.EaseOut } // 使用自定义流畅缓动函数
            };
            

            
            BeginAnimation(TopProperty, animation);
        }

        public void HideWithAnimation()
        {
            var workingArea = SystemParameters.WorkArea;
            
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

        private void RefreshBookmarkMenu()
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
                
                bookmarkItem.Click += (s, args) => {
                    if (bookmarkItem.Tag != null)
                    {
                        webView.CoreWebView2?.Navigate(bookmarkItem.Tag.ToString());
                    }
                };
                
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
                
                var json = JsonSerializer.Serialize(appSettings, new JsonSerializerOptions { WriteIndented = true });
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
        
        public void ToggleSuperMinimalMode(bool enabled)
        {
            isSuperMinimalMode = enabled;
            
            if (enabled)
            {
                // 隐藏底部工具栏
                BottomToolbar.Visibility = Visibility.Collapsed;
                
                // 让WebView2占用整个可用空间，将底部行高度设为0
                var mainGrid = (Grid)BottomToolbar.Parent;
                if (mainGrid != null && mainGrid.RowDefinitions.Count >= 3)
                {
                    mainGrid.RowDefinitions[2].Height = new GridLength(0);
                }
            }
            else
            {
                // 显示底部工具栏
                BottomToolbar.Visibility = Visibility.Visible;
                
                // 恢复底部工具栏的高度
                var mainGrid = (Grid)BottomToolbar.Parent;
                if (mainGrid != null && mainGrid.RowDefinitions.Count >= 3)
                {
                    mainGrid.RowDefinitions[2].Height = new GridLength(40);
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
        public bool IsAnimationEnabled => isAnimationEnabled;
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
        public bool IsDarkMode { get; set; } = false;
        public bool IsTopMost { get; set; } = true;
        public bool IsSuperMinimalMode { get; set; } = false;
        public bool IsAnimationEnabled { get; set; } = true;
    }
}