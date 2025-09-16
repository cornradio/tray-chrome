using System;
using System.Diagnostics;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using Hardcodet.Wpf.TaskbarNotification;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;

namespace TrayChrome
{
    public partial class App : Application
    {
        private TaskbarIcon? trayIcon;
        private MainWindow? mainWindow;
        private static int instanceCounter = 0;
        private int currentInstanceId;
        private List<Bookmark> bookmarks = new List<Bookmark>();
        private string bookmarksFilePath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "TrayChrome", "bookmarks.json");

        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);

            // 分配实例ID
            currentInstanceId = ++instanceCounter;

            // 创建托盘图标
            trayIcon = (TaskbarIcon)FindResource("TrayIcon");
            
            // 更新托盘图标标识
            if (trayIcon != null)
            {
                trayIcon.ToolTipText = $"Tray Chrome Browser - 实例 {currentInstanceId}";
            }
            
            // 创建主窗口但不显示
            mainWindow = new MainWindow();
            mainWindow.Hide();
            
            // 订阅标题变化事件
            mainWindow.TitleChanged += OnMainWindowTitleChanged;
            
            // 更新托盘菜单中超级极简模式的状态
            UpdateSuperMinimalModeMenuState();
            
            // 加载收藏夹并刷新托盘菜单
            LoadBookmarks();
            RefreshTrayBookmarkMenu();
            
            // 隐藏主窗口，只显示托盘图标
            MainWindow = mainWindow;
            MainWindow.WindowState = WindowState.Minimized;
            MainWindow.ShowInTaskbar = false;
        }

        private void TrayIcon_TrayLeftMouseUp(object sender, RoutedEventArgs e)
        {
            if (mainWindow != null)
            {
                if (mainWindow.IsVisible)
                {
                    mainWindow.HideWithAnimation();
                }
                else
                {
                    mainWindow.ShowWithAnimation();
                    mainWindow.WindowState = WindowState.Normal;
                    mainWindow.Activate();
                }
            }
        }

        private void AddInstance_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                // 启动新的应用程序实例
                string currentExecutable = Process.GetCurrentProcess().MainModule?.FileName ?? "";
                if (!string.IsNullOrEmpty(currentExecutable))
                {
                    Process.Start(currentExecutable);
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"启动新实例失败: {ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void TrayIcon_TrayMiddleMouseUp(object sender, RoutedEventArgs e)
        {
            // 鼠标中键点击关闭实例
            Application.Current.Shutdown();
        }

        private void RestartInstance_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                // 启动新的应用程序实例
                string currentExecutable = Process.GetCurrentProcess().MainModule?.FileName ?? "";
                if (!string.IsNullOrEmpty(currentExecutable))
                {
                    Process.Start(currentExecutable);
                }
                
                // 关闭当前实例
                Application.Current.Shutdown();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"重启实例失败: {ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void CloseInstance_Click(object sender, RoutedEventArgs e)
        {
            // 关闭当前实例
            Application.Current.Shutdown();
        }

        private void SuperMinimalMode_Click(object sender, RoutedEventArgs e)
        {
            if (sender is MenuItem menuItem && mainWindow != null)
            {
                // 切换超级极简模式状态
                mainWindow.ToggleSuperMinimalMode(menuItem.IsChecked);
                
                // 同步菜单项状态到主窗口的超级极简模式状态
                UpdateSuperMinimalModeMenuState();
            }
        }
        
        private void UpdateSuperMinimalModeMenuState()
        {
            if (trayIcon?.ContextMenu != null && mainWindow != null)
            {
                var menuItem = trayIcon.ContextMenu.Items.OfType<MenuItem>()
                    .FirstOrDefault(item => item.Name == "SuperMinimalModeMenuItem");
                if (menuItem != null)
                {
                    menuItem.IsChecked = mainWindow.IsSuperMinimalMode;
                }
            }
        }

        private void OnMainWindowTitleChanged(string title)
        {
            if (trayIcon != null)
            {
                // 更新托盘图标的提示文本，只显示当前网页标题
                trayIcon.ToolTipText = title;
            }
        }

        protected override void OnExit(ExitEventArgs e)
        {
            // 取消订阅事件
            if (mainWindow != null)
            {
                mainWindow.TitleChanged -= OnMainWindowTitleChanged;
            }
            
            trayIcon?.Dispose();
            base.OnExit(e);
        }

        private void AddBookmarkFromTray_Click(object sender, RoutedEventArgs e)
        {
            if (mainWindow?.webView?.CoreWebView2 != null)
            {
                string currentUrl = mainWindow.webView.CoreWebView2.Source;
                string currentTitle = mainWindow.webView.CoreWebView2.DocumentTitle;

                if (!string.IsNullOrEmpty(currentUrl) && !bookmarks.Any(b => b.Url.Equals(currentUrl, StringComparison.OrdinalIgnoreCase)))
                {
                    var newBookmark = new Bookmark
                    {
                        Title = !string.IsNullOrEmpty(currentTitle) ? currentTitle : currentUrl,
                        Url = currentUrl
                    };

                    bookmarks.Add(newBookmark);
                    SaveBookmarks();
                    RefreshTrayBookmarkMenu();
                    
                    MessageBox.Show($"已添加到收藏夹: {newBookmark.Title}", "收藏夹", MessageBoxButton.OK, MessageBoxImage.Information);
                }
                else
                {
                    MessageBox.Show("该页面已在收藏夹中或无效", "收藏夹", MessageBoxButton.OK, MessageBoxImage.Warning);
                }
            }
        }

        private void EditBookmarkFromTray_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (!File.Exists(bookmarksFilePath))
                {
                    SaveBookmarks(); // 创建文件
                }

                string configFolder = Path.GetDirectoryName(bookmarksFilePath);
                Process.Start(new ProcessStartInfo
                {
                    FileName = "explorer.exe",
                    Arguments = configFolder,
                    UseShellExecute = true
                });
            }
            catch (Exception ex)
            {
                MessageBox.Show($"打开收藏夹文件夹失败: {ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
            }
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
                    // 创建默认收藏夹
                    bookmarks = new List<Bookmark>
                    {
                        new Bookmark { Title = "Google", Url = "https://www.google.com" },
                        new Bookmark { Title = "GitHub", Url = "https://github.com" },
                        new Bookmark { Title = "Stack Overflow", Url = "https://stackoverflow.com" }
                    };
                    SaveBookmarks();
                }

                RefreshTrayBookmarkMenu();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"加载收藏夹失败: {ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void SaveBookmarks()
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(bookmarksFilePath));
                var json = JsonSerializer.Serialize(bookmarks, new JsonSerializerOptions { WriteIndented = true });
                File.WriteAllText(bookmarksFilePath, json);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"保存收藏夹失败: {ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void RefreshTrayBookmarkMenu()
        {
            if (trayIcon?.ContextMenu != null)
            {
                var bookmarksMenuItem = trayIcon.ContextMenu.Items.OfType<MenuItem>()
                    .FirstOrDefault(item => item.Name == "BookmarksMenuItem");

                if (bookmarksMenuItem != null)
                {
                    // 清除现有的收藏夹菜单项（保留"添加到收藏夹"、"编辑收藏夹"和分隔符）
                    var itemsToRemove = bookmarksMenuItem.Items.Cast<object>().Skip(3).ToList();
                    foreach (var item in itemsToRemove)
                    {
                        bookmarksMenuItem.Items.Remove(item);
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
                            if (bookmarkItem.Tag != null && mainWindow?.webView?.CoreWebView2 != null)
                            {
                                mainWindow.webView.CoreWebView2.Navigate(bookmarkItem.Tag.ToString());
                                
                                // 显示主窗口
                                if (!mainWindow.IsVisible)
                                {
                                    mainWindow.Show();
                                    mainWindow.WindowState = WindowState.Normal;
                                    mainWindow.Activate();
                                }
                            }
                        };

                        bookmarksMenuItem.Items.Add(bookmarkItem);
                    }
                }
            }
        }
    }
}