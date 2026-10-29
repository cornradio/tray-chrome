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
using System.Windows.Input;
using System.Globalization;

namespace TrayChrome
{
    public partial class App : Application
    {
        private TaskbarIcon? trayIcon;
        private MainWindow? mainWindow;
        private BookmarkManagerWindow? bookmarkManagerWindow;
        private static int instanceCounter = 0;
        private int currentInstanceId;
        private List<Bookmark> bookmarks = new List<Bookmark>();
        private string bookmarksFilePath = BookmarkFolders.FilePath;
        
        // 全局快捷键管理器
        private GlobalHotKeyManager? hotKeyManager;
        private AppSettings appSettings = new AppSettings();
        private string settingsFilePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "settings.json");
        private FileSystemWatcher? settingsWatcher;
        private string currentIconPath = IconSettings.DefaultPath;
        public string CurrentIconPath => currentIconPath;
        private InstanceService? instanceService;
        private string? instanceHotkey;

        // 验证URL格式的辅助方法
        private bool IsValidUrl(string url)
        {
            return Uri.TryCreate(url, UriKind.Absolute, out Uri? result) 
                   && (result.Scheme == Uri.UriSchemeHttp || result.Scheme == Uri.UriSchemeHttps);
        }

        // 解析窗口大小的辅助方法
        private bool TryParseSize(string sizeValue, out double width, out double height)
        {
            width = 0;
            height = 0;
            
            if (string.IsNullOrEmpty(sizeValue))
                return false;
                
            string[] parts = sizeValue.Split('x', 'X', '*');
            if (parts.Length != 2)
                return false;
                
            if ((double.TryParse(parts[0], NumberStyles.Float, CultureInfo.InvariantCulture, out width)
                && double.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out height))
                || (double.TryParse(parts[0], out width) && double.TryParse(parts[1], out height)))
            {
                // 验证尺寸范围
                if (width >= 200 && width <= 3840 && height >= 150 && height <= 2160)
                {
                    return true;
                }
            }
            
