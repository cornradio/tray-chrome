using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using Microsoft.Win32;

namespace TrayChrome
{
    public partial class SettingsWindow : Window
    {
        private AppSettings originalSettings;
        private AppSettings currentSettings;
        private MainWindow? mainWindow;
        private App? app;

        // 当前版本号
        private const string CurrentVersion = "3.0.1";

        // 标记设置是否已保存（用于非模态窗口）
        public bool SettingsSaved { get; private set; } = false;
        
        // 用于数据绑定的属性
        public double WindowWidth { get; set; }
        public double WindowHeight { get; set; }
        public bool IsTopMost { get; set; }
        public bool IsSuperMinimalMode { get; set; }
        public bool IsAnimationEnabled { get; set; }
        public bool IsDarkMode { get; set; }
        public double ZoomFactor { get; set; }
        public bool IsMobileUA { get; set; }
        public bool IsAdBlockEnabled { get; set; }
        public string AdBlockRulesText { get; set; } = string.Empty;
        public string AdAllowRulesText { get; set; } = string.Empty;
        public bool EnableGlobalHotKey { get; set; }
        public string Hotkey { get; set; } = string.Empty;
        public string SelectedIconType { get; set; } = "default";
        private string customIconPath = string.Empty;
        public bool IsProxyEnabled { get; set; }
        public string ProxyServer { get; set; } = string.Empty;
        public bool AutoZoomOutOnStartup { get; set; }
        public bool EnableRoundedCorners { get; set; }
        public double CornerRadiusPx { get; set; }

        public SettingsWindow(AppSettings settings, MainWindow? mainWindow = null, App? app = null)
        {
            InitializeComponent();

            // 显示当前版本
            VersionText.Text = CurrentVersion;

            this.mainWindow = mainWindow;
            this.app = app;
            this.originalSettings = settings;
            settings.NormalizeWindowSize();
            
            // 设置窗口所有者
            if (mainWindow != null)
            {
                this.Owner = mainWindow;
            }
            
            // 创建当前设置的副本
            currentSettings = new AppSettings
            {
                ZoomFactor = settings.ZoomFactor,
                IsMobileUA = settings.IsMobileUA,
                WindowWidth = settings.WindowWidth,
                WindowHeight = settings.WindowHeight,
                WindowLeft = settings.WindowLeft,
                WindowTop = settings.WindowTop,
                IsDarkMode = settings.IsDarkMode,
                IsTopMost = settings.IsTopMost,
                IsSuperMinimalMode = settings.IsSuperMinimalMode,
                IsAnimationEnabled = settings.IsAnimationEnabled,
                IsAdBlockEnabled = settings.IsAdBlockEnabled,
                AdBlockRules = new List<string>(settings.AdBlockRules ?? new List<string>()),
                AdAllowRules = new List<string>(settings.AdAllowRules ?? new List<string>()),
                Hotkey = settings.Hotkey,
                EnableGlobalHotKey = settings.EnableGlobalHotKey,
                IsProxyEnabled = settings.IsProxyEnabled,
                ProxyServer = settings.ProxyServer,
                AutoZoomOutOnStartup = settings.AutoZoomOutOnStartup,
                EnableRoundedCorners = settings.EnableRoundedCorners,
                CornerRadiusPx = settings.CornerRadiusPx
            };
            
            // 加载当前设置到UI
            LoadSettingsToUI();
            
            // 设置数据绑定
            SetupDataBinding();
            
            // 加载当前图标设置
            LoadIconSetting();
        }

        private void LoadSettingsToUI()
        {
            WindowWidth = currentSettings.WindowWidth;
            WindowHeight = currentSettings.WindowHeight;
            IsTopMost = currentSettings.IsTopMost;
            IsSuperMinimalMode = currentSettings.IsSuperMinimalMode;
            IsAnimationEnabled = currentSettings.IsAnimationEnabled;
            IsDarkMode = currentSettings.IsDarkMode;
            ZoomFactor = currentSettings.ZoomFactor;
            IsMobileUA = currentSettings.IsMobileUA;
            IsAdBlockEnabled = currentSettings.IsAdBlockEnabled;
            AdBlockRulesText = string.Join("\r\n", currentSettings.AdBlockRules ?? new List<string>());
            AdAllowRulesText = string.Join("\r\n", currentSettings.AdAllowRules ?? new List<string>());
            EnableGlobalHotKey = currentSettings.EnableGlobalHotKey;
            Hotkey = currentSettings.Hotkey;
            IsProxyEnabled = currentSettings.IsProxyEnabled;
            ProxyServer = currentSettings.ProxyServer;
            AutoZoomOutOnStartup = currentSettings.AutoZoomOutOnStartup;
            EnableRoundedCorners = currentSettings.EnableRoundedCorners;
            CornerRadiusPx = currentSettings.CornerRadiusPx;
        }

