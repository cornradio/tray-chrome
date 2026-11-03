using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace TrayChrome
{
    public sealed class VisitRecord
    {
        public DateTimeOffset VisitedAt { get; set; }
        public string Title { get; set; }
        public string Url { get; set; }
        public string LocalTime => VisitedAt.LocalDateTime.ToString("yyyy-MM-dd HH:mm:ss");
    }

    internal static class VisitHistory
    {
        private static string FilePath => Path.Combine(AppContext.BaseDirectory, "history.json");

        private static Task<T> AccessAsync<T>(Func<List<VisitRecord>, T> action) => Task.Run(() =>
        {
            string key = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(FilePath.ToUpperInvariant())));
            using var mutex = new Mutex(false, @"Local\TrayChrome.History." + key);
            bool acquired;
            try { acquired = mutex.WaitOne(TimeSpan.FromSeconds(10)); }
            catch (AbandonedMutexException) { acquired = true; }
            if (!acquired) throw new IOException("访问历史记录超时，请重试。");
            try
            {
                var records = File.Exists(FilePath)
                    ? JsonSerializer.Deserialize<List<VisitRecord>>(File.ReadAllText(FilePath))
                        ?? throw new InvalidDataException("历史记录文件为空。")
                    : new List<VisitRecord>();
                records.RemoveAll(record => record.VisitedAt < DateTimeOffset.UtcNow.AddDays(-7));
                T result = action(records);
                string temporary = FilePath + "." + Guid.NewGuid().ToString("N") + ".tmp";
                try
                {
                    File.WriteAllText(temporary, JsonSerializer.Serialize(records));
                    File.Move(temporary, FilePath, true);
                }
                finally { if (File.Exists(temporary)) File.Delete(temporary); }
                return result;
            }
            finally { mutex.ReleaseMutex(); }
        });

        public static Task RecordAsync(string url, string title)
        {
            if (!Uri.TryCreate(url, UriKind.Absolute, out var uri)
                || (uri.Scheme != "http" && uri.Scheme != "https") || uri.Host == "traychrome.invalid")
                return Task.CompletedTask;
            return AccessAsync(records =>
            {
                records.Add(new VisitRecord { Url = url, Title = string.IsNullOrWhiteSpace(title) ? url : title,
                    VisitedAt = DateTimeOffset.UtcNow });
                return true;
            });
        }

        public static Task<List<VisitRecord>> ReadAsync() =>
            AccessAsync(records => records.OrderByDescending(record => record.VisitedAt).ToList());

        public static Task ClearAsync() => AccessAsync(records => { records.Clear(); return true; });
    }
}
