using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;

namespace TrayChrome
{
    internal static class SiteZoomStore
    {
        private static string FilePath => Path.Combine(AppContext.BaseDirectory, "site-zoom.json");

        public static string Site(string url) =>
            Uri.TryCreate(url, UriKind.Absolute, out var uri)
                && (uri.Scheme == "https" || uri.Scheme == "http") ? uri.IdnHost.ToLowerInvariant() : null;

        private static T Access<T>(Func<Dictionary<string, double>, T> action, bool save)
        {
            string key = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(FilePath.ToUpperInvariant())));
            using var mutex = new Mutex(false, @"Local\TrayChrome.SiteZoom." + key);
            bool acquired;
            try { acquired = mutex.WaitOne(TimeSpan.FromSeconds(3)); }
            catch (AbandonedMutexException) { acquired = true; }
            if (!acquired) throw new IOException("访问网站缩放设置超时。");
            try
            {
                var values = File.Exists(FilePath)
                    ? JsonSerializer.Deserialize<Dictionary<string, double>>(File.ReadAllText(FilePath))
                        ?? throw new InvalidDataException("网站缩放文件为空。")
                    : new Dictionary<string, double>();
                foreach (double value in values.Values)
                    if (!double.IsFinite(value) || value < 0.3 || value > 3)
                        throw new InvalidDataException("网站缩放比例必须在 30%–300% 之间。");
                T result = action(values);
                if (save)
                {
                    string temporary = FilePath + "." + Guid.NewGuid().ToString("N") + ".tmp";
                    try
                    {
                        File.WriteAllText(temporary, JsonSerializer.Serialize(values));
                        File.Move(temporary, FilePath, true);
                    }
                    finally { if (File.Exists(temporary)) File.Delete(temporary); }
                }
                return result;
            }
            finally { mutex.ReleaseMutex(); }
        }

        public static double Read(string url, double defaultZoom)
        {
            string site = Site(url);
            return site == null ? defaultZoom : Access(values => values.TryGetValue(site, out double value) ? value : defaultZoom, false);
        }

        public static void Save(string url, double factor)
        {
            string site = Site(url);
            if (site == null) throw new ArgumentException("当前页面不是可保存缩放的网站。", nameof(url));
            if (!double.IsFinite(factor) || factor < 0.3 || factor > 3) throw new ArgumentOutOfRangeException(nameof(factor));
            Access(values => { values[site] = factor; return true; }, true);
        }
    }
}