            return false;
        }

        protected override void OnStartup(StartupEventArgs e)
        {
            WriteDiagnostic($"Startup; runtime={Environment.Version}; OS={Environment.OSVersion}; base={AppDomain.CurrentDomain.BaseDirectory}");
            base.OnStartup(e);

            // 分配实例ID
            currentInstanceId = ++instanceCounter;

            // 解析命令行参数
            string? startupUrl = null;
            bool shouldOpen = false; // 默认不直接显示窗口
            bool shouldUseCleanMode = false; // 默认不使用超级简洁模式
            bool shouldForceUncleanMode = false; // 是否强制禁用超级极简模式
            bool shouldShowHelp = false; // 是否显示帮助信息
            double? customWidth = null; // 自定义窗口宽度
            double? customHeight = null; // 自定义窗口高度
            string? startupIcon = null;
            string? startupAppGroup = null;
            var startupTabs = new List<string>();
            
            if (e.Args.Length > 0)
            {
                // 支持多种参数格式
                for (int i = 0; i < e.Args.Length; i++)
                {
                    string arg = e.Args[i];
                    if (arg.Equals("--tab", StringComparison.OrdinalIgnoreCase))
                    {
                        if (++i >= e.Args.Length || !Uri.TryCreate(e.Args[i], UriKind.Absolute, out var tabUri)
                            || (tabUri.Scheme != Uri.UriSchemeHttp && tabUri.Scheme != Uri.UriSchemeHttps))
                        {
                            MessageBox.Show("--tab 需要 HTTP 或 HTTPS 地址。", "启动失败", MessageBoxButton.OK, MessageBoxImage.Error);
                            Shutdown(1);
                            return;
                        }
                        startupTabs.Add(e.Args[i]);
                        continue;
                    }
                    if (arg.Equals("--instance-hotkey", StringComparison.OrdinalIgnoreCase))
                    {
                        if (i + 1 >= e.Args.Length)
                        {
                            MessageBox.Show("--instance-hotkey 需要快捷键参数。", "启动失败", MessageBoxButton.OK, MessageBoxImage.Error);
                            Shutdown(1);
                            return;
                        }
                        instanceHotkey = e.Args[++i];
                        try
                        {
                            if (!string.IsNullOrWhiteSpace(instanceHotkey))
                                instanceHotkey = GlobalHotKeyManager.ParseHotkey(instanceHotkey, out _, out _);
                        }
                        catch (ArgumentException ex)
                        {
                            MessageBox.Show(ex.Message, "快捷键无效", MessageBoxButton.OK, MessageBoxImage.Error);
                            Shutdown(1);
                            return;
                        }
                        continue;
                    }
                    if (arg.StartsWith("--app-group=", StringComparison.OrdinalIgnoreCase))
                    {
                        startupAppGroup = arg.Substring("--app-group=".Length);
                        continue;
                    }
                    if (arg.Equals("--app-group", StringComparison.OrdinalIgnoreCase))
                    {
                        if (i + 1 >= e.Args.Length || e.Args[i + 1].StartsWith("--", StringComparison.Ordinal))
                        {
                            MessageBox.Show("--app-group 需要指定 JSON 文件路径。", "启动失败", MessageBoxButton.OK, MessageBoxImage.Error);
                            Shutdown(1);
                            return;
                        }
                        startupAppGroup = e.Args[++i];
                        continue;
                    }
                    if (arg.Equals("--icon", StringComparison.OrdinalIgnoreCase) && i + 1 < e.Args.Length)
                    {
                        startupIcon = e.Args[++i];
                        continue;
                    }
                    
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
                    // 支持 --size=480x320 格式
                    else if (arg.StartsWith("--size=", StringComparison.OrdinalIgnoreCase))
                    {
                        string sizeValue = arg.Substring(7);
                        if (TryParseSize(sizeValue, out double width, out double height))
                        {
                            customWidth = width;
                            customHeight = height;
                        }
                    }
                    // 支持 --size 480x320 格式
                    else if (arg.Equals("--size", StringComparison.OrdinalIgnoreCase) && i + 1 < e.Args.Length)
                    {
                        string sizeValue = e.Args[i + 1];
                        if (TryParseSize(sizeValue, out double width, out double height))
                        {
                            customWidth = width;
                            customHeight = height;
                        }
                        i++; // 跳过下一个参数，因为已经作为size使用了
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

            if (startupAppGroup != null)
            {
                ShutdownMode = ShutdownMode.OnExplicitShutdown;
                Dispatcher.BeginInvoke(new Action(async () =>
                {
                    bool loaded = await LoadAppGroupAsync(startupAppGroup);
                    Shutdown(loaded ? 0 : 1);
                }));
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
            mainWindow = new MainWindow(startupUrl, shouldUseCleanMode, shouldForceUncleanMode, customWidth, customHeight, startupTabs);
            LoadIconSetting();
            if (startupIcon != null)
            {
                try { SetApplicationIcon(startupIcon); }
                catch (Exception ex)
                {
                    MessageBox.Show($"加载启动图标失败: {ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
                    Shutdown(1);
                    return;
                }
            }
            
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
            UpdateAdBlockMenuState();
            
            // 加载收藏夹并刷新托盘菜单
            LoadBookmarks();
            RefreshTrayBookmarkMenu();
            
            // 加载设置并初始化全局快捷键
            LoadSettings();
            InitializeGlobalHotKey();
            InitializeSettingsWatcher();
            instanceService = new InstanceService(CaptureCurrentApp,
                () => mainWindow.TabsReady, () => Shutdown());
            instanceService.Start();
            
            // 隐藏主窗口，只显示托盘图标
            MainWindow = mainWindow;
            MainWindow.ShowInTaskbar = false;
        }

        private void TrayIcon_TrayLeftMouseUp(object sender, RoutedEventArgs e)
        {
            WriteDiagnostic($"Tray left click; visible={mainWindow?.IsVisible}; state={mainWindow?.WindowState}");
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
            if (trayIcon?.ContextMenu != null) trayIcon.ContextMenu.IsOpen = false;
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
            // Ctrl + 中键：增加实例；普通中键：不做操作
            try
            {
                if ((Keyboard.Modifiers & ModifierKeys.Control) == ModifierKeys.Control)
                {
                    string currentExecutable = Process.GetCurrentProcess().MainModule?.FileName ?? "";
                    if (!string.IsNullOrEmpty(currentExecutable))
                    {
                        Process.Start(currentExecutable);
                    }
                }else{
                    Application.Current.Shutdown();
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"启动新实例失败: {ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void RestartInstance_Click(object sender, RoutedEventArgs e)
        {
            if (trayIcon?.ContextMenu != null) trayIcon.ContextMenu.IsOpen = false;
            try
            {
                RestartCurrentInstance();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"重启实例失败: {ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void CloseInstance_Click(object sender, RoutedEventArgs e)
        {
            if (trayIcon?.ContextMenu != null) trayIcon.ContextMenu.IsOpen = false;
            // 关闭当前实例
            Application.Current.Shutdown();
        }

        private void ResetWindowSizeAndPosition_Click(object sender, RoutedEventArgs e)
        {
            mainWindow?.ResetWindowSizeAndPosition();
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

        private void AdBlock_Click(object sender, RoutedEventArgs e)
        {
            if (sender is MenuItem menuItem && mainWindow != null)
            {
                // 切换广告拦截
                mainWindow.ToggleAdBlock(menuItem.IsChecked);
                
                UpdateAdBlockMenuState();
            }
        }

        private void AdBlockSettings_Click(object sender, RoutedEventArgs e)
        {
            if (mainWindow != null)
            {
                mainWindow.ShowAdBlockSettings();
                UpdateAdBlockMenuState();
            }
        }

        private void OpenSettings_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (mainWindow != null)
                {
                    // 确保主窗口已经显示过（即使现在是隐藏的），这样才能设置Owner
                    if (!mainWindow.IsLoaded)
                    {
                        mainWindow.Show();
                        mainWindow.Hide();
                    }
                    
                    var settingsWindow = new SettingsWindow(appSettings, mainWindow, this);
                    // 订阅窗口关闭事件，在关闭时更新设置
                    settingsWindow.Closed += (s, args) =>
                    {
                        // 对于非模态窗口，使用SettingsSaved属性来判断是否保存了设置
                        if (settingsWindow.SettingsSaved)
                        {
                            // 设置已保存，重新加载全局快捷键
                            ReloadGlobalHotKey();
                            // 更新菜单状态
                            UpdateSuperMinimalModeMenuState();
                            UpdateAnimationMenuState();
                            UpdateAdBlockMenuState();
                        }
                    };
                    // 使用非模态窗口，不阻塞主线程
                    settingsWindow.Show();
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"打开设置窗口失败: {ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        public BookmarkManagerWindow OpenBookmarkManager()
        {
            if (bookmarkManagerWindow == null)
            {
                var manager = new BookmarkManagerWindow(this, mainWindow);
                bookmarkManagerWindow = manager;
                manager.Closed += (_, _) => bookmarkManagerWindow = null;
            }
            bookmarkManagerWindow.ActivateManager();
            return bookmarkManagerWindow;
        }

        public void OnBookmarksUpdated(object? sender, EventArgs e)
        {
            // 重新加载收藏夹并刷新托盘菜单 (LoadBookmarks 内部会调用 RefreshTrayBookmarkMenu)
            LoadBookmarks();
            // 同时更新主窗口的收藏夹菜单
            mainWindow?.LoadBookmarks();
            if (!ReferenceEquals(sender, bookmarkManagerWindow))
                bookmarkManagerWindow?.ReloadBookmarks();
        }
        
        private void UpdateAdBlockMenuState()
        {
            if (trayIcon?.ContextMenu != null && mainWindow != null)
            {
                var menuItem = trayIcon.ContextMenu.Items.OfType<MenuItem>()
                    .FirstOrDefault(item => item.Name == "AdBlockMenuItem");
                if (menuItem != null)
                {
                    menuItem.IsChecked = mainWindow.IsAdBlockEnabled;
                }
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

        public void SetIcon_Click(object? sender, RoutedEventArgs? e)
        {
            string? iconType = null;
            if (sender is MenuItem menuItem && menuItem.Tag is string tag)
            {
                iconType = tag;
            }
            else if (sender is RadioButton radioButton && radioButton.Tag is string radioTag)
            {
                iconType = radioTag;
            }
            
            if (!string.IsNullOrEmpty(iconType))
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

        private async void SetCustomIcon_Click(object sender, RoutedEventArgs e)
        {
            if (isSelectingCustomIcon) return;
            isSelectingCustomIcon = true;
            try
            {
                WriteDiagnostic("Custom icon: closing tray context menu");
                if (trayIcon?.ContextMenu != null) trayIcon.ContextMenu.IsOpen = false;
                // 等托盘菜单的原生消息回调返回，再进入文件选择框的模态消息循环。
                await System.Windows.Threading.Dispatcher.Yield(System.Windows.Threading.DispatcherPriority.ApplicationIdle);
                string targetPath = IconSettings.SelectAndImport(mainWindow?.IsVisible == true ? mainWindow : null);
                if (targetPath != null)
                {
                    WriteDiagnostic("Custom icon: applying imported image");
                    SetApplicationIcon(targetPath);
                    SaveIconSetting("custom", targetPath);
                    WriteDiagnostic("Custom icon: saved");
                    
                    MessageBox.Show("自定义图标设置成功！", "成功", MessageBoxButton.OK, MessageBoxImage.Information);
                }
            }
            catch (Exception ex)
            {
                WriteDiagnostic("Set custom icon failed", ex);
                MessageBox.Show($"设置自定义图标失败: {ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally { isSelectingCustomIcon = false; }
        }

        private bool isSelectingCustomIcon;

        public void SetApplicationIcon(string iconPath)
        {
            IconSettings.ValidatePath(iconPath);
            var bitmapImage = new System.Windows.Media.Imaging.BitmapImage();
            bitmapImage.BeginInit();
            bitmapImage.UriSource = new Uri(iconPath, UriKind.Absolute);
            bitmapImage.CacheOption = System.Windows.Media.Imaging.BitmapCacheOption.OnLoad;
            bitmapImage.EndInit();

            System.Drawing.Icon newIcon;
            using var stream = iconPath.StartsWith("pack://", StringComparison.Ordinal)
                ? Application.GetResourceStream(new Uri(iconPath)).Stream
                : File.OpenRead(iconPath);
            if (Path.GetExtension(iconPath).Equals(".ico", StringComparison.OrdinalIgnoreCase))
            {
                using var loaded = new System.Drawing.Icon(stream);
                newIcon = (System.Drawing.Icon)loaded.Clone();
            }
            else
            {
                using var bitmap = new System.Drawing.Bitmap(stream);
                var handle = bitmap.GetHicon();
                try
                {
                    using var borrowedIcon = System.Drawing.Icon.FromHandle(handle);
                    newIcon = (System.Drawing.Icon)borrowedIcon.Clone();
                }
                finally { DestroyIcon(handle); }
            }
            if (trayIcon != null)
            {
                var previous = trayIcon.Icon;
                trayIcon.Icon = newIcon;
                previous?.Dispose();
            }
            else newIcon.Dispose();
            if (mainWindow != null) mainWindow.Icon = bitmapImage;
            currentIconPath = iconPath;
        }

        [System.Runtime.InteropServices.DllImport("user32.dll")]
        private static extern bool DestroyIcon(IntPtr handle);

        public void SaveIconSetting(string iconType, string iconPath)
        {
            new IconSettings { IconType = iconType, IconPath = iconPath }.Save();
        }

        private void LoadIconSetting()
        {
            try
            {
                SetApplicationIcon(IconSettings.Load().IconPath);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"加载图标设置失败: {ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Warning);
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
            WriteDiagnostic($"Exit; code={e.ApplicationExitCode}");
            // 取消订阅事件
            if (mainWindow != null)
            {
                mainWindow.TitleChanged -= OnMainWindowTitleChanged;
            }
            
            // 清理全局快捷键资源
            hotKeyManager?.Dispose();
            instanceService?.Dispose();
            
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
            OpenBookmarkManager();
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

        private void OpenGithub_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = "https://github.com/cornradio/tray-chrome",
                    UseShellExecute = true
                });
            }
            catch (Exception ex)
            {
                MessageBox.Show($"打开 GitHub 链接失败: {ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
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

        private string CreateDesktopShortcut(string url, string title, string? arguments = null)
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
                 
                 // 获取当前窗口大小
                 string sizeArgument = "";
                 if (mainWindow != null)
                 {
                     // 获取当前窗口的实际大小（四舍五入到整数）
                     int currentWidth = (int)Math.Round(mainWindow.ActualWidth);
                     int currentHeight = (int)Math.Round(mainWindow.ActualHeight);
                     
                     // 确保尺寸在有效范围内
                     if (currentWidth >= 200 && currentWidth <= 3840 && 
                         currentHeight >= 150 && currentHeight <= 2160)
                     {
                         sizeArgument = $" --size {currentWidth}x{currentHeight}";
                     }
                 }
                 
                 // 使用PowerShell创建Windows快捷方式
                 string psScript = $@"
$ErrorActionPreference = 'Stop'
$WshShell = New-Object -comObject WScript.Shell
$Shortcut = $WshShell.CreateShortcut('{shortcutPath.Replace("'", "''")}')
$Shortcut.TargetPath = '{appPath.Replace("'", "''")}'
$Shortcut.Arguments = '{(arguments ?? $"--url \"{url}\" --open{sizeArgument}").Replace("'", "''")}'
$Shortcut.Description = 'TrayChrome - {title.Replace("'", "''")}'
$Shortcut.IconLocation = '{appPath.Replace("'", "''")}' + ',0'
$Shortcut.WorkingDirectory = '{Path.GetDirectoryName(appPath)?.Replace("'", "''")}'
$Shortcut.Save()
";
                 
                 ProcessStartInfo psi = new ProcessStartInfo
                 {
                     FileName = "powershell.exe",
                     Arguments = "-NoProfile -NonInteractive -EncodedCommand "
                         + Convert.ToBase64String(System.Text.Encoding.Unicode.GetBytes(psScript)),
                     UseShellExecute = false,
                     CreateNoWindow = true,
                     RedirectStandardOutput = true,
                     RedirectStandardError = true
                 };
                 
                 using (Process process = Process.Start(psi))
                 {
                     if (process == null) throw new InvalidOperationException("无法启动快捷方式创建进程。");
                     var output = process.StandardOutput.ReadToEndAsync();
                     var error = process.StandardError.ReadToEndAsync();
                     process.WaitForExit();
                     Task.WaitAll(output, error);
                     if (process.ExitCode != 0)
                     {
                         throw new Exception($"PowerShell执行失败: {error.Result}");
                     }
                 }
                 if (!File.Exists(shortcutPath)) throw new IOException("快捷方式文件未创建。");
                 return shortcutPath;
             }
             catch (Exception ex)
             {
                 throw new Exception($"创建快捷方式文件失败: {ex.Message}", ex);
             }
         }

        public void LoadBookmarks()
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(bookmarksFilePath));

                if (File.Exists(bookmarksFilePath))
                {
                    bookmarks = BookmarkFolders.Load(bookmarksFilePath);
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
                BookmarkFolders.Save(bookmarksFilePath, bookmarks);
                OnBookmarksUpdated(this, EventArgs.Empty);
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
                    void Populate(ItemsControl parent, List<Bookmark> entries)
                    {
                    foreach (var bookmark in entries)
                    {
                        if (bookmark.IsFolder)
                        {
                            var folderMenu = new MenuItem { Header = bookmark.Title };
                            Populate(folderMenu, bookmark.Children);
                            if (folderMenu.Items.Count == 0) folderMenu.Items.Add(new MenuItem { Header = "空文件夹", IsEnabled = false });
                            parent.Items.Add(folderMenu);
                            continue;
                        }
                        MenuItem bookmarkItem = new MenuItem
                        {
                            Header = bookmark.Title,
                            Tag = bookmark.Url,
                            ToolTip = bookmark.Url
                        };

                        // 左键点击导航
                        bookmarkItem.Click += (s, args) => {
                            args.Handled = true;
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
                                    entries.Remove(bookmark);
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
                                entries.Remove(bookmark);
                                SaveBookmarks();
                                RefreshTrayBookmarkMenu();
                                // 同时更新主窗口的收藏夹菜单
                                mainWindow?.RefreshBookmarkMenu();
                            }
                        };
                        
                        itemContextMenu.Items.Add(editItem);
                        itemContextMenu.Items.Add(deleteItem);
                        bookmarkItem.ContextMenu = itemContextMenu;

                        parent.Items.Add(bookmarkItem);
                    }
                    }
                    Populate(bookmarksMenuItem, bookmarks);
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
                       
  --size <宽度x高度>    指定窗口大小
                       格式: --size 480x320
                       或: --size=800x600
                       支持的分隔符: x, X, *
                       尺寸范围: 宽度200-3840，高度150-2160

  --icon <路径>        指定当前实例的 PNG/ICO 图标，不修改默认图标

  --app-group <路径>   打开指定 JSON 文件中的全部 App，保留原有实例
                       仅作为组启动器，不额外创建空白实例

  --instance-hotkey <组合>  为组内实例指定独立显示/隐藏快捷键

  --help, -h           显示此帮助信息

示例:
  TrayChrome.exe
  TrayChrome.exe --url https://www.baidu.com
  TrayChrome.exe --url https://www.google.com --open
  TrayChrome.exe --url https://jandan.net --open --clean
  TrayChrome.exe --unclean
  TrayChrome.exe https://github.com
  TrayChrome.exe --size 480x320 --url https://www.baidu.com --open

功能说明:
  • 左键点击托盘图标：显示/隐藏窗口
  • 中键点击托盘图标：关闭当前实例
  • 右键点击托盘图标：显示菜单
  • 支持多实例运行
  • 自动保存窗口位置和大小
  • 支持收藏夹功能
  • 支持暗色模式切换
  • 支持窗口置顶功能（蓝色置顶，灰色未置顶）
  • 极简模式下 Alt + 左键拖动移动窗口
  • Ctrl + Shift + L：聚焦地址栏
  • Ctrl + Shift + 点击链接：用默认浏览器打开
  • 设置 > App 组：保存全部实例、导入和切换多组 JSON 文件";

            MessageBox.Show(helpText, "TrayChrome 帮助", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        private void EditTrayBookmark(Bookmark bookmark)
        {
            OpenBookmarkManager().EditBookmarkFromMenu(bookmark, bookmarks);
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
                        loadedSettings.NormalizeWindowSize();
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
            if (instanceHotkey != null && hotKeyManager != null) return;
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
            if (hotKeyManager != null) return;
            bool enabled = instanceHotkey != null
                ? !string.IsNullOrWhiteSpace(instanceHotkey) : appSettings.EnableGlobalHotKey;
            if (enabled && mainWindow != null)
            {
                try
                {
                    uint modifiers = appSettings.HotKeyModifiers;
                    uint key = appSettings.HotKeyVirtualKey;
                    if (instanceHotkey != null)
                        GlobalHotKeyManager.ParseHotkey(instanceHotkey, out modifiers, out key);
                    hotKeyManager = new GlobalHotKeyManager(mainWindow);
                    if (!hotKeyManager.RegisterHotKey(modifiers, key, ToggleMainWindow))
                        WriteDiagnostic($"Hotkey '{instanceHotkey ?? appSettings.Hotkey}' is already registered; skipping binding for this instance. Tray controls remain available.");
                }
                catch (Exception ex)
                {
                    WriteDiagnostic("Register global hotkey failed", ex);
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