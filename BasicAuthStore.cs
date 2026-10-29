#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace TrayChrome
{
    // 按站点（协议+主机+端口）保存 Basic Auth 账号，密码使用当前 Windows 用户的 DPAPI 加密。
    public static class BasicAuthStore
    {
        private sealed class Entry
        {
            public string Origin { get; set; } = string.Empty;
            public string UserName { get; set; } = string.Empty;
            public string Password { get; set; } = string.Empty;
        }

        private static readonly byte[] Entropy = Encoding.UTF8.GetBytes("TrayChrome.BasicAuth");
        private static readonly object Sync = new object();
        public static string FilePath => Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "basic-auth.json");

        public static string? GetOrigin(string? uri)
        {
            if (!Uri.TryCreate(uri, UriKind.Absolute, out var parsed)
                || (parsed.Scheme != Uri.UriSchemeHttp && parsed.Scheme != Uri.UriSchemeHttps))
                return null;
            return parsed.GetLeftPart(UriPartial.Authority).ToLowerInvariant();
        }

        public static (string UserName, string Password)? Find(string origin)
        {
            lock (Sync)
            {
                var entry = Load().FirstOrDefault(item => item.Origin == origin);
                if (entry == null) return null;
                try
                {
                    byte[] data = ProtectedData.Unprotect(Convert.FromBase64String(entry.Password), Entropy, DataProtectionScope.CurrentUser);
                    return (entry.UserName, Encoding.UTF8.GetString(data));
                }
                catch (Exception ex) when (ex is CryptographicException || ex is FormatException)
                {
                    return null;
                }
            }
        }

        public static void Save(string origin, string userName, string password)
        {
            lock (Sync)
            {
                var entries = Load();
                entries.RemoveAll(item => item.Origin == origin);
                byte[] data = ProtectedData.Protect(Encoding.UTF8.GetBytes(password), Entropy, DataProtectionScope.CurrentUser);
                entries.Add(new Entry { Origin = origin, UserName = userName, Password = Convert.ToBase64String(data) });
                Write(entries);
            }
        }

        public static bool Remove(string origin)
        {
            lock (Sync)
            {
                var entries = Load();
                if (entries.RemoveAll(item => item.Origin == origin) == 0) return false;
                Write(entries);
                return true;
            }
        }

        private static List<Entry> Load()
        {
            if (!File.Exists(FilePath)) return new List<Entry>();
            try
            {
                return JsonSerializer.Deserialize<List<Entry>>(File.ReadAllText(FilePath)) ?? new List<Entry>();
            }
            catch (JsonException)
            {
                return new List<Entry>();
            }
        }

        private static void Write(List<Entry> entries)
        {
            string temporary = FilePath + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                File.WriteAllText(temporary, JsonSerializer.Serialize(entries, new JsonSerializerOptions { WriteIndented = true }));
                File.Move(temporary, FilePath, true);
            }
            finally
            {
                if (File.Exists(temporary)) File.Delete(temporary);
            }
        }
    }
}
