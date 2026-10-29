#nullable enable
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;

namespace TrayChrome
{
    public partial class App
    {
        private bool switchingAppGroup;
        private bool hotKeysSuspended;
        private AppGroupManagerWindow? appGroupManagerWindow;

        // 先释放本实例快捷键，让组内新实例能注册相同组合；成功后退出本实例，失败则恢复快捷键。
        public async Task<bool> LoadAppGroupReplacingCurrentAsync(string path)
        {
            if (switchingAppGroup) return false;
            hotKeysSuspended = true;
            hotKeyManager?.Dispose();
            hotKeyManager = null;
            bool loaded = false;
            try
            {
                loaded = await LoadAppGroupAsync(path);
                if (loaded) Shutdown();
                return loaded;
            }
            finally
            {
                if (!loaded)
                {
                    hotKeysSuspended = false;
                    InitializeGlobalHotKey();
                }
            }
        }

        public AppGroupEntry CaptureCurrentApp()
        {
            if (mainWindow == null) throw new InvalidOperationException("主窗口尚未初始化。");
            if (!mainWindow.TabsReady) throw new InvalidOperationException("标签页尚未初始化完成，请稍后保存。");
            var normalSize = mainWindow.GetNormalWindowSize();
            var position = mainWindow.GetNormalWindowPosition();
            return new AppGroupEntry
            {
                Url = mainWindow.webView.CoreWebView2?.Source ?? mainWindow.AddressBar.Text,
                Tabs = mainWindow.CaptureTabUrls().Where((url, index) => index != mainWindow.ActiveTabIndex).ToList(),
                IconPath = currentIconPath,
                Width = normalSize.Width,
                Height = normalSize.Height,
                Left = position?.X,
                Top = position?.Y,
                IsSuperMinimalMode = mainWindow.IsSuperMinimalMode,
                Hotkey = instanceHotkey ?? (appSettings.EnableGlobalHotKey ? appSettings.Hotkey : string.Empty)
            };
        }

        public void OpenUrlInNewInstance(string url)
        {
            var entry = CaptureCurrentApp();
            entry.Url = url;
            entry.Tabs.Clear();
            // 新窗口不继承快捷键，避免与当前实例冲突；位置交给默认逻辑。
            entry.Hotkey = string.Empty;
            entry.Left = null;
            entry.Top = null;
            using var process = AppGroups.Start(entry, showWindow: true);
        }

        public void RestartCurrentInstance()
        {
            using var process = AppGroups.Start(CaptureCurrentApp(), showWindow: true);
            Shutdown();
        }

        private void AppGroups_SubmenuOpened(object sender, RoutedEventArgs e)
        {
            if (e.Source != sender || sender is not MenuItem menu) return;
            menu.Items.Clear();
            try
            {
                var catalog = AppGroups.LoadCatalog();
                foreach (string path in catalog.Files)
                {
                    var item = new MenuItem
                    {
                        Header = Path.GetFileNameWithoutExtension(path),
                        ToolTip = path,
                        IsCheckable = true,
                        IsChecked = string.Equals(path, catalog.ActiveFile, StringComparison.OrdinalIgnoreCase)
                    };
                    item.Click += async (s, args) => await LoadAppGroupReplacingCurrentAsync(path);
                    menu.Items.Add(item);
                }
                if (menu.Items.Count == 0) menu.Items.Add(new MenuItem { Header = "（暂无 App 组）", IsEnabled = false });
            }
            catch (Exception ex)
            {
                MessageBox.Show($"读取 App 组列表失败: {ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        public AppGroupManagerWindow OpenAppGroupManager()
        {
            if (appGroupManagerWindow == null)
            {
                var manager = new AppGroupManagerWindow();
                appGroupManagerWindow = manager;
                manager.Closed += (_, _) => appGroupManagerWindow = null;
            }
            appGroupManagerWindow.ActivateManager();
            return appGroupManagerWindow;
        }

        private void OpenAppGroupManager_Click(object sender, RoutedEventArgs e)
        {
            try { OpenAppGroupManager(); }
            catch (Exception ex)
            {
                MessageBox.Show($"打开 App 组管理失败: {ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        public string CreateAppGroupShortcut(string path)
        {
            path = Path.GetFullPath(path);
            var group = AppGroups.Load(path);
            if (group.Apps.Count == 0) throw new InvalidDataException("App 组为空，请先保存实例或编辑组文件。");
            string name = string.IsNullOrWhiteSpace(group.Name) ? Path.GetFileNameWithoutExtension(path) : group.Name;
            return CreateDesktopShortcut(string.Empty, $"TrayChrome - {name}", $"--app-group \"{path}\"");
        }

        public async Task<bool> LoadAppGroupAsync(string path, int? entryIndex = null)
        {
            if (switchingAppGroup) return false;
            switchingAppGroup = true;
            var started = new List<Process>();
            try
            {
                var group = AppGroups.Load(path);
                if (group.Apps.Count == 0) throw new InvalidDataException("App 组为空，请先保存实例或编辑组文件。");
                IEnumerable<AppGroupEntry> entries = group.Apps;
                if (entryIndex.HasValue)
                {
                    if (entryIndex.Value < 0 || entryIndex.Value >= group.Apps.Count)
                        throw new InvalidOperationException("条目已发生变化，请重新选择配置。");
                    entries = new[] { group.Apps[entryIndex.Value] };
                }
                foreach (var entry in entries) started.Add(AppGroups.Start(entry));
                await Task.WhenAll(started.Select(AppGroups.WaitForInstanceAsync));
                AppGroups.Remember(path);
                return true;
            }
            catch (Exception ex)
            {
                foreach (var process in started)
                {
                    try
                    {
                        if (!process.HasExited)
                        {
                            // 仅回收本次启动的具体进程，不影响原有实例。
                            process.Kill();
                            await process.WaitForExitAsync();
                        }
                    }
                    catch (Exception cleanupError)
                    {
                        Trace.WriteLine($"清理新实例 {process.Id} 失败: {cleanupError}");
                        MessageBox.Show($"清理新实例 {process.Id} 失败: {cleanupError.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Warning);
                    }
                }
                MessageBox.Show($"加载 App 组失败: {ex.Message}\n原有实例未关闭。",
                    "错误", MessageBoxButton.OK, MessageBoxImage.Error);
                WriteDiagnostic("Load app group failed", ex);
                return false;
            }
            finally
            {
                foreach (var process in started) process.Dispose();
                switchingAppGroup = false;
            }
        }
    }
}