        private void SetupDataBinding()
        {
            // 窗口大小 - 使用事件处理
            WidthTextBox.Text = WindowWidth.ToString();
            HeightTextBox.Text = WindowHeight.ToString();
            WidthTextBox.TextChanged += (s, e) => { if (double.TryParse(WidthTextBox.Text, out double w)) WindowWidth = w; };
            HeightTextBox.TextChanged += (s, e) => { if (double.TryParse(HeightTextBox.Text, out double h)) WindowHeight = h; };
            
            // 窗口行为
            TopMostCheckBox.IsChecked = IsTopMost;
            TopMostCheckBox.Checked += (s, e) => IsTopMost = true;
            TopMostCheckBox.Unchecked += (s, e) => IsTopMost = false;
            
            SuperMinimalModeCheckBox.IsChecked = IsSuperMinimalMode;
            SuperMinimalModeCheckBox.Checked += (s, e) => IsSuperMinimalMode = true;
            SuperMinimalModeCheckBox.Unchecked += (s, e) => IsSuperMinimalMode = false;
            
            AnimationCheckBox.IsChecked = IsAnimationEnabled;
            AnimationCheckBox.Checked += (s, e) => IsAnimationEnabled = true;
            AnimationCheckBox.Unchecked += (s, e) => IsAnimationEnabled = false;
            
            // 外观
            DarkModeCheckBox.IsChecked = IsDarkMode;
            DarkModeCheckBox.Checked += (s, e) => IsDarkMode = true;
            DarkModeCheckBox.Unchecked += (s, e) => IsDarkMode = false;
            
            // 缩放
            ZoomSlider.Value = ZoomFactor;
            ZoomValueLabel.Content = $"{ZoomFactor:F1}x";
            ZoomSlider.ValueChanged += (s, e) => 
            { 
                ZoomFactor = ZoomSlider.Value; 
                ZoomValueLabel.Content = $"{ZoomFactor:F1}x"; 
            };
            
            // UA - 使用事件处理
            if (IsMobileUA)
            {
                MobileUARadio.IsChecked = true;
                DesktopUARadio.IsChecked = false;
            }
            else
            {
                MobileUARadio.IsChecked = false;
                DesktopUARadio.IsChecked = true;
            }
            MobileUARadio.Checked += (s, e) => IsMobileUA = true;
            DesktopUARadio.Checked += (s, e) => IsMobileUA = false;
            
            // 广告拦截
            AdBlockEnabledCheckBox.IsChecked = IsAdBlockEnabled;
            AdBlockEnabledCheckBox.Checked += (s, e) => IsAdBlockEnabled = true;
            AdBlockEnabledCheckBox.Unchecked += (s, e) => IsAdBlockEnabled = false;
            
            AdBlockRulesTextBox.Text = AdBlockRulesText;
            AdBlockRulesTextBox.TextChanged += (s, e) => AdBlockRulesText = AdBlockRulesTextBox.Text;
            
            AdAllowRulesTextBox.Text = AdAllowRulesText;
            AdAllowRulesTextBox.TextChanged += (s, e) => AdAllowRulesText = AdAllowRulesTextBox.Text;
            
            // 快捷键
            EnableHotKeyCheckBox.IsChecked = EnableGlobalHotKey;
            EnableHotKeyCheckBox.Checked += (s, e) => EnableGlobalHotKey = true;
            EnableHotKeyCheckBox.Unchecked += (s, e) => EnableGlobalHotKey = false;
            
            HotKeyTextBox.Text = Hotkey;
            HotKeyTextBox.TextChanged += (s, e) => Hotkey = HotKeyTextBox.Text;

            // 代理
            ProxyEnabledCheckBox.IsChecked = IsProxyEnabled;
            ProxyEnabledCheckBox.Checked += (s, e) => IsProxyEnabled = true;
            ProxyEnabledCheckBox.Unchecked += (s, e) => IsProxyEnabled = false;

            ProxyServerTextBox.Text = ProxyServer;
            ProxyServerTextBox.TextChanged += (s, e) => ProxyServer = ProxyServerTextBox.Text;

            // 启动缩放
            AutoZoomOutCheckBox.IsChecked = AutoZoomOutOnStartup;
            AutoZoomOutCheckBox.Checked += (s, e) => AutoZoomOutOnStartup = true;
            AutoZoomOutCheckBox.Unchecked += (s, e) => AutoZoomOutOnStartup = false;

            // 圆角设置
            RoundedCornersCheckBox.IsChecked = EnableRoundedCorners;
            RoundedCornersCheckBox.Checked += (s, e) => EnableRoundedCorners = true;
            RoundedCornersCheckBox.Unchecked += (s, e) => EnableRoundedCorners = false;

            CornerRadiusTextBox.Text = CornerRadiusPx.ToString();
            CornerRadiusTextBox.TextChanged += (s, e) => { if (double.TryParse(CornerRadiusTextBox.Text, out double r)) CornerRadiusPx = r; };
        }

