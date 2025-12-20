using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
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
        private List<Bookmark> bookmarks = new List<Bookmark>();
        private string bookmarksFilePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "bookmarks.json");
        
        // 收藏夹更新事件
        public event EventHandler? BookmarksUpdated;
        
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

        public SettingsWindow(AppSettings settings, MainWindow? mainWindow = null, App? app = null)
        {
            InitializeComponent();
            
            this.mainWindow = mainWindow;
            this.app = app;
            this.originalSettings = settings;
            
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
                EnableGlobalHotKey = settings.EnableGlobalHotKey
            };
            
            // 加载当前设置到UI
            LoadSettingsToUI();
            
            // 设置数据绑定
            SetupDataBinding();
            
            // 加载当前图标设置
            LoadIconSetting();
            
            // 加载收藏夹
            LoadBookmarks();
            RefreshBookmarkList();
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
        }

        private void LoadIconSetting()
        {
            try
            {
                string settingsFile = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "icon-settings.json");
                if (File.Exists(settingsFile))
                {
                    string json = File.ReadAllText(settingsFile);
                    var settings = System.Text.Json.JsonSerializer.Deserialize<System.Text.Json.JsonElement>(json);
                    if (settings.TryGetProperty("IconType", out var iconTypeElement))
                    {
                        SelectedIconType = iconTypeElement.GetString() ?? "default";
                    }
                }
            }
            catch { }
            
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
                default:
                    DefaultIconRadio.IsChecked = true;
                    break;
            }
        }

        private void CustomIconButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var openFileDialog = new OpenFileDialog();
                openFileDialog.Title = "选择图标文件";
                openFileDialog.Filter = "图标文件 (*.ico)|*.ico|PNG图片 (*.png)|*.png|所有文件 (*.*)|*.*";
                openFileDialog.FilterIndex = 1;
                openFileDialog.CheckFileExists = true;
                
                if (openFileDialog.ShowDialog() == true && !string.IsNullOrEmpty(openFileDialog.FileName))
                {
                    string selectedPath = openFileDialog.FileName;
                    
                    if (!File.Exists(selectedPath))
                    {
                        MessageBox.Show("选择的文件不存在！", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
                        return;
                    }
                    
                    var fileInfo = new FileInfo(selectedPath);
                    if (fileInfo.Length > 5 * 1024 * 1024)
                    {
                        MessageBox.Show("图标文件过大，请选择小于5MB的文件！", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
                        return;
                    }
                    
                    // 复制图标到应用程序目录
                    string customIconsDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Resources", "custom-icons");
                    Directory.CreateDirectory(customIconsDir);
                    
                    string fileName = $"custom_{DateTime.Now:yyyyMMddHHmmss}{Path.GetExtension(selectedPath)}";
                    string targetPath = Path.Combine(customIconsDir, fileName);
                    
                    File.Copy(selectedPath, targetPath, true);
                    
                    SelectedIconType = "custom";
                    SaveIconSetting("custom", targetPath);
                    
                    // 立即应用图标
                    if (app != null)
                    {
                        app.SetApplicationIcon(targetPath);
                    }
                    
                    MessageBox.Show("自定义图标设置成功！", "成功", MessageBoxButton.OK, MessageBoxImage.Information);
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"选择自定义图标失败: {ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void SaveIconSetting(string iconType, string iconPath)
        {
            try
            {
                string settingsFile = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "icon-settings.json");
                var settings = new
                {
                    IconType = iconType,
                    IconPath = iconPath,
                    LastUpdated = DateTime.Now
                };
                
                string json = System.Text.Json.JsonSerializer.Serialize(settings, new System.Text.Json.JsonSerializerOptions { WriteIndented = true });
                File.WriteAllText(settingsFile, json);
            }
            catch { }
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
            try
            {
                // 验证窗口大小
                if (!double.TryParse(WidthTextBox.Text, out double width) || width < 200 || width > 3840)
                {
                    MessageBox.Show("窗口宽度必须在 200-3840 之间！", "错误", MessageBoxButton.OK, MessageBoxImage.Warning);
                    WidthTextBox.Focus();
                    return;
                }
                
                if (!double.TryParse(HeightTextBox.Text, out double height) || height < 150 || height > 2160)
                {
                    MessageBox.Show("窗口高度必须在 150-2160 之间！", "错误", MessageBoxButton.OK, MessageBoxImage.Warning);
                    HeightTextBox.Focus();
                    return;
                }
                
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
                
                if (app != null)
                {
                    app.SetApplicationIcon(iconPath);
                    app.SaveIconSetting(iconType, iconPath);
                }
                
                // 保存设置到文件
                SaveSettingsToFile();
                
                // 更新原始设置对象（用于同步）
                CopySettings(originalSettings, currentSettings);
                
                // 标记设置已保存（非模态窗口不能设置DialogResult）
                SettingsSaved = true;
                Close();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"保存设置失败: {ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
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
        }

        // 收藏夹管理方法
        private void LoadBookmarks()
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(bookmarksFilePath));
                
                if (File.Exists(bookmarksFilePath))
                {
                    var json = File.ReadAllText(bookmarksFilePath);
                    bookmarks = System.Text.Json.JsonSerializer.Deserialize<List<Bookmark>>(json) ?? new List<Bookmark>();
                }
                else
                {
                    bookmarks = new List<Bookmark>();
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"加载收藏夹失败: {ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Warning);
                bookmarks = new List<Bookmark>();
            }
        }

        private void SaveBookmarks()
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(bookmarksFilePath));
                var json = System.Text.Json.JsonSerializer.Serialize(bookmarks, new System.Text.Json.JsonSerializerOptions { WriteIndented = true });
                File.WriteAllText(bookmarksFilePath, json);
                
                // 同步到主窗口
                if (mainWindow != null)
                {
                    mainWindow.RefreshBookmarkMenu();
                }
                
                // 触发收藏夹更新事件，通知App刷新
                BookmarksUpdated?.Invoke(this, EventArgs.Empty);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"保存收藏夹失败: {ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        private void RefreshBookmarkList()
        {
            BookmarkListBox.ItemsSource = null;
            BookmarkListBox.ItemsSource = bookmarks;
            BookmarkStatusText.Text = $"共 {bookmarks.Count} 个收藏";
        }

        private void AddBookmarkButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                string currentUrl = mainWindow?.webView?.CoreWebView2?.Source ?? "";
                string currentTitle = mainWindow?.webView?.CoreWebView2?.DocumentTitle ?? "未知页面";
                
                if (string.IsNullOrEmpty(currentUrl))
                {
                    // 如果没有当前URL，显示输入对话框
                    var dialog = new Window
                    {
                        Title = "添加收藏",
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
                    grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                    grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

                    var titleLabel = new Label { Content = "标题:", Margin = new Thickness(10, 10, 5, 5) };
                    Grid.SetRow(titleLabel, 0);
                    Grid.SetColumn(titleLabel, 0);
                    grid.Children.Add(titleLabel);

                    var titleTextBox = new TextBox { Margin = new Thickness(5, 10, 10, 5), VerticalAlignment = VerticalAlignment.Center };
                    Grid.SetRow(titleTextBox, 0);
                    Grid.SetColumn(titleTextBox, 1);
                    grid.Children.Add(titleTextBox);

                    var urlLabel = new Label { Content = "URL:", Margin = new Thickness(10, 5, 5, 5) };
                    Grid.SetRow(urlLabel, 1);
                    Grid.SetColumn(urlLabel, 0);
                    grid.Children.Add(urlLabel);

                    var urlTextBox = new TextBox { Margin = new Thickness(5, 5, 10, 5), VerticalAlignment = VerticalAlignment.Center };
                    Grid.SetRow(urlTextBox, 1);
                    Grid.SetColumn(urlTextBox, 1);
                    grid.Children.Add(urlTextBox);

                    var buttonPanel = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(10, 20, 10, 10) };
                    Grid.SetRow(buttonPanel, 2);
                    Grid.SetColumnSpan(buttonPanel, 2);

                    var okButton = new Button { Content = "确定", Width = 80, Height = 30, Margin = new Thickness(5, 0, 5, 0) };
                    okButton.Click += (s, args) =>
                    {
                        if (!string.IsNullOrWhiteSpace(titleTextBox.Text) && !string.IsNullOrWhiteSpace(urlTextBox.Text))
                        {
                            var newBookmark = new Bookmark
                            {
                                Title = titleTextBox.Text.Trim(),
                                Url = urlTextBox.Text.Trim()
                            };
                            
                            if (!bookmarks.Any(b => b.Url.Equals(newBookmark.Url, StringComparison.OrdinalIgnoreCase)))
                            {
                                bookmarks.Add(newBookmark);
                                SaveBookmarks();
                                RefreshBookmarkList();
                                dialog.DialogResult = true;
                                dialog.Close();
                            }
                            else
                            {
                                MessageBox.Show("该URL已在收藏夹中！", "提示", MessageBoxButton.OK, MessageBoxImage.Information);
                            }
                        }
                        else
                        {
                            MessageBox.Show("标题和URL不能为空！", "错误", MessageBoxButton.OK, MessageBoxImage.Warning);
                        }
                    };

                    var cancelButton = new Button { Content = "取消", Width = 80, Height = 30, Margin = new Thickness(5, 0, 5, 0) };
                    cancelButton.Click += (s, args) => { dialog.DialogResult = false; dialog.Close(); };

                    buttonPanel.Children.Add(okButton);
                    buttonPanel.Children.Add(cancelButton);
                    grid.Children.Add(buttonPanel);

                    dialog.Content = grid;
                    dialog.ShowDialog();
                }
                else
                {
                    // 使用当前页面URL
                    if (!bookmarks.Any(b => b.Url.Equals(currentUrl, StringComparison.OrdinalIgnoreCase)))
                    {
                        var newBookmark = new Bookmark
                        {
                            Title = currentTitle,
                            Url = currentUrl
                        };
                        bookmarks.Add(newBookmark);
                        SaveBookmarks();
                        RefreshBookmarkList();
                        MessageBox.Show($"已添加到收藏夹：{currentTitle}", "成功", MessageBoxButton.OK, MessageBoxImage.Information);
                    }
                    else
                    {
                        MessageBox.Show("该页面已在收藏夹中！", "提示", MessageBoxButton.OK, MessageBoxImage.Information);
                    }
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"添加收藏失败: {ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void EditBookmarkButton_Click(object sender, RoutedEventArgs e)
        {
            if (BookmarkListBox.SelectedItem is Bookmark bookmark)
            {
                EditBookmark(bookmark);
            }
            else
            {
                MessageBox.Show("请先选择一个收藏项！", "提示", MessageBoxButton.OK, MessageBoxImage.Information);
            }
        }

        private void DeleteBookmarkButton_Click(object sender, RoutedEventArgs e)
        {
            if (BookmarkListBox.SelectedItem is Bookmark bookmark)
            {
                var result = MessageBox.Show($"确定要删除收藏夹 \"{bookmark.Title}\" 吗？", 
                    "删除收藏夹", MessageBoxButton.YesNo, MessageBoxImage.Question);
                
                if (result == MessageBoxResult.Yes)
                {
                    bookmarks.Remove(bookmark);
                    SaveBookmarks();
                    RefreshBookmarkList();
                }
            }
            else
            {
                MessageBox.Show("请先选择一个收藏项！", "提示", MessageBoxButton.OK, MessageBoxImage.Information);
            }
        }

        private void RefreshBookmarkButton_Click(object sender, RoutedEventArgs e)
        {
            LoadBookmarks();
            RefreshBookmarkList();
        }

        private void BookmarkListBox_MouseDoubleClick(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            if (BookmarkListBox.SelectedItem is Bookmark bookmark && mainWindow?.webView?.CoreWebView2 != null)
            {
                mainWindow.webView.CoreWebView2.Navigate(bookmark.Url);
                if (!mainWindow.IsVisible)
                {
                    mainWindow.Show();
                    mainWindow.WindowState = WindowState.Normal;
                    mainWindow.Activate();
                }
            }
        }

        private void EditBookmark(Bookmark bookmark)
        {
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
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            var titleLabel = new Label { Content = "标题:", Margin = new Thickness(10, 10, 5, 5) };
            Grid.SetRow(titleLabel, 0);
            Grid.SetColumn(titleLabel, 0);
            grid.Children.Add(titleLabel);

            var titleTextBox = new TextBox { Text = bookmark.Title, Margin = new Thickness(5, 10, 10, 5), VerticalAlignment = VerticalAlignment.Center };
            Grid.SetRow(titleTextBox, 0);
            Grid.SetColumn(titleTextBox, 1);
            grid.Children.Add(titleTextBox);

            var urlLabel = new Label { Content = "URL:", Margin = new Thickness(10, 5, 5, 5) };
            Grid.SetRow(urlLabel, 1);
            Grid.SetColumn(urlLabel, 0);
            grid.Children.Add(urlLabel);

            var urlTextBox = new TextBox { Text = bookmark.Url, Margin = new Thickness(5, 5, 10, 5), VerticalAlignment = VerticalAlignment.Center };
            Grid.SetRow(urlTextBox, 1);
            Grid.SetColumn(urlTextBox, 1);
            grid.Children.Add(urlTextBox);

            var buttonPanel = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(10, 20, 10, 10) };
            Grid.SetRow(buttonPanel, 2);
            Grid.SetColumnSpan(buttonPanel, 2);

            var okButton = new Button { Content = "确定", Width = 80, Height = 30, Margin = new Thickness(5, 0, 5, 0) };
            okButton.Click += (s, args) =>
            {
                if (!string.IsNullOrWhiteSpace(titleTextBox.Text) && !string.IsNullOrWhiteSpace(urlTextBox.Text))
                {
                    bookmark.Title = titleTextBox.Text.Trim();
                    bookmark.Url = urlTextBox.Text.Trim();
                    SaveBookmarks();
                    RefreshBookmarkList();
                    dialog.DialogResult = true;
                    dialog.Close();
                }
                else
                {
                    MessageBox.Show("标题和URL不能为空！", "错误", MessageBoxButton.OK, MessageBoxImage.Warning);
                }
            };

            var cancelButton = new Button { Content = "取消", Width = 80, Height = 30, Margin = new Thickness(5, 0, 5, 0) };
            cancelButton.Click += (s, args) => { dialog.DialogResult = false; dialog.Close(); };

            buttonPanel.Children.Add(okButton);
            buttonPanel.Children.Add(cancelButton);
            grid.Children.Add(buttonPanel);

            dialog.Content = grid;
            dialog.ShowDialog();
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
    }
}

