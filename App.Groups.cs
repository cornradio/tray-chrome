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

        public AppGroupEntry CaptureCurrentApp()
        {
            if (mainWindow == null) throw new InvalidOperationException("主窗口尚未初始化。");
            var normalSize = mainWindow.GetNormalWindowSize();
            return new AppGroupEntry
            {
                Url = mainWindow.webView.CoreWebView2?.Source ?? mainWindow.AddressBar.Text,
                IconPath = currentIconPath,
                Width = normalSize.Width,
                Height = normalSize.Height,
                IsSuperMinimalMode = mainWindow.IsSuperMinimalMode,
                Hotkey = instanceHotkey ?? (appSettings.EnableGlobalHotKey ? appSettings.Hotkey : string.Empty)
            };
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
                    item.Click += async (s, args) => await LoadAppGroupAsync(path);
                    menu.Items.Add(item);
                }
                menu.Items.Add(new Separator());
                var settings = new MenuItem { Header = "管理 App 组..." };
                settings.Click += (s, args) => OpenAppGroupSettings();
                menu.Items.Add(settings);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"读取 App 组列表失败: {ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void OpenAppGroupSettings()
        {
            if (mainWindow == null) return;
            if (!mainWindow.IsLoaded)
            {
                mainWindow.Show();
                mainWindow.Hide();
            }
            var window = new SettingsWindow(appSettings, mainWindow, this);
            window.MainTabControl.SelectedItem = window.AppGroupsTab;
            window.Show();
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