        private void LoadIconSetting()
        {
            try
            {
                var settings = IconSettings.Load();
                var activeApp = app ?? Application.Current as App;
                if (activeApp != null && !string.Equals(activeApp.CurrentIconPath, settings.IconPath, StringComparison.OrdinalIgnoreCase))
                {
                    settings.IconType = "custom";
                    settings.IconPath = activeApp.CurrentIconPath;
                }
                SelectedIconType = settings.IconType;
                if (SelectedIconType == "custom") customIconPath = settings.IconPath;
            }
            catch (Exception ex)
            {
                MessageBox.Show($"加载图标设置失败: {ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
            
            // 设置单选按钮状态
            switch (SelectedIconType)
            {
                case "default":
                    DefaultIconRadio.IsChecked = true;
                    break;
                case "dingding":
                    DingDingIconRadio.IsChecked = true;
                    break;
                case "feishu":
                    FeishuIconRadio.IsChecked = true;
                    break;
                case "wecom":
                    WecomIconRadio.IsChecked = true;
                    break;
                case "weixin":
                    WeixinIconRadio.IsChecked = true;
                    break;
                case "custom":
                    CustomIconRadio.IsChecked = true;
                    CustomIconButton.ToolTip = customIconPath;
                    break;
                default:
                    DefaultIconRadio.IsChecked = true;
                    break;
            }
        }

        private void CustomIconButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                string targetPath = IconSettings.SelectAndImport(this);
                if (targetPath != null)
                {
                    SelectedIconType = "custom";
                    customIconPath = targetPath;
                    CustomIconRadio.IsChecked = true;
                    CustomIconButton.ToolTip = targetPath;
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"选择自定义图标失败: {ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void LoadDefaultRulesButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var adBlocker = new AdBlocker();
                adBlocker.LoadDefaultRules();
                AdBlockRulesText = string.Join("\r\n", adBlocker.BlockRules);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"加载默认规则失败: {ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void OkButton_Click(object sender, RoutedEventArgs e)
        {
            if (SaveAndApplyChanges())
            {
                Close();
            }
        }

        private void ApplyButton_Click(object sender, RoutedEventArgs e)
        {
            SaveAndApplyChanges();
        }

        private bool SaveAndApplyChanges()
        {
            try
            {
                if (CustomIconRadio.IsChecked == true) IconSettings.ValidatePath(customIconPath);
                var defaults = new AppSettings();
                if (!double.TryParse(WidthTextBox.Text, out double width)
                    || !double.IsFinite(width) || width < 200 || width > 3840)
                {
                    width = defaults.WindowWidth;
                    (Application.Current as App)?.WriteDiagnostic("Invalid settings width reset to default.");
                }

                if (!double.TryParse(HeightTextBox.Text, out double height)
                    || !double.IsFinite(height) || height < 150 || height > 2160)
                {
                    height = defaults.WindowHeight;
                    (Application.Current as App)?.WriteDiagnostic("Invalid settings height reset to default.");
                }
                WidthTextBox.Text = width.ToString();
                HeightTextBox.Text = height.ToString();

                // 更新设置对象
                currentSettings.WindowWidth = width;
                currentSettings.WindowHeight = height;
                currentSettings.IsTopMost = IsTopMost;
                currentSettings.IsSuperMinimalMode = IsSuperMinimalMode;
                currentSettings.IsAnimationEnabled = IsAnimationEnabled;
                currentSettings.IsDarkMode = IsDarkMode;
                currentSettings.ZoomFactor = ZoomFactor;
                currentSettings.IsMobileUA = IsMobileUA;
                currentSettings.IsAdBlockEnabled = IsAdBlockEnabled;
                currentSettings.AdBlockRules = AdBlockRulesText.Split(new[] { "\r\n", "\r", "\n" }, StringSplitOptions.RemoveEmptyEntries)
                    .Select(r => r.Trim())
                    .Where(r => !string.IsNullOrEmpty(r))
                    .ToList();
                currentSettings.AdAllowRules = AdAllowRulesText.Split(new[] { "\r\n", "\r", "\n" }, StringSplitOptions.RemoveEmptyEntries)
                    .Select(r => r.Trim())
                    .Where(r => !string.IsNullOrEmpty(r))
                    .ToList();
                currentSettings.EnableGlobalHotKey = EnableGlobalHotKey;
                currentSettings.Hotkey = Hotkey;
                currentSettings.IsProxyEnabled = IsProxyEnabled;
                currentSettings.ProxyServer = ProxyServer;
                currentSettings.AutoZoomOutOnStartup = AutoZoomOutOnStartup;
                currentSettings.EnableRoundedCorners = EnableRoundedCorners;

                // 验证圆角大小
                if (!double.TryParse(CornerRadiusTextBox.Text, out double cornerRadius) || cornerRadius < 0 || cornerRadius > 200)
                {
                    MessageBox.Show("圆角大小必须在 0-200 之间！", "错误", MessageBoxButton.OK, MessageBoxImage.Warning);
                    CornerRadiusTextBox.Focus();
                    return false;
                }
                currentSettings.CornerRadiusPx = cornerRadius;

                // 应用设置到主窗口
                if (mainWindow != null)
                {
                    mainWindow.ApplySettings(currentSettings);
                }

                // 应用图标设置
                string iconType = "default";
                string iconPath = "pack://application:,,,/Resources/Ampeross-Ampola-Chrome.ico";

                if (DingDingIconRadio.IsChecked == true)
                {
                    iconType = "dingding";
                    iconPath = "pack://application:,,,/Resources/alternative-icons/dingding.ico";
                }
                else if (FeishuIconRadio.IsChecked == true)
                {
                    iconType = "feishu";
                    iconPath = "pack://application:,,,/Resources/alternative-icons/feishu.ico";
                }
                else if (WecomIconRadio.IsChecked == true)
                {
                    iconType = "wecom";
                    iconPath = "pack://application:,,,/Resources/alternative-icons/wecom.ico";
                }
                else if (WeixinIconRadio.IsChecked == true)
                {
                    iconType = "weixin";
                    iconPath = "pack://application:,,,/Resources/alternative-icons/weixin.ico";
                }
                else if (CustomIconRadio.IsChecked == true)
                {
                    iconType = "custom";
                    iconPath = customIconPath;
                }

                if (app != null)
                {
                    app.SetApplicationIcon(iconPath);
                    app.SaveIconSetting(iconType, iconPath);
                }
                else
                {
                    ((App)Application.Current).SetApplicationIcon(iconPath);
                    new IconSettings { IconType = iconType, IconPath = iconPath }.Save();
                }

                // 保存设置到文件
                SaveSettingsToFile();

                // 更新原始设置对象（用于同步）
                CopySettings(originalSettings, currentSettings);

                // 标记设置已保存（非模态窗口不能设置DialogResult）
                SettingsSaved = true;
                return true;
            }
            catch (Exception ex)
            {
                MessageBox.Show($"保存设置失败: {ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
                return false;
            }
        }

        private void CancelButton_Click(object sender, RoutedEventArgs e)
        {
            // 非模态窗口不需要设置DialogResult
            Close();
        }

        private void SaveSettingsToFile()
        {
            try
            {
                string settingsFilePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "settings.json");
                string directory = Path.GetDirectoryName(settingsFilePath);
                if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
                {
                    Directory.CreateDirectory(directory);
                }

                var options = new System.Text.Json.JsonSerializerOptions 
                { 
                    WriteIndented = true,
                    Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
                };
                string json = System.Text.Json.JsonSerializer.Serialize(currentSettings, options);
                File.WriteAllText(settingsFilePath, json);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"保存设置文件失败: {ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        private void CopySettings(AppSettings target, AppSettings source)
        {
            target.ZoomFactor = source.ZoomFactor;
            target.IsMobileUA = source.IsMobileUA;
            target.WindowWidth = source.WindowWidth;
            target.WindowHeight = source.WindowHeight;
            target.WindowLeft = source.WindowLeft;
            target.WindowTop = source.WindowTop;
            target.IsDarkMode = source.IsDarkMode;
            target.IsTopMost = source.IsTopMost;
            target.IsSuperMinimalMode = source.IsSuperMinimalMode;
            target.IsAnimationEnabled = source.IsAnimationEnabled;
            target.IsAdBlockEnabled = source.IsAdBlockEnabled;
            target.AdBlockRules = new List<string>(source.AdBlockRules ?? new List<string>());
            target.AdAllowRules = new List<string>(source.AdAllowRules ?? new List<string>());
            target.Hotkey = source.Hotkey;
            target.EnableGlobalHotKey = source.EnableGlobalHotKey;
            target.IsProxyEnabled = source.IsProxyEnabled;
            target.ProxyServer = source.ProxyServer;
            target.AutoZoomOutOnStartup = source.AutoZoomOutOnStartup;
            target.EnableRoundedCorners = source.EnableRoundedCorners;
            target.CornerRadiusPx = source.CornerRadiusPx;
        }

        private void OpenBookmarkManager_Click(object sender, RoutedEventArgs e)
        {
            (app ?? Application.Current as App)?.OpenBookmarkManager();
        }

        private void OpenAppGroupManager_Click(object sender, RoutedEventArgs e)
        {
            try { (app ?? Application.Current as App)?.OpenAppGroupManager(); }
            catch (Exception ex)
            {
                MessageBox.Show(this, $"打开 App 组管理失败: {ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void GitHubButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
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

        private void EmailButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                {
                    FileName = "mailto:kasusaland@gmail.com",
                    UseShellExecute = true
                });
            }
            catch (Exception ex)
            {
                MessageBox.Show($"打开邮件客户端失败: {ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void AltSnapButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                {
                    FileName = "https://github.com/RamonUnch/AltSnap",
                    UseShellExecute = true
                });
            }
            catch (Exception ex)
            {
                MessageBox.Show($"打开 AltSnap 链接失败: {ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private async void CheckUpdateButton_Click(object sender, RoutedEventArgs e)
        {
            CheckUpdateButton.IsEnabled = false;
            UpdateStatusText.Text = "正在检查更新...";

            try
            {
                using var client = new HttpClient();
                client.DefaultRequestHeaders.UserAgent.ParseAdd("TrayChrome");
                client.Timeout = TimeSpan.FromSeconds(15);

                string json = await client.GetStringAsync("https://api.github.com/repos/cornradio/tray-chrome/releases/latest");
                using var doc = JsonDocument.Parse(json);

                string tagName = doc.RootElement.GetProperty("tag_name").GetString() ?? "";
                string latestVersion = tagName.TrimStart('v', 'V');

                if (IsNewerVersion(latestVersion, CurrentVersion))
                {
                    UpdateStatusText.Text = $"发现新版本 {latestVersion}（当前 {CurrentVersion}）";
                    var result = MessageBox.Show(
                        $"发现新版本 {latestVersion}\n当前版本：{CurrentVersion}\n\n是否前往下载页面？",
                        "检查更新", MessageBoxButton.YesNo, MessageBoxImage.Information);

                    if (result == MessageBoxResult.Yes)
                    {
                        OpenUrl("https://github.com/cornradio/tray-chrome/releases/latest");
                    }
                }
                else
                {
                    UpdateStatusText.Text = $"当前已是最新版本（{CurrentVersion}）";
                }
            }
            catch (Exception)
            {
                UpdateStatusText.Text = "检查更新失败，请稍后重试。";
            }
            finally
            {
                CheckUpdateButton.IsEnabled = true;
            }
        }

        private static bool IsNewerVersion(string latest, string current)
        {
            if (System.Version.TryParse(latest, out var l) && System.Version.TryParse(current, out var c))
            {
                return l > c;
            }
            return false;
        }

        private void OpenUrl(string url)
        {
            try
            {
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                {
                    FileName = url,
                    UseShellExecute = true
                });
            }
            catch (Exception ex)
            {
                MessageBox.Show($"打开链接失败: {ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
    }
}
