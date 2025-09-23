using System;
using System.Diagnostics;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using Hardcodet.Wpf.TaskbarNotification;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;

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
        
        // 全局快捷键管理器
        private GlobalHotKeyManager? hotKeyManager;
        private AppSettings appSettings = new AppSettings();
        private string settingsFilePath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "TrayChrome", "settings.json");
        private FileSystemWatcher? settingsWatcher;

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
            
            // 加载设置并初始化全局快捷键
            LoadSettings();
            InitializeGlobalHotKey();
            InitializeSettingsWatcher();
            
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

        private void SetIcon_Click(object sender, RoutedEventArgs e)
        {
            if (sender is MenuItem menuItem && menuItem.Tag is string iconType)
            {
                try
                {
                    string iconPath;
                    switch (iconType)
                    {
                        case "default":
                            iconPath = "pack://application:,,,/Resources/Ampeross-Ampola-Chrome.ico";
                            break;
                        case "dingding":
                            iconPath = "pack://application:,,,/Resources/alternative-icons/dingding.ico";
                            break;
                        case "feishu":
                            iconPath = "pack://application:,,,/Resources/alternative-icons/feishu.ico";
                            break;
                        case "wecom":
                            iconPath = "pack://application:,,,/Resources/alternative-icons/wecom.ico";
                            break;
                        case "weixin":
                            iconPath = "pack://application:,,,/Resources/alternative-icons/weixin.ico";
                            break;
                        default:
                            return;
                    }

                    SetApplicationIcon(iconPath);
                    SaveIconSetting(iconType, iconPath);
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"设置图标失败: {ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
        }

        private void SetCustomIcon_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                // 创建文件选择对话框
                var openFileDialog = new Microsoft.Win32.OpenFileDialog();
                
                // 设置对话框属性
                openFileDialog.Title = "选择图标文件";
                openFileDialog.Filter = "图标文件 (*.ico)|*.ico|PNG图片 (*.png)|*.png|所有文件 (*.*)|*.*";
                openFileDialog.FilterIndex = 1;
                openFileDialog.CheckFileExists = true;
                openFileDialog.CheckPathExists = true;
                openFileDialog.Multiselect = false;
                openFileDialog.RestoreDirectory = true;
                
                // 设置初始目录为用户的桌面
                openFileDialog.InitialDirectory = Environment.GetFolderPath(Environment.SpecialFolder.Desktop);

                // 显示对话框并获取结果
                Window owner = null;
                if (mainWindow != null && mainWindow.IsVisible)
                {
                    owner = mainWindow;
                }
                else
                {
                    // 如果主窗口不可见，尝试获取当前活动窗口
                    owner = Application.Current.MainWindow;
                }
                
                bool? dialogResult;
                if (owner != null)
                {
                    dialogResult = openFileDialog.ShowDialog(owner);
                }
                else
                {
                    dialogResult = openFileDialog.ShowDialog();
                }

                if (dialogResult == true && !string.IsNullOrEmpty(openFileDialog.FileName))
                {
                    string selectedPath = openFileDialog.FileName;
                    
                    // 验证文件是否存在和可读
                    if (!File.Exists(selectedPath))
                    {
                        MessageBox.Show("选择的文件不存在！", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
                        return;
                    }

                    // 验证文件大小（不超过5MB）
                    var fileInfo = new FileInfo(selectedPath);
                    if (fileInfo.Length > 5 * 1024 * 1024)
                    {
                        MessageBox.Show("图标文件过大，请选择小于5MB的文件！", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
                        return;
                    }

                    // 验证图标文件是否有效
                    if (!IsValidIconFile(selectedPath))
                    {
                        MessageBox.Show("选择的文件不是有效的图标文件！", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
                        return;
                    }
                    
                    // 复制图标到应用程序目录
                    string customIconsDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Resources", "custom-icons");
                    Directory.CreateDirectory(customIconsDir);
                    
                    string fileName = $"custom_{DateTime.Now:yyyyMMddHHmmss}{Path.GetExtension(selectedPath)}";
                    string targetPath = Path.Combine(customIconsDir, fileName);
                    
                    File.Copy(selectedPath, targetPath, true);
                    
                    SetApplicationIcon(targetPath);
                    SaveIconSetting("custom", targetPath);
                    
                    MessageBox.Show("自定义图标设置成功！", "成功", MessageBoxButton.OK, MessageBoxImage.Information);
                }
            }
            catch (Exception ex)
            {
                // 显示详细的错误信息用于调试
                string errorMessage = $"错误类型: {ex.GetType().Name}\n错误信息: {ex.Message}\n堆栈跟踪: {ex.StackTrace}";
                MessageBox.Show(errorMessage, "详细错误信息", MessageBoxButton.OK, MessageBoxImage.Error);
                
                // 同时输出到调试控制台
                System.Diagnostics.Debug.WriteLine($"SetCustomIcon_Click异常详情: {ex}");
            }
        }

        private bool IsValidIconFile(string filePath)
        {
            try
            {
                string extension = Path.GetExtension(filePath).ToLower();
                
                if (extension == ".ico")
                {
                    // 尝试创建Icon对象来验证ICO文件
                    using (var icon = new System.Drawing.Icon(filePath))
                    {
                        return icon.Width > 0 && icon.Height > 0;
                    }
                }
                else if (extension == ".png")
                {
                    // 尝试创建BitmapImage来验证PNG文件
                    var bitmap = new System.Windows.Media.Imaging.BitmapImage();
                    bitmap.BeginInit();
                    bitmap.UriSource = new Uri(filePath, UriKind.Absolute);
                    bitmap.EndInit();
                    return bitmap.PixelWidth > 0 && bitmap.PixelHeight > 0;
                }
                
                return false;
            }
            catch
            {
                return false;
            }
        }

        private void SetApplicationIcon(string iconPath)
        {
            try
            {
                // 设置托盘图标
                if (trayIcon != null)
                {
                    if (iconPath.StartsWith("pack://"))
                    {
                        // 使用资源图标
                        var iconUri = new Uri(iconPath);
                        var iconStream = Application.GetResourceStream(iconUri);
                        if (iconStream != null)
                        {
                            // 需要重新创建流，因为Icon构造函数会关闭流
                            var memoryStream = new MemoryStream();
                            iconStream.Stream.CopyTo(memoryStream);
                            memoryStream.Position = 0;
                            
                            // 释放旧图标
                            trayIcon.Icon?.Dispose();
                            trayIcon.Icon = new System.Drawing.Icon(memoryStream);
                        }
                    }
                    else if (File.Exists(iconPath))
                    {
                        // 使用文件图标
                        string extension = Path.GetExtension(iconPath).ToLower();
                        
                        if (extension == ".ico")
                        {
                            // 释放旧图标
                            trayIcon.Icon?.Dispose();
                            trayIcon.Icon = new System.Drawing.Icon(iconPath);
                        }
                        else if (extension == ".png")
                        {
                            // 将PNG转换为Icon
                            using (var bitmap = new System.Drawing.Bitmap(iconPath))
                            {
                                var hIcon = bitmap.GetHicon();
                                // 释放旧图标
                                trayIcon.Icon?.Dispose();
                                trayIcon.Icon = System.Drawing.Icon.FromHandle(hIcon);
                            }
                        }
                    }
                }

                // 设置窗口图标
                if (mainWindow != null)
                {
                    if (iconPath.StartsWith("pack://"))
                    {
                        var iconUri = new Uri(iconPath);
                        var bitmapImage = new System.Windows.Media.Imaging.BitmapImage();
                        bitmapImage.BeginInit();
                        bitmapImage.UriSource = iconUri;
                        bitmapImage.EndInit();
                        mainWindow.Icon = bitmapImage;
                    }
                    else if (File.Exists(iconPath))
                    {
                        var iconUri = new Uri(iconPath, UriKind.Absolute);
                        var bitmapImage = new System.Windows.Media.Imaging.BitmapImage();
                        bitmapImage.BeginInit();
                        bitmapImage.UriSource = iconUri;
                        bitmapImage.CacheOption = System.Windows.Media.Imaging.BitmapCacheOption.OnLoad;
                        bitmapImage.EndInit();
                        mainWindow.Icon = bitmapImage;
                    }
                }
            }
            catch (ArgumentException argEx)
            {
                MessageBox.Show($"图标文件格式不正确: {argEx.Message}", "格式错误", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            catch (OutOfMemoryException)
            {
                MessageBox.Show("内存不足，无法加载图标文件！", "内存错误", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"应用图标失败: {ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void SaveIconSetting(string iconType, string iconPath)
        {
            try
            {
                string settingsDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "TrayChrome");
                Directory.CreateDirectory(settingsDir);
                
                string settingsFile = Path.Combine(settingsDir, "icon-settings.json");
                
                var settings = new
                {
                    IconType = iconType,
                    IconPath = iconPath,
                    LastUpdated = DateTime.Now
                };
                
                string json = JsonSerializer.Serialize(settings, new JsonSerializerOptions { WriteIndented = true });
                File.WriteAllText(settingsFile, json);
            }
            catch (Exception ex)
            {
                // 静默处理保存错误，不影响图标设置
                System.Diagnostics.Debug.WriteLine($"保存图标设置失败: {ex.Message}");
            }
        }

        private void LoadIconSetting()
        {
            try
            {
                string settingsFile = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "TrayChrome", "icon-settings.json");
                
                if (File.Exists(settingsFile))
                {
                    string json = File.ReadAllText(settingsFile);
                    var settings = JsonSerializer.Deserialize<JsonElement>(json);
                    
                    if (settings.TryGetProperty("IconPath", out var iconPathElement))
                    {
                        string iconPath = iconPathElement.GetString() ?? "";
                        if (!string.IsNullOrEmpty(iconPath))
                        {
                            SetApplicationIcon(iconPath);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                // 静默处理加载错误，使用默认图标
                System.Diagnostics.Debug.WriteLine($"加载图标设置失败: {ex.Message}");
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
            
            // 清理全局快捷键资源
            hotKeyManager?.Dispose();
            
            // 清理文件监听器
            settingsWatcher?.Dispose();
            
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

        private void CreateDesktopShortcut_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                // 获取当前页面的URL
                string currentUrl = GetCurrentPageUrl();
                if (string.IsNullOrEmpty(currentUrl))
                {
                    MessageBox.Show("无法获取当前页面URL，请确保页面已加载完成。", "错误", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                // 获取当前页面的标题
                string pageTitle = GetCurrentPageTitle();
                if (string.IsNullOrEmpty(pageTitle))
                {
                    pageTitle = "网页快捷方式";
                }

                // 创建桌面快捷方式
                CreateDesktopShortcut(currentUrl, pageTitle);
                
                MessageBox.Show($"桌面快捷方式 \"{pageTitle}\" 创建成功！", "成功", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"创建桌面快捷方式失败: {ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private string GetCurrentPageUrl()
        {
            try
            {
                if (mainWindow?.webView?.CoreWebView2 != null)
                {
                    return mainWindow.webView.CoreWebView2.Source;
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"获取当前页面URL失败: {ex.Message}");
            }
            return string.Empty;
        }

        private string GetCurrentPageTitle()
        {
            try
            {
                if (mainWindow?.webView?.CoreWebView2 != null)
                {
                    return mainWindow.webView.CoreWebView2.DocumentTitle;
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"获取当前页面标题失败: {ex.Message}");
            }
            return string.Empty;
        }

        private void CreateDesktopShortcut(string url, string title)
         {
             try
             {
                 // 获取桌面路径
                 string desktopPath = Environment.GetFolderPath(Environment.SpecialFolder.Desktop);
                 
                 // 清理文件名中的非法字符
                 string fileName = title;
                 char[] invalidChars = Path.GetInvalidFileNameChars();
                 foreach (char c in invalidChars)
                 {
                     fileName = fileName.Replace(c, '_');
                 }
                 
                 // 限制文件名长度
                 if (fileName.Length > 50)
                 {
                     fileName = fileName.Substring(0, 50);
                 }
                 
                 string shortcutPath = Path.Combine(desktopPath, $"{fileName}.lnk");
                 
                 // 如果文件已存在，添加数字后缀
                 int counter = 1;
                 string originalPath = shortcutPath;
                 while (System.IO.File.Exists(shortcutPath))
                 {
                     string nameWithoutExt = Path.GetFileNameWithoutExtension(originalPath);
                     shortcutPath = Path.Combine(desktopPath, $"{nameWithoutExt}({counter}).lnk");
                     counter++;
                 }
                 
                 // 获取当前应用程序的路径
                 string appPath = Process.GetCurrentProcess().MainModule?.FileName ?? "";
                 if (string.IsNullOrEmpty(appPath))
                 {
                     throw new Exception("无法获取应用程序路径");
                 }
                 
                 // 使用PowerShell创建Windows快捷方式
                 string psScript = $@"
$WshShell = New-Object -comObject WScript.Shell
$Shortcut = $WshShell.CreateShortcut('{shortcutPath.Replace("'", "''")}')
$Shortcut.TargetPath = '{appPath.Replace("'", "''")}'
$Shortcut.Arguments = '--url ""{url}"" --open'
$Shortcut.Description = 'TrayChrome - {title.Replace("'", "''")}'
$Shortcut.IconLocation = '{appPath.Replace("'", "''")}' + ',0'
$Shortcut.WorkingDirectory = '{Path.GetDirectoryName(appPath)?.Replace("'", "''")}'
$Shortcut.Save()
";
                 
                 ProcessStartInfo psi = new ProcessStartInfo
                 {
                     FileName = "powershell.exe",
                     Arguments = $"-NoProfile -ExecutionPolicy Bypass -Command \"{psScript.Replace("\"", "\\\"")}\"",
                     UseShellExecute = false,
                     CreateNoWindow = true,
                     RedirectStandardOutput = true,
                     RedirectStandardError = true
                 };
                 
                 using (Process process = Process.Start(psi))
                 {
                     process?.WaitForExit();
                     if (process?.ExitCode != 0)
                     {
                         string error = process.StandardError.ReadToEnd();
                         throw new Exception($"PowerShell执行失败: {error}");
                     }
                 }
             }
             catch (Exception ex)
             {
                 throw new Exception($"创建快捷方式文件失败: {ex.Message}");
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

        // 加载设置
        private void LoadSettings()
        {
            try
            {
                if (File.Exists(settingsFilePath))
                {
                    string json = File.ReadAllText(settingsFilePath);
                    var loadedSettings = JsonSerializer.Deserialize<AppSettings>(json);
                    if (loadedSettings != null)
                    {
                        appSettings = loadedSettings;
                    }
                }
                else
                {
                    // 创建默认设置文件
                    SaveSettings();
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"加载设置失败: {ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        // 保存设置
        private void SaveSettings()
        {
            try
            {
                string directory = Path.GetDirectoryName(settingsFilePath);
                if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
                {
                    Directory.CreateDirectory(directory);
                }

                var options = new JsonSerializerOptions 
                { 
                    WriteIndented = true,
                    Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
                };
                string json = JsonSerializer.Serialize(appSettings, options);
                File.WriteAllText(settingsFilePath, json);
                
                // 重新注册全局快捷键
                ReloadGlobalHotKey();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"保存设置失败: {ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }
        
        // 重新加载全局快捷键
        private void ReloadGlobalHotKey()
        {
            try
            {
                // 先注销现有的快捷键
                hotKeyManager?.Dispose();
                hotKeyManager = null;
                
                // 重新初始化快捷键
                InitializeGlobalHotKey();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"重新加载全局快捷键失败: {ex.Message}");
            }
        }
         
         // 初始化配置文件监听器
         private void InitializeSettingsWatcher()
         {
             try
             {
                 string directory = Path.GetDirectoryName(settingsFilePath);
                 if (!string.IsNullOrEmpty(directory))
                 {
                     settingsWatcher = new FileSystemWatcher(directory, "settings.json");
                     settingsWatcher.Changed += OnSettingsFileChanged;
                     settingsWatcher.EnableRaisingEvents = true;
                 }
             }
             catch (Exception ex)
             {
                 System.Diagnostics.Debug.WriteLine($"初始化配置文件监听器失败: {ex.Message}");
             }
         }
         
         // 配置文件变化事件处理
         private async void OnSettingsFileChanged(object sender, FileSystemEventArgs e)
         {
             try
             {
                 // 延迟一下，确保文件写入完成
                 await Task.Delay(500);
                 
                 // 在UI线程中重新加载设置
                 Dispatcher.Invoke(() =>
                 {
                     try
                     {
                         LoadSettings();
                         ReloadGlobalHotKey();
                         System.Diagnostics.Debug.WriteLine("配置文件已重新加载");
                     }
                     catch (Exception ex)
                     {
                         System.Diagnostics.Debug.WriteLine($"重新加载配置失败: {ex.Message}");
                     }
                 });
             }
             catch (Exception ex)
             {
                 System.Diagnostics.Debug.WriteLine($"处理配置文件变化事件失败: {ex.Message}");
             }
         }

         // 初始化全局快捷键
        private void InitializeGlobalHotKey()
        {
            if (appSettings.EnableGlobalHotKey && mainWindow != null)
            {
                try
                {
                    hotKeyManager = new GlobalHotKeyManager(mainWindow);
                    hotKeyManager.RegisterHotKey(appSettings.HotKeyModifiers, appSettings.HotKeyVirtualKey, ToggleMainWindow);
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"注册全局快捷键失败: {ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Warning);
                }
            }
        }

        // 切换主窗口显示/隐藏
        private void ToggleMainWindow()
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
    }
}