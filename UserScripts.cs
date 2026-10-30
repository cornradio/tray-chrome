#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace TrayChrome
{
    public sealed class UserScript
    {
        public string FileName { get; set; } = string.Empty;
        public string Code { get; set; } = string.Empty;
        public string MetaBlock { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public string Namespace { get; set; } = string.Empty;
        public string Version { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public string RunAt { get; set; } = "document-idle";
        public bool NoFrames { get; set; }
        public bool Enabled { get; set; } = true;
        public List<string> Matches { get; } = new();
        public List<string> Includes { get; } = new();
        public List<string> Excludes { get; } = new();
        public List<string> ExcludeMatches { get; } = new();
        public List<string> Grants { get; } = new();
        public List<string> Requires { get; } = new();
        public List<string> Connects { get; } = new();
        public Dictionary<string, string> Resources { get; } = new();

        public string MatchSummary => string.Join("  ", Matches.Concat(Includes).DefaultIfEmpty("所有网页"));

        public bool HasGrant(string api) => Grants.Any(grant => string.Equals(grant, api, StringComparison.Ordinal));

        // 返回 (正则源码, JS 标志)；include 为空表示匹配所有网页。
        public List<(string Source, string Flags)> IncludePatterns() =>
            Matches.Select(UserScriptPatterns.FromMatch).Concat(Includes.Select(UserScriptPatterns.FromInclude)).ToList();

        public List<(string Source, string Flags)> ExcludePatterns() =>
            ExcludeMatches.Select(UserScriptPatterns.FromMatch).Concat(Excludes.Select(UserScriptPatterns.FromInclude)).ToList();

        public bool AppliesTo(string url)
        {
            bool Test(List<(string Source, string Flags)> patterns) => patterns.Any(pattern =>
            {
                try
                {
                    var options = pattern.Flags.Contains('i') ? RegexOptions.IgnoreCase : RegexOptions.None;
                    return Regex.IsMatch(url, pattern.Source, options, TimeSpan.FromMilliseconds(200));
                }
                catch (Exception) { return false; }
            });
            var includes = IncludePatterns();
            return (includes.Count == 0 || Test(includes)) && !Test(ExcludePatterns());
        }
    }

    public static class UserScriptPatterns
    {
        private static readonly Regex MatchPattern = new(@"^(\*|https?|file|ftp|wss?)://(\*|\*\.[^/*]+|[^/*]*)(/.*)?$", RegexOptions.Compiled);

        public static (string Source, string Flags) FromMatch(string pattern)
        {
            if (pattern == "<all_urls>") return ("^(https?|file|ftp)://", "");
            var match = MatchPattern.Match(pattern);
            if (!match.Success) return FromInclude(pattern);
            string scheme = match.Groups[1].Value == "*" ? "https?" : Regex.Escape(match.Groups[1].Value);
            string host = match.Groups[2].Value;
            string hostSource = host == "*" ? "[^/]*"
                : host.StartsWith("*.", StringComparison.Ordinal) ? @"([^/]*\.)?" + Regex.Escape(host[2..])
                : Regex.Escape(host);
            if (host != "*" && !host.Contains(':')) hostSource += @"(:\d+)?";
            string path = match.Groups[3].Success ? match.Groups[3].Value : "/";
            return ("^" + scheme + "://" + hostSource + Glob(path) + "(#.*)?$", "");
        }

        public static (string Source, string Flags) FromInclude(string pattern)
        {
            if (pattern.Length > 2 && pattern.StartsWith('/') && pattern.LastIndexOf('/') > 0)
            {
                int end = pattern.LastIndexOf('/');
                string flags = pattern[(end + 1)..].Contains('i') ? "i" : "";
                return (pattern[1..end], flags);
            }
            return ("^" + Glob(pattern).Replace(@"\.tld", @"\.[a-z]{2,}(\.[a-z]{2,})?") + "$", "i");
        }

        private static string Glob(string value) => Regex.Escape(value).Replace(@"\*", ".*").Replace(@"\?", @"\?");
    }

    public static class UserScriptStore
    {
        public const long MaxScriptBytes = 5 * 1024 * 1024;
        private const long MaxDependencyBytes = 10 * 1024 * 1024;
        private static readonly object fileLock = new();
        private static readonly HttpClient http = CreateHttpClient();

        public static string Folder => Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "userscripts");
        public static string CacheFolder => Path.Combine(Folder, ".cache");
        private static string ValuesFolder => Path.Combine(Folder, ".values");
        private static string StateFile => Path.Combine(Folder, ".state.json");

        public const string Template = """
            // ==UserScript==
            // @name         新脚本
            // @namespace    traychrome
            // @version      0.1
            // @description
            // @match        https://example.com/*
            // @grant        none
            // ==/UserScript==

            (function () {
                'use strict';

            })();
            """;

        private static HttpClient CreateHttpClient()
        {
            var client = new HttpClient(new HttpClientHandler { AutomaticDecompression = DecompressionMethods.All }) { Timeout = TimeSpan.FromSeconds(30) };
            client.DefaultRequestHeaders.UserAgent.ParseAdd("Mozilla/5.0 TrayChrome-UserScript");
            return client;
        }

        public static void EnsureFolder() => Directory.CreateDirectory(Folder);

        public static UserScript Parse(string code, string fileName = "")
        {
            var block = Regex.Match(code, @"//\s*==UserScript==(.*?)//\s*==/UserScript==", RegexOptions.Singleline);
            if (!block.Success) throw new InvalidDataException("缺少 // ==UserScript== ... // ==/UserScript== 元数据块。");
            var script = new UserScript { Code = code, FileName = fileName, MetaBlock = block.Value };
            string? localizedName = null;
            foreach (string rawLine in block.Groups[1].Value.Split('\n'))
            {
                var line = Regex.Match(rawLine, @"^\s*//\s*@([\w:.-]+)(?:\s+(.*?))?\s*$");
                if (!line.Success) continue;
                string key = line.Groups[1].Value;
                string value = line.Groups[2].Value.Trim();
                switch (key.ToLowerInvariant())
                {
                    case "name": script.Name = value; break;
                    case "name:zh-cn": case "name:zh": localizedName ??= value; break;
                    case "namespace": script.Namespace = value; break;
                    case "version": script.Version = value; break;
                    case "description": if (script.Description.Length == 0) script.Description = value; break;
                    case "match": if (value.Length > 0) script.Matches.Add(value); break;
                    case "include": if (value.Length > 0) script.Includes.Add(value); break;
                    case "exclude": if (value.Length > 0) script.Excludes.Add(value); break;
                    case "exclude-match": if (value.Length > 0) script.ExcludeMatches.Add(value); break;
                    case "grant": if (value.Length > 0) script.Grants.Add(value); break;
                    case "require": if (value.Length > 0) script.Requires.Add(value); break;
                    case "connect": if (value.Length > 0) script.Connects.Add(value); break;
                    case "noframes": script.NoFrames = true; break;
                    case "run-at": script.RunAt = value.ToLowerInvariant() switch
                    {
                        "document-start" => "document-start",
                        "document-end" or "document-body" => "document-end",
                        _ => "document-idle"
                    }; break;
                    case "resource":
                        var resource = Regex.Match(value, @"^(\S+)\s+(\S+)$");
                        if (resource.Success) script.Resources[resource.Groups[1].Value] = resource.Groups[2].Value;
                        break;
                }
            }
            if (localizedName != null) script.Name = localizedName;
            if (string.IsNullOrWhiteSpace(script.Name))
                script.Name = string.IsNullOrEmpty(fileName) ? "未命名脚本" : fileName.Replace(".user.js", "", StringComparison.OrdinalIgnoreCase);
            return script;
        }

        public static List<UserScript> LoadAll()
        {
            EnsureFolder();
            var disabled = LoadDisabled();
            var scripts = new List<UserScript>();
            foreach (string path in Directory.GetFiles(Folder, "*.user.js").OrderBy(path => path, StringComparer.OrdinalIgnoreCase))
            {
                string fileName = Path.GetFileName(path);
                try
                {
                    var script = Parse(File.ReadAllText(path), fileName);
                    script.Enabled = !disabled.Contains(fileName);
                    scripts.Add(script);
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"读取脚本 {fileName} 失败: {ex.Message}");
                }
            }
            return scripts;
        }

        private static HashSet<string> LoadDisabled()
        {
            try
            {
                if (!File.Exists(StateFile)) return new(StringComparer.OrdinalIgnoreCase);
                var state = JsonSerializer.Deserialize<ScriptState>(File.ReadAllText(StateFile));
                return new(state?.Disabled ?? new(), StringComparer.OrdinalIgnoreCase);
            }
            catch (Exception) { return new(StringComparer.OrdinalIgnoreCase); }
        }

        public static void SetEnabled(string fileName, bool enabled)
        {
            lock (fileLock)
            {
                var disabled = LoadDisabled();
                if (enabled) disabled.Remove(fileName); else disabled.Add(fileName);
                EnsureFolder();
                File.WriteAllText(StateFile, JsonSerializer.Serialize(new ScriptState { Disabled = disabled.OrderBy(name => name).ToList() }));
            }
        }

        // 保存脚本：editingFileName 为空时按 @name 生成新文件名（同名同命名空间的已有脚本会被覆盖）。
        public static UserScript Save(string code, string? editingFileName = null)
        {
            var script = Parse(code);
            if (Encoding.UTF8.GetByteCount(code) > MaxScriptBytes) throw new InvalidDataException("脚本超过 5MB。");
            EnsureFolder();
            string fileName = editingFileName ?? LoadAll().FirstOrDefault(existing =>
                existing.Name == script.Name && existing.Namespace == script.Namespace)?.FileName ?? UniqueFileName(script.Name);
            File.WriteAllText(Path.Combine(Folder, fileName), code, new UTF8Encoding(false));
            script.FileName = fileName;
            script.Enabled = !LoadDisabled().Contains(fileName);
            return script;
        }

        private static string UniqueFileName(string name)
        {
            string safe = new string(name.Select(ch => Path.GetInvalidFileNameChars().Contains(ch) ? '_' : ch).ToArray()).Trim().TrimEnd('.');
            if (safe.Length == 0) safe = "script";
            if (safe.Length > 60) safe = safe[..60];
            string fileName = safe + ".user.js";
            for (int i = 2; File.Exists(Path.Combine(Folder, fileName)); i++) fileName = $"{safe} ({i}).user.js";
            return fileName;
        }

        public static void Delete(string fileName)
        {
            string path = Path.Combine(Folder, Path.GetFileName(fileName));
            if (File.Exists(path))
                Microsoft.VisualBasic.FileIO.FileSystem.DeleteFile(path,
                    Microsoft.VisualBasic.FileIO.UIOption.OnlyErrorDialogs, Microsoft.VisualBasic.FileIO.RecycleOption.SendToRecycleBin);
            string values = ValuesPath(fileName);
            if (File.Exists(values)) File.Delete(values);
            SetEnabled(fileName, true);
        }

        public static async Task<string> DownloadScriptAsync(string url)
        {
            if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
                throw new InvalidDataException("只支持 HTTP/HTTPS 地址。");
            var bytes = await DownloadAsync(uri.AbsoluteUri, MaxScriptBytes);
            return Encoding.UTF8.GetString(bytes.Data).TrimStart('\uFEFF');
        }

        // 下载 @require / @resource 到本地缓存；返回失败信息（为空表示全部成功）。
        public static async Task<List<string>> EnsureDependenciesAsync(UserScript script)
        {
            var errors = new List<string>();
            foreach (string url in script.Requires.Concat(script.Resources.Values).Distinct())
            {
                if (File.Exists(CachePath(url))) continue;
                try
                {
                    var download = await DownloadAsync(url, MaxDependencyBytes);
                    Directory.CreateDirectory(CacheFolder);
                    File.WriteAllBytes(CachePath(url), download.Data);
                    File.WriteAllText(CachePath(url) + ".type", download.ContentType);
                }
                catch (Exception ex) { errors.Add($"{url}：{ex.Message}"); }
            }
            return errors;
        }

        public static bool DependenciesCached(UserScript script) =>
            script.Requires.Concat(script.Resources.Values).All(url => File.Exists(CachePath(url)));

        public static string? ReadRequire(string url) =>
            File.Exists(CachePath(url)) ? File.ReadAllText(CachePath(url)).TrimStart('\uFEFF') : null;

        public static (string Mime, byte[] Data)? ReadResource(string url)
        {
            string path = CachePath(url);
            if (!File.Exists(path)) return null;
            string mime = File.Exists(path + ".type") ? File.ReadAllText(path + ".type") : "application/octet-stream";
            return (mime, File.ReadAllBytes(path));
        }

        private static string CachePath(string url) =>
            Path.Combine(CacheFolder, Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(url))).ToLowerInvariant());

        private static async Task<(byte[] Data, string ContentType)> DownloadAsync(string url, long limit)
        {
            using var response = await http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead);
            response.EnsureSuccessStatusCode();
            if (response.Content.Headers.ContentLength > limit) throw new InvalidDataException("文件过大。");
            await using var stream = await response.Content.ReadAsStreamAsync();
            using var memory = new MemoryStream();
            var buffer = new byte[81920];
            int read;
            while ((read = await stream.ReadAsync(buffer)) > 0)
            {
                memory.Write(buffer, 0, read);
                if (memory.Length > limit) throw new InvalidDataException("文件过大。");
            }
            return (memory.ToArray(), response.Content.Headers.ContentType?.MediaType ?? "application/octet-stream");
        }

        private static string ValuesPath(string fileName) => Path.Combine(ValuesFolder, Path.GetFileName(fileName) + ".json");

        public static Dictionary<string, JsonElement> LoadValues(string fileName)
        {
            try
            {
                string path = ValuesPath(fileName);
                if (!File.Exists(path)) return new();
                return JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(File.ReadAllText(path)) ?? new();
            }
            catch (Exception) { return new(); }
        }

        public static void SaveValues(string fileName, Dictionary<string, JsonElement> values)
        {
            lock (fileLock)
            {
                Directory.CreateDirectory(ValuesFolder);
                string path = ValuesPath(fileName);
                if (values.Count == 0) { if (File.Exists(path)) File.Delete(path); return; }
                File.WriteAllText(path, JsonSerializer.Serialize(values));
            }
        }

        private sealed class ScriptState
        {
            public List<string> Disabled { get; set; } = new();
        }
    }

    // 每个窗口一份：生成注入网页的脚本，并处理网页发回的 GM_* 请求。
    public sealed class UserScriptRuntime : IDisposable
    {
        public static readonly string[] ApiNames =
        {
            "GM_info", "GM", "unsafeWindow", "GM_addStyle", "GM_addElement", "GM_getValue", "GM_setValue",
            "GM_deleteValue", "GM_listValues", "GM_xmlhttpRequest", "GM_openInTab", "GM_setClipboard",
            "GM_notification", "GM_registerMenuCommand", "GM_unregisterMenuCommand", "GM_getResourceText",
            "GM_getResourceURL", "GM_log"
        };

        private const long MaxResponseBytes = 50 * 1024 * 1024;
        private static readonly JsonSerializerOptions jsonOptions = new() { Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping };
        private static readonly HttpClient xhrClient = new(new HttpClientHandler
        {
            UseCookies = false,
            AutomaticDecompression = DecompressionMethods.All,
            AllowAutoRedirect = true
        }) { Timeout = Timeout.InfiniteTimeSpan };

        private readonly string token = RandomId();
        private readonly string reply = RandomId();
        private readonly string key = "__tc_" + RandomId();
        private readonly Dictionary<string, Dictionary<string, JsonElement>> values = new();
        private readonly HashSet<string> dirtyValues = new();
        private readonly Dictionary<string, CancellationTokenSource> requests = new();
        private List<UserScript> scripts = new();
        private bool downloadingDependencies;

        public IReadOnlyList<UserScript> Scripts => scripts;
        public event Action? ValuesChanged;
        public event Action? DependenciesReady;

        public void Reload()
        {
            FlushValues();
            scripts = UserScriptStore.LoadAll().Where(script => script.Enabled).ToList();
            values.Clear();
            foreach (var script in scripts) values[script.FileName] = UserScriptStore.LoadValues(script.FileName);
            if (!downloadingDependencies && scripts.Any(script => !UserScriptStore.DependenciesCached(script)))
                _ = DownloadMissingDependenciesAsync();
        }

        private async Task DownloadMissingDependenciesAsync()
        {
            downloadingDependencies = true;
            try
            {
                foreach (var script in scripts.ToList()) await UserScriptStore.EnsureDependenciesAsync(script);
            }
            finally { downloadingDependencies = false; }
            DependenciesReady?.Invoke();
        }

        public void FlushValues()
        {
            foreach (string fileName in dirtyValues.ToList())
            {
                try { if (values.TryGetValue(fileName, out var map)) UserScriptStore.SaveValues(fileName, map); }
                catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"保存脚本数据失败: {ex.Message}"); }
            }
            dirtyValues.Clear();
        }

        // 第一段建立通信桥并放在页面无法访问的闭包里；每个脚本单独注入，语法错误互不影响；最后一段删除临时入口。
        public List<string> BuildDocumentScripts()
        {
            var result = new List<string>();
            if (scripts.Count == 0) return result;
            result.Add(BuildBridge());
            foreach (var script in scripts) result.Add(BuildScript(script));
            result.Add($"try {{ delete window[{Js(key)}]; }} catch (e) {{}}");
            return result;
        }

        private string BuildScript(UserScript script)
        {
            var resources = new Dictionary<string, object>();
            foreach (var pair in script.Resources)
            {
                var resource = UserScriptStore.ReadResource(pair.Value);
                if (resource != null) resources[pair.Key] = new { mime = resource.Value.Mime, base64 = Convert.ToBase64String(resource.Value.Data) };
            }
            var meta = new
            {
                id = script.FileName,
                name = script.Name,
                @namespace = script.Namespace,
                version = script.Version,
                description = script.Description,
                runAt = script.RunAt,
                noframes = script.NoFrames,
                include = script.IncludePatterns().Select(pattern => new[] { pattern.Source, pattern.Flags }),
                exclude = script.ExcludePatterns().Select(pattern => new[] { pattern.Source, pattern.Flags }),
                matches = script.Matches,
                includes = script.Includes,
                excludes = script.Excludes,
                grant = script.Grants,
                metaStr = script.MetaBlock,
                values = values.TryGetValue(script.FileName, out var map) ? map : new Dictionary<string, JsonElement>(),
                resources
            };
            var code = new StringBuilder();
            foreach (string url in script.Requires) code.Append(UserScriptStore.ReadRequire(url) ?? $"console.warn('[TrayChrome 脚本] 未能加载 @require {url.Replace("'", "\\'")}');").Append("\n;\n");
            code.Append(script.Code);
            return $"(() => {{ const register = window[{Js(key)}]; if (typeof register !== 'function') return;\nregister({JsonSerializer.Serialize(meta, jsonOptions)}, function ({string.Join(", ", ApiNames)}) {{\n{code}\n}}); }})();";
        }

        private string BuildBridge() => """
            (() => {
              'use strict';
              const KEY = __KEY__, TOKEN = __TOKEN__, REPLY = __REPLY__;
              const view = window.chrome && window.chrome.webview;
              const post = view ? view.postMessage.bind(view) : null;
              const stringify = JSON.stringify, parse = JSON.parse, hasOwn = Object.prototype.hasOwnProperty;
              const isTop = (() => { try { return window.top === window; } catch (e) { return false; } })();
              const xhrs = new Map(), menus = new Map();
              const rnd = () => Math.random().toString(36).slice(2) + Date.now().toString(36);
              if (view) view.addEventListener('message', event => {
                const m = event.data;
                if (!m || m.tcus !== REPLY) return;
                if (m.type === 'xhr') { const done = xhrs.get(m.id); if (done) { xhrs.delete(m.id); done(m); } }
                else if (m.type === 'menu') { const run = menus.get(m.id); if (run) { try { run(); } catch (e) { console.error(e); } } }
              });
              const send = (script, type, data) => {
                if (!post) throw new Error('TrayChrome 脚本通道在此框架不可用');
                post({ ...data, tcus: TOKEN, script, type });
              };
              const test = (list, href) => list.some(([source, flags]) => { try { return new RegExp(source, flags).test(href); } catch (e) { return false; } });
              const decode = base64 => { const bin = atob(base64); const bytes = new Uint8Array(bin.length); for (let i = 0; i < bin.length; i++) bytes[i] = bin.charCodeAt(i); return bytes; };
              const clone = value => value === undefined ? undefined : parse(stringify(value));

              const makeApi = meta => {
                const id = meta.id, values = meta.values || {};
                const GM_info = {
                  script: { name: meta.name, namespace: meta.namespace, version: meta.version, description: meta.description,
                    matches: meta.matches, includes: meta.includes, excludes: meta.excludes, grant: meta.grant, 'run-at': meta.runAt,
                    resources: Object.keys(meta.resources || {}).map(name => ({ name })) },
                  scriptMetaStr: meta.metaStr, scriptHandler: 'TrayChrome', version: '1.0'
                };
                const GM_getValue = (key, fallback) => hasOwn.call(values, key) ? clone(values[key]) : fallback;
                const GM_deleteValue = key => { key = String(key); delete values[key]; send(id, 'deleteValue', { key }); };
                const GM_setValue = (key, value) => {
                  key = String(key);
                  if (value === undefined) return GM_deleteValue(key);
                  const json = stringify(value);
                  values[key] = parse(json);
                  send(id, 'setValue', { key, value: json });
                };
                const GM_listValues = () => Object.keys(values);
                const GM_addStyle = css => {
                  const style = document.createElement('style');
                  style.textContent = String(css);
                  (document.head || document.documentElement).appendChild(style);
                  return style;
                };
                const GM_addElement = (a, b, c) => {
                  let parent = a, tag = b, attrs = c;
                  if (typeof a === 'string') { parent = null; tag = a; attrs = b; }
                  const element = document.createElement(tag);
                  for (const [name, value] of Object.entries(attrs || {})) {
                    if (name === 'textContent') element.textContent = value; else element.setAttribute(name, value);
                  }
                  const head = ['script', 'style', 'link', 'meta'].includes(String(tag).toLowerCase());
                  (parent || (head ? (document.head || document.documentElement) : (document.body || document.documentElement))).appendChild(element);
                  return element;
                };
                const GM_xmlhttpRequest = details => {
                  details = details || {};
                  const requestId = rnd();
                  let finished = false;
                  const fire = (name, response) => {
                    const handler = details[name];
                    if (typeof handler === 'function') { try { handler.call(details, response); } catch (e) { console.error(e); } }
                  };
                  const responseType = details.responseType || '';
                  xhrs.set(requestId, m => {
                    if (finished) return;
                    finished = true;
                    if (m.error) {
                      const failure = { error: m.error, status: 0, statusText: '', readyState: 4, responseHeaders: '', finalUrl: details.url, context: details.context };
                      fire(m.timeout ? 'ontimeout' : 'onerror', failure);
                      fire('onloadend', failure);
                      return;
                    }
                    let response = m.text;
                    if (m.base64 != null) {
                      const bytes = decode(m.base64);
                      response = responseType === 'arraybuffer' ? bytes.buffer : new Blob([bytes], { type: m.contentType || '' });
                    } else if (responseType === 'json') {
                      try { response = parse(m.text); } catch (e) { response = undefined; }
                    } else if (responseType === 'document') {
                      try { response = new DOMParser().parseFromString(m.text, /xml/.test(m.contentType || '') ? 'text/xml' : 'text/html'); } catch (e) {}
                    }
                    const result = {
                      status: m.status, statusText: m.statusText, responseHeaders: m.headers, finalUrl: m.finalUrl,
                      readyState: 4, response, responseText: m.base64 != null ? undefined : m.text, context: details.context
                    };
                    if (m.base64 == null && /xml/.test(m.contentType || '')) {
                      try { result.responseXML = new DOMParser().parseFromString(m.text, 'text/xml'); } catch (e) {}
                    }
                    fire('onreadystatechange', result);
                    fire('onload', result);
                    fire('onloadend', result);
                  });
                  let data = details.data;
                  if (data instanceof URLSearchParams) data = data.toString();
                  else if (data != null && typeof data !== 'string') { try { data = stringify(data); } catch (e) { data = String(data); } }
                  const headers = {};
                  for (const [name, value] of Object.entries(details.headers || {})) headers[name] = String(value);
                  try {
                    send(id, 'xhr', {
                      id: requestId, method: String(details.method || 'GET').toUpperCase(),
                      url: new URL(String(details.url), location.href).href, headers,
                      data: data == null ? null : String(data), binary: responseType === 'arraybuffer' || responseType === 'blob',
                      timeout: Number(details.timeout) || 0, anonymous: !!details.anonymous, overrideMimeType: details.overrideMimeType || null
                    });
                  } catch (e) {
                    setTimeout(() => { const done = xhrs.get(requestId); if (done) { xhrs.delete(requestId); done({ error: String(e && e.message || e) }); } });
                  }
                  return {
                    abort: () => {
                      if (finished) return;
                      finished = true;
                      xhrs.delete(requestId);
                      try { send(id, 'xhrAbort', { id: requestId }); } catch (e) {}
                      fire('onabort', { status: 0, readyState: 4, context: details.context });
                    }
                  };
                };
                const GM_openInTab = (url, options) => {
                  const active = typeof options === 'boolean' ? !options : !!(options && options.active);
                  send(id, 'openTab', { url: new URL(String(url), location.href).href, active });
                  return { close() {}, closed: false };
                };
                const GM_setClipboard = text => send(id, 'clipboard', { text: String(text) });
                const GM_notification = (a, b) => {
                  const details = a && typeof a === 'object' ? a : { text: a, title: b };
                  send(id, 'notify', { title: String(details.title || meta.name), text: String(details.text || '') });
                };
                const GM_registerMenuCommand = (caption, run) => {
                  const menuId = rnd();
                  menus.set(menuId, run);
                  send(id, 'menu', { id: menuId, caption: String(caption) });
                  return menuId;
                };
                const GM_unregisterMenuCommand = menuId => {
                  menus.delete(menuId);
                  try { send(id, 'unmenu', { id: menuId }); } catch (e) {}
                };
                const resource = name => (meta.resources || {})[name];
                const GM_getResourceText = name => { const r = resource(name); return r ? new TextDecoder().decode(decode(r.base64)) : null; };
                const GM_getResourceURL = name => { const r = resource(name); return r ? `data:${r.mime};base64,${r.base64}` : null; };
                const GM_log = (...args) => console.log(`[${meta.name}]`, ...args);
                const promised = fn => (...args) => new Promise((resolve, reject) => { try { resolve(fn(...args)); } catch (e) { reject(e); } });
                const GM = {
                  info: GM_info,
                  getValue: promised(GM_getValue), setValue: promised(GM_setValue), deleteValue: promised(GM_deleteValue),
                  listValues: promised(GM_listValues), addStyle: promised(GM_addStyle), addElement: promised(GM_addElement),
                  openInTab: promised(GM_openInTab), setClipboard: promised(GM_setClipboard), notification: promised(GM_notification),
                  registerMenuCommand: promised(GM_registerMenuCommand), unregisterMenuCommand: promised(GM_unregisterMenuCommand),
                  getResourceUrl: promised(GM_getResourceURL), getResourceURL: promised(GM_getResourceURL), getResourceText: promised(GM_getResourceText),
                  log: GM_log,
                  xmlHttpRequest: details => new Promise((resolve, reject) => {
                    details = details || {};
                    GM_xmlhttpRequest({ ...details,
                      onload: r => { if (details.onload) details.onload(r); resolve(r); },
                      onerror: r => { if (details.onerror) details.onerror(r); reject(r); },
                      ontimeout: r => { if (details.ontimeout) details.ontimeout(r); reject(r); },
                      onabort: r => { if (details.onabort) details.onabort(r); reject(r); } });
                  })
                };
                return [GM_info, GM, window, GM_addStyle, GM_addElement, GM_getValue, GM_setValue, GM_deleteValue, GM_listValues,
                  GM_xmlhttpRequest, GM_openInTab, GM_setClipboard, GM_notification, GM_registerMenuCommand, GM_unregisterMenuCommand,
                  GM_getResourceText, GM_getResourceURL, GM_log];
              };

              const register = (meta, fn) => {
                if (meta.noframes && !isTop) return;
                const href = location.href;
                if ((meta.include.length > 0 && !test(meta.include, href)) || test(meta.exclude, href)) return;
                const args = makeApi(meta);
                const run = () => {
                  try { fn.apply(window, args); }
                  catch (e) { console.error(`[TrayChrome 脚本] ${meta.name} 出错:`, e); }
                };
                if (meta.runAt === 'document-start') run();
                else if (document.readyState === 'loading')
                  document.addEventListener('DOMContentLoaded', () => meta.runAt === 'document-idle' ? setTimeout(run, 0) : run(), { once: true });
                else run();
              };
              Object.defineProperty(window, KEY, { value: register, configurable: true });
            })();
            """.Replace("__KEY__", Js(key)).Replace("__TOKEN__", Js(token)).Replace("__REPLY__", Js(reply));

        // 返回 true 表示消息属于脚本通道（无论处理成功与否）。
        public bool TryHandleMessage(string json, string sourceUrl, UserScriptHost host)
        {
            if (!json.Contains(token, StringComparison.Ordinal)) return false;
            JsonElement root;
            try
            {
                using var document = JsonDocument.Parse(json);
                root = document.RootElement.Clone();
            }
            catch (JsonException) { return false; }
            if (root.ValueKind != JsonValueKind.Object || !root.TryGetProperty("tcus", out var marker)
                || marker.ValueKind != JsonValueKind.String || marker.GetString() != token) return false;
            try { HandleMessage(root, sourceUrl, host); }
            catch (Exception ex) { host.Log($"Userscript message failed: {ex.Message}"); }
            return true;
        }

        private void HandleMessage(JsonElement message, string sourceUrl, UserScriptHost host)
        {
            string type = Text(message, "type");
            var script = scripts.FirstOrDefault(item => item.FileName == Text(message, "script"));
            if (script == null || !script.AppliesTo(sourceUrl))
            {
                host.Log($"Userscript request rejected: {type} from {sourceUrl}");
                return;
            }
            switch (type)
            {
                case "setValue":
                    using (var value = JsonDocument.Parse(Text(message, "value")))
                        Values(script)[Text(message, "key")] = value.RootElement.Clone();
                    MarkValuesDirty(script);
                    break;
                case "deleteValue":
                    if (Values(script).Remove(Text(message, "key"))) MarkValuesDirty(script);
                    break;
                case "xhr":
                    _ = RunXhrAsync(script, message, sourceUrl, host);
                    break;
                case "xhrAbort":
                    if (requests.Remove(Text(message, "id"), out var cancel)) cancel.Cancel();
                    break;
                case "openTab":
                    if (IsWebUrl(Text(message, "url"))) host.OpenTab(Text(message, "url"), message.TryGetProperty("active", out var active) && active.ValueKind == JsonValueKind.True);
                    break;
                case "clipboard":
                    host.SetClipboard(Text(message, "text"));
                    break;
                case "notify":
                    host.Notify(Text(message, "title"), Text(message, "text"));
                    break;
                case "menu":
                    host.AddMenuCommand(Text(message, "id"), Text(message, "caption"), script.Name);
                    break;
                case "unmenu":
                    host.RemoveMenuCommand(Text(message, "id"));
                    break;
            }
        }

        public string MenuClickMessage(string menuId) => JsonSerializer.Serialize(new { tcus = reply, type = "menu", id = menuId });

        private Dictionary<string, JsonElement> Values(UserScript script)
        {
            if (!values.TryGetValue(script.FileName, out var map)) values[script.FileName] = map = new();
            return map;
        }

        private void MarkValuesDirty(UserScript script)
        {
            dirtyValues.Add(script.FileName);
            ValuesChanged?.Invoke();
        }

        private async Task RunXhrAsync(UserScript script, JsonElement message, string sourceUrl, UserScriptHost host)
        {
            string requestId = Text(message, "id");
            object result;
            using var cancel = new CancellationTokenSource();
            requests[requestId] = cancel;
            try
            {
                if (!script.HasGrant("GM_xmlhttpRequest") && !script.HasGrant("GM.xmlHttpRequest"))
                    throw new InvalidOperationException("脚本未声明 @grant GM_xmlhttpRequest");
                string url = Text(message, "url");
                if (!IsWebUrl(url)) throw new InvalidOperationException("只支持 HTTP/HTTPS 请求");
                var uri = new Uri(url);
                if (!ConnectAllowed(script, uri, sourceUrl)) throw new InvalidOperationException($"脚本未在 @connect 中声明 {uri.Host}");
                int timeout = message.TryGetProperty("timeout", out var timeoutValue) && timeoutValue.TryGetInt32(out int ms) && ms > 0 ? ms : 0;
                if (timeout > 0) cancel.CancelAfter(timeout);

                using var request = new HttpRequestMessage(new HttpMethod(Text(message, "method") is { Length: > 0 } method ? method : "GET"), uri);
                var contentHeaders = new List<KeyValuePair<string, string>>();
                bool hasCookie = false, hasAgent = false;
                if (message.TryGetProperty("headers", out var headers) && headers.ValueKind == JsonValueKind.Object)
                {
                    foreach (var header in headers.EnumerateObject())
                    {
                        string value = header.Value.ValueKind == JsonValueKind.String ? header.Value.GetString() ?? "" : header.Value.ToString();
                        hasCookie |= header.Name.Equals("Cookie", StringComparison.OrdinalIgnoreCase);
                        hasAgent |= header.Name.Equals("User-Agent", StringComparison.OrdinalIgnoreCase);
                        if (header.Name.StartsWith("Content-", StringComparison.OrdinalIgnoreCase)) contentHeaders.Add(new(header.Name, value));
                        else request.Headers.TryAddWithoutValidation(header.Name, value);
                    }
                }
                if (!hasAgent) request.Headers.TryAddWithoutValidation("User-Agent", host.UserAgent);
                bool anonymous = message.TryGetProperty("anonymous", out var anon) && anon.ValueKind == JsonValueKind.True;
                if (!anonymous && !hasCookie)
                {
                    string cookies = await host.GetCookieHeaderAsync(uri.AbsoluteUri);
                    if (cookies.Length > 0) request.Headers.TryAddWithoutValidation("Cookie", cookies);
                }
                if (message.TryGetProperty("data", out var data) && data.ValueKind == JsonValueKind.String)
                {
                    request.Content = new ByteArrayContent(Encoding.UTF8.GetBytes(data.GetString() ?? ""));
                    if (!contentHeaders.Any(header => header.Key.Equals("Content-Type", StringComparison.OrdinalIgnoreCase)))
                        request.Content.Headers.TryAddWithoutValidation("Content-Type", "text/plain;charset=UTF-8");
                }
                if (contentHeaders.Count > 0)
                {
                    request.Content ??= new ByteArrayContent(Array.Empty<byte>());
                    foreach (var header in contentHeaders) request.Content.Headers.TryAddWithoutValidation(header.Key, header.Value);
                }

                using var response = await xhrClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancel.Token);
                if (response.Content.Headers.ContentLength > MaxResponseBytes) throw new InvalidDataException("响应超过 50MB");
                await using var stream = await response.Content.ReadAsStreamAsync(cancel.Token);
                using var memory = new MemoryStream();
                var buffer = new byte[81920];
                int read;
                while ((read = await stream.ReadAsync(buffer, cancel.Token)) > 0)
                {
                    memory.Write(buffer, 0, read);
                    if (memory.Length > MaxResponseBytes) throw new InvalidDataException("响应超过 50MB");
                }
                var bytes = memory.ToArray();
                string headerText = string.Join("\r\n", response.Headers.Concat(response.Content.Headers)
                    .Select(header => $"{header.Key.ToLowerInvariant()}: {string.Join(", ", header.Value)}"));
                string contentType = response.Content.Headers.ContentType?.ToString() ?? "";
                bool binary = message.TryGetProperty("binary", out var binaryValue) && binaryValue.ValueKind == JsonValueKind.True;
                result = new
                {
                    tcus = reply,
                    type = "xhr",
                    id = requestId,
                    status = (int)response.StatusCode,
                    statusText = response.ReasonPhrase ?? "",
                    headers = headerText,
                    finalUrl = response.RequestMessage?.RequestUri?.AbsoluteUri ?? url,
                    contentType,
                    text = binary ? null : DecodeText(bytes, Text(message, "overrideMimeType") is { Length: > 0 } mime ? mime : contentType),
                    base64 = binary ? Convert.ToBase64String(bytes) : null
                };
            }
            catch (OperationCanceledException) when (cancel.IsCancellationRequested)
            {
                if (!requests.ContainsKey(requestId)) return;
                result = new { tcus = reply, type = "xhr", id = requestId, error = "请求超时", timeout = true };
            }
            catch (Exception ex)
            {
                result = new { tcus = reply, type = "xhr", id = requestId, error = ex.Message };
            }
            finally { requests.Remove(requestId); }
            host.Reply(JsonSerializer.Serialize(result, jsonOptions));
        }

        private static bool ConnectAllowed(UserScript script, Uri target, string sourceUrl)
        {
            if (script.Connects.Count == 0) return true;
            string host = target.Host;
            return script.Connects.Any(connect =>
                connect == "*"
                || (connect == "self" && Uri.TryCreate(sourceUrl, UriKind.Absolute, out var source) && source.Host.Equals(host, StringComparison.OrdinalIgnoreCase))
                || host.Equals(connect, StringComparison.OrdinalIgnoreCase)
                || host.EndsWith("." + connect, StringComparison.OrdinalIgnoreCase)
                || (connect == "localhost" && (host == "127.0.0.1" || host == "[::1]")));
        }

        private static string DecodeText(byte[] bytes, string contentType)
        {
            var encoding = Encoding.UTF8;
            var charset = Regex.Match(contentType, @"charset\s*=\s*""?([\w-]+)", RegexOptions.IgnoreCase);
            if (charset.Success)
            {
                try
                {
                    Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
                    encoding = Encoding.GetEncoding(charset.Groups[1].Value);
                }
                catch (ArgumentException) { }
            }
            return encoding.GetString(bytes).TrimStart('\uFEFF');
        }

        private static bool IsWebUrl(string url) =>
            Uri.TryCreate(url, UriKind.Absolute, out var uri) && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps);

        private static string Text(JsonElement element, string name) =>
            element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() ?? "" : "";

        private static string Js(string value) => JsonSerializer.Serialize(value);

        private static string RandomId() => Convert.ToHexString(RandomNumberGenerator.GetBytes(16)).ToLowerInvariant();

        public void Dispose()
        {
            FlushValues();
            foreach (var cancel in requests.Values) cancel.Cancel();
            requests.Clear();
        }
    }

    // 脚本请求的宿主回调：由消息来源的标签页 / 框架提供。
    public sealed class UserScriptHost
    {
        public Action<string> Reply { get; init; } = _ => { };
        public Action<string, bool> OpenTab { get; init; } = (_, _) => { };
        public Action<string> SetClipboard { get; init; } = _ => { };
        public Action<string, string> Notify { get; init; } = (_, _) => { };
        public Action<string, string, string> AddMenuCommand { get; init; } = (_, _, _) => { };
        public Action<string> RemoveMenuCommand { get; init; } = _ => { };
        public Func<string, Task<string>> GetCookieHeaderAsync { get; init; } = _ => Task.FromResult(string.Empty);
        public Action<string> Log { get; init; } = _ => { };
        public string UserAgent { get; init; } = "Mozilla/5.0";
    }
}
