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

        // 验证URL格式的辅助方法
        private bool IsValidUrl(string url)
        {
            return Uri.TryCreate(url, UriKind.Absolute, out Uri? result) 
                   && (result.Scheme == Uri.UriSchemeHttp || result.Scheme == Uri.UriSchemeHttps);
        }

        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);

            // 分配实例ID
            currentInstanceId = ++instanceCounter;

            // 解析命令行参数
            string? startupUrl = null;
            bool shouldOpen = false; // 默认不直接显示窗口
            bool shouldUseCleanMode = false; // 默认不使用超级简洁模式
            bool shouldForceUncleanMode = false; // 是否强制禁用超级极简模式
            bool shouldShowHelp = false; // 是否显示帮助信息
            
            if (e.Args.Length > 0)
            {
                // 支持多种参数格式
                for (int i = 0; i < e.Args.Length; i++)
                {
                    string arg = e.Args[i];
                    
                    // 支持 --url=https://example.com 格式
                    if (arg.StartsWith("--url=", StringComparison.OrdinalIgnoreCase))
                    {
                        startupUrl = arg.Substring(6);
                    }
                    // 支持 --url https://example.com 格式
                    else if (arg.Equals("--url", StringComparison.OrdinalIgnoreCase) && i + 1 < e.Args.Length)
                    {
                        startupUrl = e.Args[i + 1];
                        i++; // 跳过下一个参数，因为已经作为URL使用了
                    }
                    // 支持 --open 格式
                    else if (arg.Equals("--open", StringComparison.OrdinalIgnoreCase))
                    {
                        shouldOpen = true;
                    }
                    // 支持 --clean 格式（启用超级极简模式）
                    else if (arg.Equals("--clean", StringComparison.OrdinalIgnoreCase))
                    {
                        shouldUseCleanMode = true;
                    }
                    // 支持 --unclean 格式（强制禁用超级极简模式）
                    else if (arg.Equals("--unclean", StringComparison.OrdinalIgnoreCase))
                    {
                        shouldForceUncleanMode = true;
                    }
                    // 支持 --help 格式
                    else if (arg.Equals("--help", StringComparison.OrdinalIgnoreCase) || arg.Equals("-h", StringComparison.OrdinalIgnoreCase))
                    {
                        shouldShowHelp = true;
                    }
                    // 支持直接传入URL（如果看起来像URL）
                    else if (IsValidUrl(arg))
                    {
                        startupUrl = arg;
                    }
                }
            }

            // 如果需要显示帮助信息
            if (shouldShowHelp)
            {
                ShowHelpMessage();
                Shutdown();
                return;
            }

            // 创建托盘图标
            trayIcon = (TaskbarIcon)FindResource("TrayIcon");
            
            // 更新托盘图标标识
            if (trayIcon != null)
            {
                trayIcon.ToolTipText = $"Tray Chrome Browser - 实例 {currentInstanceId}";
            }
            
            // 创建主窗口，传入启动参数
            mainWindow = new MainWindow(startupUrl, shouldUseCleanMode, shouldForceUncleanMode);
            
            // 根据 --open 参数决定是否显示窗口
            if (shouldOpen)
            {
                // 模拟托盘图标点击事件的逻辑
                mainWindow.ShowWithAnimation();
                mainWindow.WindowState = WindowState.Normal;
                mainWindow.Activate();
            }
            else
            {
                mainWindow.Hide();
            }
            
            // 订阅标题变化事件
            mainWindow.TitleChanged += OnMainWindowTitleChanged;
            
            // 更新托盘菜单中超级极简模式的状态
            UpdateSuperMinimalModeMenuState();
            UpdateAnimationMenuState();
            
            // 加载收藏夹并刷新托盘菜单
            LoadBookmarks();
            RefreshTrayBookmarkMenu();
            
            // 隐藏主窗口，只显示托盘图标
            MainWindow = mainWindow;
            if (!shouldOpen)
            {
                MainWindow.WindowState = WindowState.Minimized;
            }
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

        private void Animation_Click(object sender, RoutedEventArgs e)
        {
            if (sender is MenuItem menuItem && mainWindow != null)
            {
                // 切换动画设置
                mainWindow.ToggleAnimation(menuItem.IsChecked);
                
                UpdateAnimationMenuState();
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

        private void UpdateAnimationMenuState()
        {
            if (trayIcon?.ContextMenu != null && mainWindow != null)
            {
                var menuItem = trayIcon.ContextMenu.Items.OfType<MenuItem>()
                    .FirstOrDefault(item => item.Name == "AnimationMenuItem");
                if (menuItem != null)
                {
                    menuItem.IsChecked = mainWindow.IsAnimationEnabled;
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

                        // 左键点击导航
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
                                    RefreshTrayBookmarkMenu();
                                    // 同时更新主窗口的收藏夹菜单
                                    mainWindow?.RefreshBookmarkMenu();
                                }
                                args.Handled = true;
                            }
                        };

                        // 添加右键上下文菜单
                        ContextMenu itemContextMenu = new ContextMenu();
                        
                        MenuItem editItem = new MenuItem { Header = "编辑" };
                        editItem.Click += (s, args) => EditTrayBookmark(bookmark);
                        
                        MenuItem deleteItem = new MenuItem { Header = "删除" };
                        deleteItem.Click += (s, args) => {
                            var result = MessageBox.Show($"确定要删除收藏夹 \"{bookmark.Title}\" 吗？", 
                                "删除收藏夹", MessageBoxButton.YesNo, MessageBoxImage.Question);
                            
                            if (result == MessageBoxResult.Yes)
                            {
                                bookmarks.Remove(bookmark);
                                SaveBookmarks();
                                RefreshTrayBookmarkMenu();
                                // 同时更新主窗口的收藏夹菜单
                                mainWindow?.RefreshBookmarkMenu();
                            }
                        };
                        
                        itemContextMenu.Items.Add(editItem);
                        itemContextMenu.Items.Add(deleteItem);
                        bookmarkItem.ContextMenu = itemContextMenu;

                        bookmarksMenuItem.Items.Add(bookmarkItem);
                    }
                }
            }
        }

        // 显示帮助信息
        private void ShowHelpMessage()
        {
            string helpText = @"TrayChrome - 托盘浏览器

用法: TrayChrome.exe [选项] [URL]

选项:
  --url <URL>          指定启动时要打开的网址
                       格式: --url https://example.com
                       或: --url=https://example.com

  --open               启动时直接显示窗口（默认最小化到托盘）
                       
  --clean              启用超级极简模式（隐藏底部工具栏）
                       注意：此设置会被保存，下次启动时仍然生效
                       
  --unclean            强制禁用超级极简模式（显示底部工具栏）
                       用于覆盖之前保存的超级极简模式设置
                       
  --help, -h           显示此帮助信息

示例:
  TrayChrome.exe
  TrayChrome.exe --url https://www.baidu.com
  TrayChrome.exe --url https://www.google.com --open
  TrayChrome.exe --url https://jandan.net --open --clean
  TrayChrome.exe --unclean
  TrayChrome.exe https://github.com

功能说明:
  • 左键点击托盘图标：显示/隐藏窗口
  • 中键点击托盘图标：关闭当前实例
  • 右键点击托盘图标：显示菜单
  • 支持多实例运行
  • 自动保存窗口位置和大小
  • 支持收藏夹功能
  • 支持暗色模式切换
  • 支持窗口置顶功能";

            MessageBox.Show(helpText, "TrayChrome 帮助", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        private void EditTrayBookmark(Bookmark bookmark)
        {
            // 创建编辑对话框
            var dialog = new Window
            {
                Title = "编辑收藏夹",
                Width = 400,
                Height = 200,
                WindowStartupLocation = WindowStartupLocation.CenterScreen,
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
                    RefreshTrayBookmarkMenu();
                    // 同时更新主窗口的收藏夹菜单
                    mainWindow?.RefreshBookmarkMenu();
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
    }
}