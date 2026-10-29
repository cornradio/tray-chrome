using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Text.RegularExpressions;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace TrayChrome
{
    public static class BookmarkFolders
    {
        public static string FilePath => Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Bookmarks.chrome.json");

        public static List<Bookmark> Load(string path)
        {
            var document = JsonNode.Parse(File.ReadAllText(path)) ?? throw new InvalidDataException("书签文件为空。");
            var roots = document["roots"]?.AsObject() ?? throw new InvalidDataException("缺少 Chrome roots。");
            Bookmark Read(JsonNode node)
            {
                string type = node["type"]?.GetValue<string>();
                if (type != "url" && type != "folder") throw new InvalidDataException("未知书签类型。");
                var entry = new Bookmark { Title = node["name"]?.GetValue<string>() ?? "", IsFolder = type == "folder" };
                if (entry.IsFolder)
                {
                    var children = node["children"]?.AsArray() ?? throw new InvalidDataException("文件夹缺少 children。");
                    foreach (var child in children) entry.Children.Add(Read(child));
                }
                else entry.Url = node["url"]?.GetValue<string>() ?? throw new InvalidDataException("书签缺少 URL。");
                return entry;
            }
            var result = new List<Bookmark>();
            foreach (var root in roots)
            {
                var folder = Read(root.Value);
                if (!folder.IsFolder) throw new InvalidDataException("Chrome 根节点必须为文件夹。");
                if (root.Key == "bookmark_bar") result.AddRange(folder.Children);
                else if (folder.Children.Count > 0) result.Add(folder);
            }
            return result;
        }

        public static void Save(string path, List<Bookmark> entries)
        {
            long id = 0;
            JsonObject Write(Bookmark entry)
            {
                var node = new JsonObject { ["id"] = (++id).ToString(), ["name"] = entry.Title,
                    ["type"] = entry.IsFolder ? "folder" : "url", ["date_added"] = "0" };
                if (entry.IsFolder)
                {
                    node["date_modified"] = "0";
                    var children = new JsonArray();
                    foreach (var child in entry.Children) children.Add(Write(child));
                    node["children"] = children;
                }
                else node["url"] = entry.Url;
                return node;
            }
            var root = new Bookmark { IsFolder = true, Title = "书签栏", Children = entries };
            var document = new JsonObject { ["version"] = 1, ["roots"] = new JsonObject
                { ["bookmark_bar"] = Write(root), ["other"] = Write(new Bookmark { IsFolder = true, Title = "其他书签" }),
                  ["synced"] = Write(new Bookmark { IsFolder = true, Title = "移动设备书签" }) } };
            string temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                File.WriteAllText(temporary, document.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
                File.Move(temporary, path, true);
            }
            finally { if (File.Exists(temporary)) File.Delete(temporary); }
        }

        public static string ExportHtml(IEnumerable<Bookmark> entries)
        {
            var html = new StringBuilder("<!DOCTYPE NETSCAPE-Bookmark-file-1>\n<META HTTP-EQUIV=\"Content-Type\" CONTENT=\"text/html; charset=UTF-8\">\n<TITLE>Bookmarks</TITLE>\n<H1>Bookmarks</H1>\n<DL><p>\n");
            void Write(IEnumerable<Bookmark> nodes)
            {
                foreach (var entry in nodes)
                {
                    string title = WebUtility.HtmlEncode(entry.Title);
                    if (entry.IsFolder)
                    {
                        html.Append("<DT><H3>").Append(title).Append("</H3>\n<DL><p>\n");
                        Write(entry.Children);
                        html.Append("</DL><p>\n");
                    }
                    else html.Append("<DT><A HREF=\"").Append(WebUtility.HtmlEncode(entry.Url))
                        .Append("\">").Append(title).Append("</A>\n");
                }
            }
            Write(entries);
            return html.Append("</DL><p>\n").ToString();
        }

        public static List<Bookmark> FindParent(List<Bookmark> entries, Bookmark target)
        {
            if (entries.Contains(target)) return entries;
            foreach (var folder in entries.Where(item => item.IsFolder))
            {
                var result = FindParent(folder.Children, target);
                if (result != null) return result;
            }
            return null;
        }

        public static bool Move(List<Bookmark> entries, Bookmark item, Bookmark destination)
        {
            if (destination != null && (!destination.IsFolder || All(new[] { item }).Contains(destination))) return false;
            var source = FindParent(entries, item);
            var target = destination?.Children ?? entries;
            if (source == null || source == target) return false;
            source.Remove(item);
            target.Add(item);
            return true;
        }
        public static IEnumerable<Bookmark> All(IEnumerable<Bookmark> entries)
        {
            foreach (var entry in entries)
            {
                yield return entry;
                foreach (var child in All(entry.Children)) yield return child;
            }
        }

        public static List<Bookmark> ImportHtml(string html) => ImportHtml(html, out _);

        public static List<Bookmark> ImportHtml(string html, out int skipped)
        {
            skipped = 0;
            if (!Regex.IsMatch(html, @"<!DOCTYPE\s+NETSCAPE-Bookmark-file-1|<DL\b",
                RegexOptions.IgnoreCase, TimeSpan.FromSeconds(2)))
                throw new InvalidDataException("请选择浏览器导出的 HTML 书签文件。");
            var root = new List<Bookmark>();
            var stack = new Stack<List<Bookmark>>();
            stack.Push(root);
            Bookmark pending = null;
            foreach (Match token in Regex.Matches(html, @"<H3\b[^>]*>(?<folder>.*?)</H3\s*>|<A\b(?<attrs>[^>]*)>(?<title>.*?)</A\s*>|(?<open><DL\b[^>]*>)|(?<close></DL\s*>)",
                RegexOptions.IgnoreCase | RegexOptions.Singleline, TimeSpan.FromSeconds(5)))
            {
                if (token.Groups["folder"].Success)
                {
                    pending = new Bookmark { Title = Decode(token.Groups["folder"].Value), IsFolder = true };
                    stack.Peek().Add(pending);
                }
                else if (token.Groups["attrs"].Success)
                {
                    var href = Regex.Match(token.Groups["attrs"].Value,
                        "\\bHREF\\s*=\\s*(?:\"(?<url>[^\"]*)\"|'(?<url>[^']*)'|(?<url>[^\\s>]+))",
                        RegexOptions.IgnoreCase, TimeSpan.FromSeconds(2));
                    string url = WebUtility.HtmlDecode(href.Groups["url"].Value);
                    if (!href.Success || !Uri.TryCreate(url, UriKind.Absolute, out var uri)
                        || (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
                    {
                        skipped++;
                        pending = null;
                        continue;
                    }
                    stack.Peek().Add(new Bookmark { Title = Decode(token.Groups["title"].Value), Url = url });
                    pending = null;
                }
                else if (token.Groups["open"].Success)
                {
                    stack.Push(pending?.Children ?? stack.Peek());
                    pending = null;
                }
                else if (stack.Count > 1) stack.Pop();
            }
            if (root.Count == 0 && skipped == 0) throw new InvalidDataException("HTML 文件中没有可导入的书签或文件夹。");
            return root;
        }

        private static string Decode(string text) => WebUtility.HtmlDecode(
            Regex.Replace(text, "<[^>]*>", "", RegexOptions.Singleline, TimeSpan.FromSeconds(2))).Trim();
    }
}
