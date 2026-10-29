#nullable enable
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace TrayChrome
{
    public class AppGroupEntry
    {
        public string Url { get; set; } = string.Empty;
        public string IconPath { get; set; } = IconSettings.DefaultPath;
        public double Width { get; set; } = 360;
        public double Height { get; set; } = 640;
        public bool IsSuperMinimalMode { get; set; }
        public string Hotkey { get; set; } = string.Empty;
        public List<string> Tabs { get; set; } = new();
    }

    public class AppGroup
    {
        public string Name { get; set; } = "默认 App 组";
        public List<AppGroupEntry> Apps { get; set; } = new List<AppGroupEntry>();
    }

    public class AppGroupCatalog
    {
        public string ActiveFile { get; set; } = AppGroups.DefaultFile;
        public List<string> Files { get; set; } = new List<string> { AppGroups.DefaultFile };
    }

    public static class AppGroups
    {
        public static string DefaultFile => Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "apps.json");
        private static string CatalogFile => Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "app-groups.json");
        private static readonly JsonSerializerOptions JsonOptions = new JsonSerializerOptions { WriteIndented = true };
        private static readonly string CatalogMutexName = @"Local\TrayChrome.AppGroups."
            + Convert.ToHexString(SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(
                AppDomain.CurrentDomain.BaseDirectory.ToUpperInvariant())));

        public static AppGroupCatalog LoadCatalog()
        {
            return WithCatalogLock(LoadCatalogCore);
        }

        private static T WithCatalogLock<T>(Func<T> action)
        {
            using var mutex = new Mutex(false, CatalogMutexName);
            try
            {
                if (!mutex.WaitOne(TimeSpan.FromSeconds(5)))
                    throw new TimeoutException("其他实例正在更新 App 组索引，请稍后重试。");
            }
            catch (AbandonedMutexException ex)
            {
                Trace.WriteLine($"上次更新 App 组索引的实例已退出: {ex.Message}");
            }
            try { return action(); }
            finally { mutex.ReleaseMutex(); }
        }

        private static AppGroupCatalog LoadCatalogCore()
        {
            if (!File.Exists(CatalogFile))
            {
                if (!File.Exists(DefaultFile)) Save(DefaultFile, new AppGroup());
                var initial = new AppGroupCatalog();
                WriteJson(CatalogFile, initial);
                return initial;
            }
            var catalog = JsonSerializer.Deserialize<AppGroupCatalog>(File.ReadAllText(CatalogFile))
                ?? throw new InvalidDataException("App 组索引文件为空。");
            if (catalog.Files == null || catalog.ActiveFile == null
                || catalog.Files.Any(string.IsNullOrWhiteSpace))
                throw new InvalidDataException("App 组索引文件格式不正确。");
            catalog.Files = catalog.Files.Select(Path.GetFullPath).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            if (!string.IsNullOrWhiteSpace(catalog.ActiveFile))
                catalog.ActiveFile = Path.GetFullPath(catalog.ActiveFile);
            if (!catalog.Files.Contains(catalog.ActiveFile, StringComparer.OrdinalIgnoreCase))
                catalog.ActiveFile = catalog.Files.FirstOrDefault() ?? string.Empty;
            return catalog;
        }

        public static void Remove(string path)
        {
            WithCatalogLock(() =>
            {
                var catalog = LoadCatalogCore();
                path = Path.GetFullPath(path);
                if (!catalog.Files.Contains(path, StringComparer.OrdinalIgnoreCase)
                    || !Path.GetExtension(path).Equals(".json", StringComparison.OrdinalIgnoreCase))
                    throw new InvalidOperationException("只能移除列表中的 JSON 配置文件。");
                File.Delete(path);
                catalog.Files.RemoveAll(file => string.Equals(file, path, StringComparison.OrdinalIgnoreCase));
                if (string.Equals(catalog.ActiveFile, path, StringComparison.OrdinalIgnoreCase))
                    catalog.ActiveFile = catalog.Files.FirstOrDefault() ?? string.Empty;
                WriteJson(CatalogFile, catalog);
                return true;
            });
        }

        public static void Remember(string path)
        {
            WithCatalogLock(() =>
            {
                var catalog = LoadCatalogCore();
                path = Path.GetFullPath(path);
                if (!catalog.Files.Contains(path, StringComparer.OrdinalIgnoreCase)) catalog.Files.Add(path);
                catalog.ActiveFile = path;
                WriteJson(CatalogFile, catalog);
                return true;
            });
        }

        public static AppGroup Load(string path)
        {
            var group = JsonSerializer.Deserialize<AppGroup>(File.ReadAllText(path))
                ?? throw new InvalidDataException("App 组文件为空。");
            Validate(group, path);
            return group;
        }

        public static void Save(string path, AppGroup group)
        {
            Validate(group, path);
            WriteJson(path, group);
        }

        private static void WriteJson<T>(string path, T value)
        {
            path = Path.GetFullPath(path);
            string temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                File.WriteAllText(temporary, JsonSerializer.Serialize(value, JsonOptions));
                File.Move(temporary, path, true);
            }
            finally
            {
                if (File.Exists(temporary)) File.Delete(temporary);
            }
        }

        private static void Validate(AppGroup group, string path)
        {
            if (group.Apps == null) throw new InvalidDataException("App 组缺少 Apps 列表。");
            var hotkeys = new HashSet<ulong>();
            for (int i = 0; i < group.Apps.Count; i++)
            {
                var entry = group.Apps[i] ?? throw new InvalidDataException($"第 {i + 1} 个 App 为空。");
                if (entry.Tabs == null || entry.Tabs.Any(url => !Uri.TryCreate(url, UriKind.Absolute, out var tabUri)
                    || (tabUri.Scheme != Uri.UriSchemeHttp && tabUri.Scheme != Uri.UriSchemeHttps)))
                    throw new InvalidDataException($"第 {i + 1} 个 App 的标签页必须是 HTTP 或 HTTPS 地址。");
                if (!Uri.TryCreate(entry.Url, UriKind.Absolute, out var uri)
                    || (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
                    throw new InvalidDataException($"第 {i + 1} 个 App 的 URL 必须是 HTTP 或 HTTPS 地址。");
                var sizeSettings = new AppSettings { WindowWidth = entry.Width, WindowHeight = entry.Height };
                sizeSettings.NormalizeWindowSize();
                entry.Width = sizeSettings.WindowWidth;
                entry.Height = sizeSettings.WindowHeight;
                if (string.IsNullOrWhiteSpace(entry.IconPath)) entry.IconPath = IconSettings.DefaultPath;
                if (!entry.IconPath.StartsWith("pack://", StringComparison.Ordinal))
                {
                    string directory = Path.GetDirectoryName(Path.GetFullPath(path))
                        ?? throw new InvalidDataException("App 组文件路径不正确。");
                    entry.IconPath = Path.GetFullPath(entry.IconPath, directory);
                }
                IconSettings.ValidatePath(entry.IconPath);
                if (!string.IsNullOrWhiteSpace(entry.Hotkey))
                {
                    entry.Hotkey = GlobalHotKeyManager.ParseHotkey(entry.Hotkey, out uint modifiers, out uint key);
                    if (!hotkeys.Add(((ulong)modifiers << 32) | key))
                        throw new InvalidDataException($"第 {i + 1} 个 App 的快捷键与组内其他条目重复。");
                }
                else entry.Hotkey = string.Empty;
            }
        }

        public static async Task<List<int>> GetInstanceIdsAsync()
        {
            string executable = Environment.ProcessPath ?? throw new InvalidOperationException("无法获取程序路径。");
            var ids = new List<int>();
            foreach (var process in Process.GetProcessesByName(Path.GetFileNameWithoutExtension(executable)))
            {
                using (process)
                {
                    if (process.HasExited) continue;
                    if (string.Equals(process.MainModule?.FileName, executable, StringComparison.OrdinalIgnoreCase))
                        ids.Add(process.Id);
                }
            }
            ids.Sort();
            // 检查所有实例均支持协议，避免导出残缺快照。
            foreach (int id in ids) await InstanceService.RequestAsync(id, "ping");
            return ids;
        }

        public static async Task<AppGroup> CaptureAsync(string name)
        {
            var group = new AppGroup { Name = name };
            foreach (int id in await GetInstanceIdsAsync())
            {
                var response = await InstanceService.RequestAsync(id, "snapshot");
                group.Apps.Add(response.Entry ?? throw new InvalidDataException($"实例 {id} 未返回窗口数据。"));
            }
            return group;
        }

        public static Process Start(AppGroupEntry entry, bool showWindow = false)
        {
            var start = new ProcessStartInfo(Environment.ProcessPath
                ?? throw new InvalidOperationException("无法获取程序路径。")) { UseShellExecute = false };
            start.ArgumentList.Add("--url");
            start.ArgumentList.Add(entry.Url);
            foreach (string tab in entry.Tabs)
            {
                start.ArgumentList.Add("--tab");
                start.ArgumentList.Add(tab);
            }
            start.ArgumentList.Add("--icon");
            start.ArgumentList.Add(entry.IconPath);
            start.ArgumentList.Add("--instance-hotkey");
            start.ArgumentList.Add(entry.Hotkey);
            if (showWindow) start.ArgumentList.Add("--open");
            start.ArgumentList.Add(entry.IsSuperMinimalMode ? "--clean" : "--unclean");
            start.ArgumentList.Add("--size");
            start.ArgumentList.Add(string.Create(CultureInfo.InvariantCulture, $"{entry.Width}x{entry.Height}"));
            return Process.Start(start) ?? throw new InvalidOperationException($"启动 App 失败：{entry.Url}");
        }

        public static async Task WaitForInstanceAsync(Process process)
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            while (true)
            {
                if (process.HasExited) throw new IOException($"新实例 {process.Id} 在初始化完成前退出。");
                var response = await InstanceService.RequestAsync(process.Id, "ping", timeout.Token);
                if (response.Ready) return;
                await Task.Delay(100, timeout.Token);
            }
        }
    }
}
