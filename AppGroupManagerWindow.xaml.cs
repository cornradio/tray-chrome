#nullable enable
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Media;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Microsoft.Win32;

namespace TrayChrome
{
    public partial class AppGroupManagerWindow : Window
    {
        private bool updatingGroupFiles;
        private AppGroup? displayedAppGroup;
        private string? displayedPath;
        private Window? editDialog;

        public AppGroupManagerWindow()
        {
            InitializeComponent();
            InitializeAppGroups();
            Closed += (_, _) => editDialog?.Close();
        }

        private string? SelectedAppGroupPath => (AppGroupFilesComboBox.SelectedItem as AppGroupFileOption)?.FilePath;
        private AppGroupRow? SelectedRow => AppGroupGrid.SelectedItem as AppGroupRow;

        private sealed class AppGroupFileOption
        {
            public string? FilePath { get; }
            public string FileName { get; }
            public AppGroupFileOption(string? path)
            {
                FilePath = path;
                FileName = path == null ? "＋ 新建组..." : Path.GetFileName(path);
            }
        }

        private sealed class AppGroupRow
        {
            public AppGroupEntry Entry { get; }
            public int Index { get; }
            public IReadOnlyList<string> AllUrls { get; }
            public string Urls => string.Join("   |   ", AllUrls);
            public string UrlList => string.Join(Environment.NewLine, AllUrls);
            public string Hotkey => string.IsNullOrWhiteSpace(Entry.Hotkey) ? "未设置" : Entry.Hotkey;
            public ImageSource Logo { get; }
            public AppGroupRow(AppGroupEntry entry, int index)
            {
                Entry = entry;
                Index = index;
                AllUrls = new[] { entry.Url }.Concat(entry.Tabs).ToList();
                Logo = IconSettings.LoadImage(entry.IconPath);
            }
        }

        public void ActivateManager()
        {
            if (WindowState == WindowState.Minimized) WindowState = WindowState.Normal;
            Show();
            Activate();
            if (editDialog != null)
            {
                if (editDialog.WindowState == WindowState.Minimized) editDialog.WindowState = WindowState.Normal;
                editDialog.Activate();
            }
        }

        private void InitializeAppGroups(string? selectedPath = null)
        {
            try
            {
                var catalog = AppGroups.LoadCatalog();
                var options = catalog.Files.Select(path => new AppGroupFileOption(path)).ToList();
                options.Add(new AppGroupFileOption(null));
                string? target = selectedPath ?? catalog.ActiveFile;
                updatingGroupFiles = true;
                AppGroupFilesComboBox.ItemsSource = options;
                AppGroupFilesComboBox.SelectedItem = options.FirstOrDefault(option => option.FilePath != null
                    && string.Equals(option.FilePath, target, StringComparison.OrdinalIgnoreCase))
                    ?? options.FirstOrDefault(option => option.FilePath != null);
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
            if (AppGroupFilesComboBox.SelectedItem is AppGroupFileOption { FilePath: null })
            {
                RestoreDisplayedSelection();
                Dispatcher.BeginInvoke(new Action(CreateAppGroup));
                return;
            }
            try { ShowSelectedAppGroup(); }
            catch (Exception ex) { ShowAppGroupError(ex); }
        }

        private void RestoreDisplayedSelection()
        {
            updatingGroupFiles = true;
            AppGroupFilesComboBox.SelectedItem = AppGroupFilesComboBox.Items.OfType<AppGroupFileOption>()
                .FirstOrDefault(option => option.FilePath != null
                    && string.Equals(option.FilePath, displayedPath, StringComparison.OrdinalIgnoreCase));
            updatingGroupFiles = false;
        }

        private void CreateAppGroup()
        {
            string? name = PromptText("新建组", "组名称：", string.Empty);
            if (name == null) return;
            try
            {
                name = name.Trim();
                if (name.Length == 0 || name.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
                    throw new InvalidDataException("组名称不能为空，且不能包含文件名非法字符。");
                string path = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, name + ".json");
                if (File.Exists(path)) throw new IOException($"{Path.GetFileName(path)} 已存在。");
                AppGroups.Save(path, new AppGroup { Name = name });
                AppGroups.Remember(path);
                InitializeAppGroups(path);
            }
            catch (Exception ex) { ShowAppGroupError(ex); }
        }

        private void ShowSelectedAppGroup(int? selectIndex = null)
        {
            AppGroupGrid.ItemsSource = null;
            displayedAppGroup = null;
            displayedPath = null;
            Title = "App 组管理";
            if (SelectedAppGroupPath is not string path) return;
            var group = AppGroups.Load(path);
            var rows = group.Apps.Select((entry, index) => new AppGroupRow(entry, index)).ToList();
            AppGroups.Remember(path);
            displayedAppGroup = group;
            displayedPath = path;
            AppGroupGrid.ItemsSource = rows;
            Title = $"App 组管理 - {Path.GetFileName(path)} · {rows.Count} 个实例";
            if (selectIndex.HasValue && rows.Count > 0)
            {
                var row = rows[Math.Clamp(selectIndex.Value, 0, rows.Count - 1)];
                AppGroupGrid.SelectedItem = row;
                AppGroupGrid.ScrollIntoView(row);
            }
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
                AppGroups.Save(path, group);
                AppGroups.Remember(path);
                InitializeAppGroups(path);
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
            if (dialog.ShowDialog(this) != true) return;
            try
            {
                AppGroups.Save(dialog.FileName, displayedAppGroup);
                AppGroups.Remember(dialog.FileName);
                InitializeAppGroups(dialog.FileName);
            }
            catch (Exception ex) { ShowAppGroupError(ex); }
        }

        private async void LoadAppGroup_Click(object sender, RoutedEventArgs e)
        {
            if (SelectedAppGroupPath is not string path)
            {
                ShowAppGroupError(new InvalidOperationException("请先选择一个 App 组。"));
                return;
            }
            SetAppGroupBusy(true);
            try { await ((App)Application.Current).LoadAppGroupReplacingCurrentAsync(path); }
            finally { SetAppGroupBusy(false); }
        }

        private void CreateAppGroupShortcut_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                string path = SelectedAppGroupPath ?? throw new InvalidOperationException("请先选择一个 App 组。");
                string shortcutPath = ((App)Application.Current).CreateAppGroupShortcut(path);
                MessageBox.Show(this, $"已创建快捷方式：{shortcutPath}", "成功", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex) { ShowAppGroupError(ex); }
        }

