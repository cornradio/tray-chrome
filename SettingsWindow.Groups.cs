#nullable enable
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Microsoft.Win32;

namespace TrayChrome
{
    public partial class SettingsWindow
    {
        private bool updatingGroupFiles;
        private AppGroup? displayedAppGroup;
        private string? SelectedAppGroupPath => AppGroupFilesComboBox.SelectedValue as string;

        private sealed class AppGroupFileOption
        {
            public string FilePath { get; }
            public string FileName => Path.GetFileName(FilePath);
            public AppGroupFileOption(string path) => FilePath = path;
        }

        private sealed class AppGroupRow
        {
            public AppGroupEntry Entry { get; }
            public AppGroupEntry Owner { get; }
            public int TabIndex { get; }
            public string Instance { get; }
            public string Url => Entry.Url;
            public string Hotkey => string.IsNullOrWhiteSpace(Entry.Hotkey) ? "未设置" : Entry.Hotkey;
            public ImageSource Logo { get; }
            public AppGroupRow(AppGroupEntry owner, int instanceIndex, int tabIndex = -1)
            {
                Owner = owner;
                TabIndex = tabIndex;
                Instance = $"实例 {instanceIndex + 1}";
                Entry = CopyAppGroupEntry(owner);
                if (tabIndex >= 0) Entry.Url = owner.Tabs[tabIndex];
                Logo = IconSettings.LoadImage(owner.IconPath);
            }
        }

        private void InitializeAppGroups(string? selectedPath = null)
        {
            try
            {
                var catalog = AppGroups.LoadCatalog();
                updatingGroupFiles = true;
                AppGroupFilesComboBox.ItemsSource = catalog.Files.Select(path => new AppGroupFileOption(path)).ToList();
                AppGroupFilesComboBox.SelectedValue = selectedPath ?? catalog.ActiveFile;
                updatingGroupFiles = false;
                ShowSelectedAppGroup();
            }
            catch (Exception ex)
            {
                updatingGroupFiles = false;
                ShowAppGroupError(ex);
            }
        }

        private void AppGroupFilesComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (updatingGroupFiles) return;
            try { ShowSelectedAppGroup(); }
            catch (Exception ex) { ShowAppGroupError(ex); }
        }

        private void ShowSelectedAppGroup()
        {
            AppGroupGrid.ItemsSource = null;
            displayedAppGroup = null;
            AppGroupStatusText.Text = "暂无配置，可导入组文件或保存当前窗口。";
            if (SelectedAppGroupPath is not string path) return;
            var group = AppGroups.Load(path);
            var rows = group.Apps.SelectMany((entry, index) =>
                new[] { new AppGroupRow(entry, index) }.Concat(
                    entry.Tabs.Select((_, tabIndex) => new AppGroupRow(entry, index, tabIndex)))).ToList();
            AppGroups.Remember(path);
            displayedAppGroup = group;
            AppGroupGrid.ItemsSource = rows;
            AppGroupStatusText.Text = $"{Path.GetFileName(path)} · {group.Apps.Count} 个实例 · {rows.Count} 个网页";
        }

