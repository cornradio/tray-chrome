using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using Microsoft.Web.WebView2.Core;

namespace TrayChrome
{
    public partial class MainWindow : Window
    {
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

        public MainWindow()
        {
            InitializeComponent();
            LoadSettings();
            InitializeWebView();
            LoadBookmarks();
            SetupWindowAnimation();
            
            // 添加汉堡菜单拖拽功能
            HamburgerMenu.MouseLeftButtonDown += (sender, e) => {
                e.Handled = true;
                this.DragMove();
            };
            
            // 添加汉堡菜单右键调整窗口大小功能
            HamburgerMenu.MouseRightButtonDown += HamburgerMenu_MouseRightButtonDown;
            HamburgerMenu.MouseRightButtonUp += HamburgerMenu_MouseRightButtonUp;
            HamburgerMenu.MouseMove += HamburgerMenu_MouseMove;
            
            // 窗口关闭时保存设置
            this.Closing += (sender, e) => SaveSettings();
        }

        private async void InitializeWebView()
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
                
                // 监听导航事件
                webView.CoreWebView2.NavigationCompleted += CoreWebView2_NavigationCompleted;
                webView.CoreWebView2.SourceChanged += CoreWebView2_SourceChanged;
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
            });
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

        private void BookmarkButton_Click(object sender, RoutedEventArgs e)
        {
            // 显示右键菜单
            BookmarkContextMenu.IsOpen = true;
        }

        private void UAButton_Click(object sender, RoutedEventArgs e)
        {
            ToggleUserAgent();
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
            
            var animation = new DoubleAnimation
            {
                From = workingArea.Bottom + 50,
                To = targetTop,
                Duration = TimeSpan.FromMilliseconds(300),
                EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut }
            };
            
            BeginAnimation(TopProperty, animation);
        }

        public void HideWithAnimation()
        {
            var workingArea = SystemParameters.WorkArea;
            
            var animation = new DoubleAnimation
            {
                From = Top,
                To = workingArea.Bottom + 50,
                Duration = TimeSpan.FromMilliseconds(300),
                EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseIn }
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
                // 创建收藏夹菜单项
                MenuItem bookmarkItem = new MenuItem
                {
                    Header = title,
                    Tag = currentUrl,
                    ToolTip = currentUrl
                };
                
                bookmarkItem.Click += (s, args) => {
                    webView.CoreWebView2?.Navigate(bookmarkItem.Tag.ToString());
                };
                
                // 添加到右键菜单
                BookmarkContextMenu.Items.Add(bookmarkItem);
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
                    UAButton.Content = "📱";
                    UAButton.ToolTip = "切换用户代理 (当前: 手机)";
                }
                else
                {
                    webView.CoreWebView2.Settings.UserAgent = DesktopUA;
                    UAButton.Content = "🖥️";
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
                
                // 设置最小和最大尺寸限制
                if (newWidth >= 200 && newWidth <= 1200)
                {
                    this.Width = newWidth;
                }
                
                if (newHeight >= 300 && newHeight <= 1000)
                {
                    this.Height = newHeight;
                }
                
                // 更新起始点
                resizeStartPoint = currentPoint;
                e.Handled = true;
            }
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
    }
}