        private void SetAppGroupBusy(bool busy)
        {
            AppGroupFileBar.IsEnabled = !busy;
            AppGroupActions.IsEnabled = !busy;
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

        private void AppGroupGrid_PreviewMouseRightButtonDown(object sender, MouseButtonEventArgs e)
        {
            var row = FindParent<DataGridRow>(e.OriginalSource as DependencyObject);
            AppGroupGrid.SelectedItem = row?.Item;
        }

        private void AppGroupGrid_ContextMenuOpening(object sender, ContextMenuEventArgs e)
        {
            bool hasRow = SelectedRow != null;
            StartAppMenuItem.IsEnabled = hasRow;
            EditAppMenuItem.IsEnabled = hasRow;
            IconAppMenuItem.IsEnabled = hasRow;
            DeleteAppMenuItem.IsEnabled = hasRow;
        }

        private void AppGroupGrid_MouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            if (e.ChangedButton != MouseButton.Left) return;
            if (FindParent<DataGridRow>(e.OriginalSource as DependencyObject)?.Item is AppGroupRow row)
                EditAppGroupEntry(row);
        }

        private void AppGroupGrid_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (SelectedRow is not AppGroupRow row) return;
            if (e.Key == Key.Delete)
            {
                DeleteRow(row);
                e.Handled = true;
            }
            else if (e.Key == Key.Enter)
            {
                EditAppGroupEntry(row);
                e.Handled = true;
            }
        }

        private static T? FindParent<T>(DependencyObject? source) where T : DependencyObject
        {
            while (source != null && source is not T)
                source = source is Visual || source is System.Windows.Media.Media3D.Visual3D
                    ? VisualTreeHelper.GetParent(source) : LogicalTreeHelper.GetParent(source);
            return source as T;
        }

        private void DeleteAppGroupItem_Click(object sender, RoutedEventArgs e)
        {
            if (SelectedRow is AppGroupRow row) DeleteRow(row);
        }

        private void DeleteRow(AppGroupRow row)
        {
            try { SaveEntry(row.Index, null); }
            catch (Exception ex) { ShowAppGroupError(ex); }
        }

        private void ChangeAppGroupIcon_Click(object sender, RoutedEventArgs e)
        {
            if (SelectedRow is not AppGroupRow row) return;
            try
            {
                string? selectedIcon = IconSettings.SelectAndImport(this);
                if (selectedIcon == null) return;
                var replacement = CopyAppGroupEntry(row.Entry);
                replacement.IconPath = selectedIcon;
                SaveEntry(row.Index, replacement);
            }
            catch (Exception ex) { ShowAppGroupError(ex); }
        }

        private static AppGroupEntry CopyAppGroupEntry(AppGroupEntry entry) => new AppGroupEntry
        {
            Url = entry.Url,
            IconPath = entry.IconPath,
            Width = entry.Width,
            Height = entry.Height,
            Left = entry.Left,
            Top = entry.Top,
            IsSuperMinimalMode = entry.IsSuperMinimalMode,
            Hotkey = entry.Hotkey,
            Tabs = new List<string>(entry.Tabs)
        };