        private void ImportAppGroup_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new OpenFileDialog { Title = "导入 App 组", Filter = "App 组 (*.json)|*.json" };
            if (dialog.ShowDialog(this) != true) return;
            try
            {
                AppGroups.Load(dialog.FileName);
                AppGroups.Remember(dialog.FileName);
                InitializeAppGroups(dialog.FileName);
            }
            catch (Exception ex) { ShowAppGroupError(ex); }
        }

        private async void SaveAppGroup_Click(object sender, RoutedEventArgs e)
        {
            string path = SelectedAppGroupPath ?? AppGroups.DefaultFile;
            if (File.Exists(path) && MessageBox.Show(this, "用当前所有运行中的 App 覆盖所选配置？包括隐藏到托盘的实例。", "保存 App 组",
                MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;
            SetAppGroupBusy(true);
            try
            {
                string name = displayedAppGroup?.Name ?? Path.GetFileNameWithoutExtension(path);
                var group = await AppGroups.CaptureAsync(name);
                SaveAppGroup(path, group);
            }
            catch (Exception ex) { ShowAppGroupError(ex); }
            finally { SetAppGroupBusy(false); }
        }

        private void SaveAppGroupAs_Click(object sender, RoutedEventArgs e)
        {
            if (displayedAppGroup == null)
            {
                ShowAppGroupError(new InvalidOperationException("请先选择一个可读取的配置。"));
                return;
            }
            var dialog = new SaveFileDialog
            {
                Title = "当前配置另存为",
                Filter = "App 组 (*.json)|*.json",
                FileName = SelectedAppGroupPath == null ? "apps.json" : Path.GetFileName(SelectedAppGroupPath),
                DefaultExt = ".json",
                InitialDirectory = AppDomain.CurrentDomain.BaseDirectory
            };
            if (dialog.ShowDialog(this) == true)
            {
                try { SaveAppGroup(dialog.FileName, displayedAppGroup); }
                catch (Exception ex) { ShowAppGroupError(ex); }
            }
        }

        private void SaveAppGroup(string path, AppGroup group)
        {
            SetAppGroupBusy(true);
            try
            {
                AppGroups.Save(path, group);
                AppGroups.Remember(path);
                InitializeAppGroups(path);
            }
            catch (Exception ex) { ShowAppGroupError(ex); }
            finally
            {
                SetAppGroupBusy(false);
            }
        }

        private async void LoadAppGroup_Click(object sender, RoutedEventArgs e)
        {
            await LoadSelectedAppGroupAsync(false);
        }

        private async void LoadAppGroupAndClose_Click(object sender, RoutedEventArgs e)
        {
            await LoadSelectedAppGroupAsync(true);
        }

        private async Task LoadSelectedAppGroupAsync(bool closeCurrent)
        {
            if (SelectedAppGroupPath is not string path)
            {
                ShowAppGroupError(new InvalidOperationException("请先选择一个 App 组。"));
                return;
            }
            SetAppGroupBusy(true);
            try
            {
                var currentApp = (App)Application.Current;
                bool loaded = await currentApp.LoadAppGroupAsync(path);
                if (loaded && closeCurrent) currentApp.Shutdown();
            }
            finally { SetAppGroupBusy(false); }
        }

        private void CreateAppGroupShortcut_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (SelectedAppGroupPath is not string path)
                    throw new InvalidOperationException("请先选择一个 App 组。");
                string shortcutPath = ((App)Application.Current).CreateAppGroupShortcut(path);
                AppGroupStatusText.Text = $"已创建快捷方式：{shortcutPath}";
                MessageBox.Show(this, AppGroupStatusText.Text, "成功", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex) { ShowAppGroupError(ex); }
        }

        private void SetAppGroupBusy(bool busy)
        {
            AppGroupActions.IsEnabled = !busy;
            AppGroupLoadActions.IsEnabled = !busy;
            AppGroupFileActions.IsEnabled = !busy;
            AppGroupFilesComboBox.IsEnabled = !busy;
            AppGroupGrid.IsEnabled = !busy;
        }

        private void RemoveAppGroup_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                string path = SelectedAppGroupPath ?? throw new InvalidOperationException("请先选择一个 App 组。");
                AppGroups.Remove(path);
                InitializeAppGroups();
            }
            catch (Exception ex) { ShowAppGroupError(ex); }
        }

        private void OpenAppGroupFolder_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                string path = SelectedAppGroupPath ?? throw new InvalidOperationException("请先选择一个 App 组。");
                if (!File.Exists(path)) throw new FileNotFoundException("配置文件不存在。", path);
                Process.Start(new ProcessStartInfo("explorer.exe", $"/select,\"{path}\"") { UseShellExecute = true });
            }
            catch (Exception ex) { ShowAppGroupError(ex); }
        }

        private void DeleteAppGroupItem_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not Button { DataContext: AppGroupRow row }) return;
            try
            {
                var group = CreateEditedAppGroup(row, null);
                SaveEditedAppGroup(group);
            }
            catch (Exception ex) { ShowAppGroupError(ex); }
        }

        private void ChangeAppGroupIcon_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not Button { DataContext: AppGroupRow row }) return;
            try
            {
                string? selectedIcon = ChooseAppGroupIcon(this);
                if (selectedIcon == null) return;
                var replacement = CopyAppGroupEntry(row.Entry);
                replacement.IconPath = selectedIcon;
                SaveEditedAppGroup(CreateEditedAppGroup(row, replacement));
            }
            catch (Exception ex) { ShowAppGroupError(ex); }
        }

        private static string? ChooseAppGroupIcon(Window owner)
        {
            return IconSettings.SelectAndImport(owner);
        }

        private static AppGroupEntry CopyAppGroupEntry(AppGroupEntry entry) => new AppGroupEntry
        {
            Url = entry.Url,
            IconPath = entry.IconPath,
            Width = entry.Width,
            Height = entry.Height,
            IsSuperMinimalMode = entry.IsSuperMinimalMode,
            Hotkey = entry.Hotkey,
            Tabs = new List<string>(entry.Tabs)
        };

        private AppGroup CreateEditedAppGroup(AppGroupRow row, AppGroupEntry? replacement)
        {
            var current = displayedAppGroup ?? throw new InvalidOperationException("请重新选择要编辑的配置。");
            int index = current.Apps.IndexOf(row.Owner);
            if (index < 0) throw new InvalidOperationException("条目已发生变化，请重新选择配置。");
            var result = new AppGroup { Name = current.Name, Apps = new List<AppGroupEntry>(current.Apps) };
            var updated = CopyAppGroupEntry(row.Owner);
            if (replacement == null)
            {
                if (row.TabIndex >= 0) updated.Tabs.RemoveAt(row.TabIndex);
                else if (updated.Tabs.Count > 0)
                {
                    updated.Url = updated.Tabs[0];
                    updated.Tabs.RemoveAt(0);
                }
                else { result.Apps.RemoveAt(index); return result; }
            }
            else
            {
                if (row.TabIndex >= 0) updated.Tabs[row.TabIndex] = replacement.Url;
                else updated.Url = replacement.Url;
                updated.IconPath = replacement.IconPath;
                updated.Hotkey = replacement.Hotkey;
            }
            result.Apps[index] = updated;
            return result;
        }

        private void SaveEditedAppGroup(AppGroup group)
        {
            string path = SelectedAppGroupPath ?? throw new InvalidOperationException("请先选择一个 App 组。");
            AppGroups.Save(path, group);
            ShowSelectedAppGroup();
        }

        private async void StartAppGroupItem_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not Button { DataContext: AppGroupRow row }) return;
            SetAppGroupBusy(true);
            try
            {
                string path = SelectedAppGroupPath ?? throw new InvalidOperationException("请先选择一个 App 组。");
                int index = displayedAppGroup?.Apps.IndexOf(row.Owner) ?? -1;
                if (index < 0) throw new InvalidOperationException("条目已发生变化，请重新选择配置。");
                bool started = await ((App)Application.Current).LoadAppGroupAsync(path, index);
                if (started) AppGroupStatusText.Text = $"已启动到托盘：{row.Url}";
            }
            catch (Exception ex) { ShowAppGroupError(ex); }
            finally { SetAppGroupBusy(false); }
        }

        private void EditAppGroupItem_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not Button { DataContext: AppGroupRow row }) return;
            var dialog = new Window
            {
                Title = "编辑 App",
                Owner = this,
                Width = 480,
                SizeToContent = SizeToContent.Height,
                ResizeMode = ResizeMode.NoResize,
                WindowStartupLocation = WindowStartupLocation.CenterOwner
            };
            var panel = new StackPanel { Margin = new Thickness(15) };
            panel.Children.Add(new TextBlock { Text = "URL（HTTP / HTTPS）", Margin = new Thickness(5) });
            var urlBox = new TextBox { Text = row.Url };
            panel.Children.Add(urlBox);
            panel.Children.Add(new TextBlock { Text = "实例快捷键（留空不注册）", Margin = new Thickness(5) });
            var hotkeyPanel = new DockPanel();
            var clearHotkey = new Button { Content = "清除", MinWidth = 60 };
            DockPanel.SetDock(clearHotkey, Dock.Right);
            hotkeyPanel.Children.Add(clearHotkey);
            var hotkeyBox = new TextBox
            {
                Text = row.Entry.Hotkey,
                IsReadOnly = true,
                IsReadOnlyCaretVisible = false,
                ToolTip = "点击后按下快捷键组合，使用清除按钮取消绑定",
                ContextMenu = null
            };
            hotkeyPanel.Children.Add(hotkeyBox);
            panel.Children.Add(hotkeyPanel);
            var hotkeyHint = new TextBlock
            {
                Text = "点击输入框后按组合键；至少一个修饰键 + A-Z、0-9 或 F1-F24。\n显示/隐藏对应实例，下次启动时生效，不改变当前实例。",
                Margin = new Thickness(5), TextWrapping = TextWrapping.Wrap, Foreground = Brushes.Gray
            };
            hotkeyBox.PreviewKeyDown += (s, args) =>
            {
                // 保留 Tab 导航和 Esc 取消，其他输入只用于录入组合键。
                Key key = args.Key == Key.System ? args.SystemKey : args.Key;
                if (key == Key.Tab || key == Key.Escape) return;
                args.Handled = true;
                if (key == Key.LeftCtrl || key == Key.RightCtrl || key == Key.LeftAlt || key == Key.RightAlt
                    || key == Key.LeftShift || key == Key.RightShift || key == Key.LWin || key == Key.RWin)
                    return;
                var modifiers = Keyboard.Modifiers;
                if (Keyboard.IsKeyDown(Key.LWin) || Keyboard.IsKeyDown(Key.RWin)) modifiers |= ModifierKeys.Windows;
                uint code = (uint)KeyInterop.VirtualKeyFromKey(key);
                string keyName;
                if ((code >= 0x41 && code <= 0x5A) || (code >= 0x30 && code <= 0x39))
                    keyName = ((char)code).ToString();
                else if (code >= 0x70 && code <= 0x87) keyName = $"F{code - 0x70 + 1}";
                else
                {
                    hotkeyHint.Text = "此按键不支持，请使用修饰键 + A-Z、0-9 或 F1-F24。";
                    return;
                }
                if (modifiers == ModifierKeys.None)
                {
                    hotkeyHint.Text = "请同时按住 Ctrl、Alt、Shift 或 Win 中的至少一个修饰键。";
                    return;
                }
                var parts = new List<string>();
                if (modifiers.HasFlag(ModifierKeys.Control)) parts.Add("Ctrl");
                if (modifiers.HasFlag(ModifierKeys.Alt)) parts.Add("Alt");
                if (modifiers.HasFlag(ModifierKeys.Shift)) parts.Add("Shift");
                if (modifiers.HasFlag(ModifierKeys.Windows)) parts.Add("Win");
                parts.Add(keyName);
                hotkeyBox.Text = GlobalHotKeyManager.ParseHotkey(string.Join(" + ", parts), out _, out _);
                hotkeyHint.Text = "已录入快捷键，点击保存后，下次启动此 App 时生效。";
            };
            clearHotkey.Click += (s, args) =>
            {
                hotkeyBox.Text = string.Empty;
                hotkeyHint.Text = "已清除，保存后此 App 不注册快捷键。";
            };
            panel.Children.Add(hotkeyHint);
            var iconPanel = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(5) };
            var preview = new Image { Source = row.Logo, Width = 40, Height = 40, Margin = new Thickness(5) };
            var chooseIcon = new Button { Content = "更换图标..." };
            iconPanel.Children.Add(preview);
            iconPanel.Children.Add(chooseIcon);
            panel.Children.Add(iconPanel);
            string? selectedIcon = null;
            chooseIcon.Click += (s, args) =>
            {
                try
                {
                    string? path = ChooseAppGroupIcon(dialog);
                    if (path == null) return;
                    preview.Source = IconSettings.LoadImage(path);
                    selectedIcon = path;
                }
                catch (Exception ex)
                {
                    MessageBox.Show(dialog, $"选择图标失败：{ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            };
            var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
            var save = new Button { Content = "保存" };
            var cancel = new Button { Content = "取消", IsCancel = true };
            save.Click += (s, args) =>
            {
                try
                {
                    string url = urlBox.Text.Trim();
                    if (!Uri.TryCreate(url, UriKind.Absolute, out var uri)
                        || (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
                        throw new InvalidDataException("URL 必须是有效的 HTTP 或 HTTPS 地址。");
                    var replacement = CopyAppGroupEntry(row.Entry);
                    replacement.Url = url;
                    replacement.Hotkey = string.IsNullOrWhiteSpace(hotkeyBox.Text) ? string.Empty
                        : GlobalHotKeyManager.ParseHotkey(hotkeyBox.Text, out _, out _);
                    if (selectedIcon != null) replacement.IconPath = selectedIcon;
                    SaveEditedAppGroup(CreateEditedAppGroup(row, replacement));
                    dialog.DialogResult = true;
                }
                catch (Exception ex)
                {
                    MessageBox.Show(dialog, $"保存 App 失败：{ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            };
            buttons.Children.Add(save);
            buttons.Children.Add(cancel);
            panel.Children.Add(buttons);
            dialog.Content = panel;
            dialog.Loaded += (s, args) => { urlBox.Focus(); urlBox.SelectAll(); };
            dialog.ShowDialog();
        }

        private void ShowAppGroupError(Exception ex)
        {
            AppGroupStatusText.Text = $"App 组操作失败：{ex.Message}";
            MessageBox.Show(this, AppGroupStatusText.Text, "错误", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }
}