        // index < 0 表示追加；replacement 为 null 表示删除。
        private void SaveEntry(int index, AppGroupEntry? replacement)
        {
            var current = displayedAppGroup ?? throw new InvalidOperationException("请先选择一个 App 组。");
            string path = displayedPath ?? throw new InvalidOperationException("请先选择一个 App 组。");
            if (index >= current.Apps.Count) throw new InvalidOperationException("条目已发生变化，请重新选择配置。");
            var apps = current.Apps.Select(CopyAppGroupEntry).ToList();
            int selectIndex;
            if (index < 0)
            {
                apps.Add(replacement ?? throw new ArgumentNullException(nameof(replacement)));
                selectIndex = apps.Count - 1;
            }
            else if (replacement == null)
            {
                apps.RemoveAt(index);
                selectIndex = index;
            }
            else
            {
                apps[index] = replacement;
                selectIndex = index;
            }
            AppGroups.Save(path, new AppGroup { Name = current.Name, Apps = apps });
            ShowSelectedAppGroup(selectIndex);
            AppGroupGrid.Focus();
        }

        private async void StartAppGroupItem_Click(object sender, RoutedEventArgs e)
        {
            if (SelectedRow is not AppGroupRow row) return;
            SetAppGroupBusy(true);
            try
            {
                string path = displayedPath ?? throw new InvalidOperationException("请先选择一个 App 组。");
                await ((App)Application.Current).LoadAppGroupAsync(path, row.Index);
            }
            catch (Exception ex) { ShowAppGroupError(ex); }
            finally { SetAppGroupBusy(false); }
        }

        private void EditAppGroupItem_Click(object sender, RoutedEventArgs e)
        {
            if (SelectedRow is AppGroupRow row) EditAppGroupEntry(row);
        }

        private void AddAppGroupItem_Click(object sender, RoutedEventArgs e)
        {
            if (displayedAppGroup == null)
            {
                ShowAppGroupError(new InvalidOperationException("请先选择或新建一个 App 组。"));
                return;
            }
            EditAppGroupEntry(null);
        }

        private static List<string> ParseUrls(string text)
        {
            var urls = new List<string>();
            foreach (string line in text.Split(new[] { "\r\n", "\r", "\n" }, StringSplitOptions.None))
            {
                string url = line.Trim();
                if (url.Length == 0) continue;
                if (!url.Contains("://")) url = "https://" + url;
                if (!Uri.TryCreate(url, UriKind.Absolute, out var uri)
                    || (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
                    throw new InvalidDataException($"不是有效的 HTTP/HTTPS 地址：{line.Trim()}");
                urls.Add(url);
            }
            if (urls.Count == 0) throw new InvalidDataException("请至少填写一个 URL。");
            return urls;
        }

        private void EditAppGroupEntry(AppGroupRow? row)
        {
            if (editDialog != null)
            {
                editDialog.Activate();
                return;
            }
            var source = row?.Entry ?? new AppGroupEntry();
            int index = row?.Index ?? -1;
            string? path = displayedPath;
            var dialog = new Window
            {
                Title = row == null ? "添加 App" : "编辑 App",
                Owner = this,
                Width = 520,
                SizeToContent = SizeToContent.Height,
                ResizeMode = ResizeMode.NoResize,
                ShowInTaskbar = false,
                WindowStartupLocation = WindowStartupLocation.CenterOwner
            };
            var panel = new StackPanel { Margin = new Thickness(15) };
            panel.Children.Add(new TextBlock { Text = "URL（每行一个标签页，第一行为当前页）", Margin = new Thickness(0, 0, 0, 5) });
            var urlBox = new TextBox
            {
                Text = row?.UrlList ?? string.Empty,
                AcceptsReturn = true,
                TextWrapping = TextWrapping.NoWrap,
                Height = 120,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Auto
            };
            panel.Children.Add(urlBox);
            panel.Children.Add(new TextBlock { Text = "实例快捷键（留空不注册）", Margin = new Thickness(0, 10, 0, 5) });
            var hotkeyPanel = new DockPanel();
            var clearHotkey = new Button { Content = "清除", MinWidth = 60, Margin = new Thickness(5, 0, 0, 0) };
            DockPanel.SetDock(clearHotkey, Dock.Right);
            hotkeyPanel.Children.Add(clearHotkey);
            var hotkeyBox = new TextBox
            {
                Text = source.Hotkey,
                IsReadOnly = true,
                IsReadOnlyCaretVisible = false,
                ToolTip = "点击后按下组合键：至少一个修饰键 + A-Z、0-9 或 F1-F24",
                ContextMenu = null
            };
            hotkeyPanel.Children.Add(hotkeyBox);
            panel.Children.Add(hotkeyPanel);
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
                    SystemSounds.Beep.Play();
                    return;
                }
                if (modifiers == ModifierKeys.None)
                {
                    SystemSounds.Beep.Play();
                    return;
                }
                var parts = new List<string>();
                if (modifiers.HasFlag(ModifierKeys.Control)) parts.Add("Ctrl");
                if (modifiers.HasFlag(ModifierKeys.Alt)) parts.Add("Alt");
                if (modifiers.HasFlag(ModifierKeys.Shift)) parts.Add("Shift");
                if (modifiers.HasFlag(ModifierKeys.Windows)) parts.Add("Win");
                parts.Add(keyName);
                hotkeyBox.Text = GlobalHotKeyManager.ParseHotkey(string.Join(" + ", parts), out _, out _);
            };
            clearHotkey.Click += (s, args) => hotkeyBox.Text = string.Empty;
            var iconPanel = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 10, 0, 0) };
            var preview = new Image { Source = IconSettings.LoadImage(source.IconPath), Width = 40, Height = 40, Margin = new Thickness(0, 0, 10, 0) };
            var chooseIcon = new Button { Content = "更换图标..." };
            iconPanel.Children.Add(preview);
            iconPanel.Children.Add(chooseIcon);
            panel.Children.Add(iconPanel);
            string? selectedIcon = null;
            chooseIcon.Click += (s, args) =>
            {
                try
                {
                    string? icon = IconSettings.SelectAndImport(dialog);
                    if (icon == null) return;
                    preview.Source = IconSettings.LoadImage(icon);
                    selectedIcon = icon;
                }
                catch (Exception ex)
                {
                    MessageBox.Show(dialog, $"选择图标失败：{ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            };
            var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 10, 0, 0) };
            var save = new Button { Content = "保存", IsDefault = false, MinWidth = 70 };
            var cancel = new Button { Content = "取消", IsCancel = true, MinWidth = 70, Margin = new Thickness(5, 0, 0, 0) };
            save.Click += (s, args) =>
            {
                try
                {
                    if (!string.Equals(path, displayedPath, StringComparison.OrdinalIgnoreCase))
                        throw new InvalidOperationException("已切换到其他组，请重新打开编辑窗口。");
                    var urls = ParseUrls(urlBox.Text);
                    var replacement = CopyAppGroupEntry(source);
                    replacement.Url = urls[0];
                    replacement.Tabs = urls.Skip(1).ToList();
                    replacement.Hotkey = string.IsNullOrWhiteSpace(hotkeyBox.Text) ? string.Empty
                        : GlobalHotKeyManager.ParseHotkey(hotkeyBox.Text, out _, out _);
                    if (selectedIcon != null) replacement.IconPath = selectedIcon;
                    SaveEntry(index, replacement);
                    dialog.Close();
                }
                catch (Exception ex)
                {
                    MessageBox.Show(dialog, $"保存 App 失败：{ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            };
            cancel.Click += (s, args) => dialog.Close();
            buttons.Children.Add(save);
            buttons.Children.Add(cancel);
            panel.Children.Add(buttons);
            dialog.Content = panel;
            dialog.Loaded += (s, args) => { urlBox.Focus(); urlBox.SelectAll(); };
            dialog.Closed += (s, args) => editDialog = null;
            editDialog = dialog;
            dialog.Show();
        }

        private string? PromptText(string title, string label, string initial)
        {
            var dialog = new Window
            {
                Title = title,
                Owner = this,
                Width = 360,
                SizeToContent = SizeToContent.Height,
                ResizeMode = ResizeMode.NoResize,
                ShowInTaskbar = false,
                WindowStartupLocation = WindowStartupLocation.CenterOwner
            };
            var panel = new StackPanel { Margin = new Thickness(15) };
            panel.Children.Add(new TextBlock { Text = label, Margin = new Thickness(0, 0, 0, 5) });
            var box = new TextBox { Text = initial };
            panel.Children.Add(box);
            var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 10, 0, 0) };
            var ok = new Button { Content = "确定", IsDefault = true, MinWidth = 70 };
            var cancel = new Button { Content = "取消", IsCancel = true, MinWidth = 70, Margin = new Thickness(5, 0, 0, 0) };
            ok.Click += (s, args) => dialog.DialogResult = true;
            buttons.Children.Add(ok);
            buttons.Children.Add(cancel);
            panel.Children.Add(buttons);
            dialog.Content = panel;
            dialog.Loaded += (s, args) => { box.Focus(); box.SelectAll(); };
            return dialog.ShowDialog() == true ? box.Text : null;
        }

        private void ShowAppGroupError(Exception ex)
        {
            MessageBox.Show(this, $"App 组操作失败：{ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }
}