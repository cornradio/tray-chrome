using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using Microsoft.Web.WebView2.Core;

namespace TrayChrome
{
    public class AdBlocker
    {
        private List<string> blockRules = new List<string>();
        private List<string> allowRules = new List<string>();
        private bool isEnabled = false;
        private CoreWebView2? webView;
        private bool isFilterAdded = false; // 跟踪过滤器是否已添加

        public bool IsEnabled
        {
            get => isEnabled;
            set
            {
                isEnabled = value;
                UpdateFilters();
            }
        }

        public List<string> BlockRules
        {
            get => blockRules;
            set
            {
                blockRules = value ?? new List<string>();
                UpdateFilters();
            }
        }

        public List<string> AllowRules
        {
            get => allowRules;
            set
            {
                allowRules = value ?? new List<string>();
                UpdateFilters();
            }
        }

        public void Initialize(CoreWebView2 coreWebView2)
        {
            if (coreWebView2 == null) return;
            
            webView = coreWebView2;
            isFilterAdded = false; // 重置状态
            UpdateFilters();
        }

        private void UpdateFilters()
        {
            if (webView == null) return;

            try
            {
                // 移除现有的事件处理器
                webView.WebResourceRequested -= WebView_WebResourceRequested;

                if (isEnabled)
                {
                    // 只有在未添加时才添加过滤器
                    if (!isFilterAdded)
                    {
                        // 添加过滤器以拦截所有资源请求
                        webView.AddWebResourceRequestedFilter("*", CoreWebView2WebResourceContext.All);
                        isFilterAdded = true;
                    }
                    webView.WebResourceRequested += WebView_WebResourceRequested;
                }
                else
                {
                    // 只有在已添加时才移除过滤器
                    if (isFilterAdded)
                    {
                        try
                        {
                            webView.RemoveWebResourceRequestedFilter("*", CoreWebView2WebResourceContext.All);
                        }
                        catch
                        {
                            // 忽略移除过滤器时的错误（可能过滤器已被清除）
                        }
                        isFilterAdded = false;
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"更新广告拦截过滤器失败: {ex.Message}");
            }
        }

        private void WebView_WebResourceRequested(object? sender, CoreWebView2WebResourceRequestedEventArgs e)
        {
            if (!isEnabled || webView == null) return;

            try
            {
                string uri = e.Request?.Uri;
                if (string.IsNullOrEmpty(uri)) return;

                // 首先检查白名单规则（允许规则优先级更高）
                if (IsAllowed(uri))
                {
                    return; // 允许通过
                }

                // 检查黑名单规则
                if (ShouldBlock(uri))
                {
                    try
                    {
                        e.Response = webView.Environment.CreateWebResourceResponse(null, 204, "No Content", "");
                    }
                    catch
                    {
                        // 忽略创建响应时的错误
                    }
                }
            }
            catch
            {
                // 忽略处理请求时的错误
            }
        }

        private bool ShouldBlock(string uri)
        {
            if (string.IsNullOrEmpty(uri)) return false;

            try
            {
                Uri parsedUri = new Uri(uri);
                string host = parsedUri.Host.ToLower();
                string path = parsedUri.AbsolutePath.ToLower();

                foreach (var rule in blockRules)
                {
                    if (string.IsNullOrWhiteSpace(rule) || rule.TrimStart().StartsWith("!"))
                        continue;

                    if (MatchesRule(uri, host, path, rule))
                    {
                        return true;
                    }
                }
            }
            catch
            {
                // 忽略无效的 URI
            }

            return false;
        }

        private bool IsAllowed(string uri)
        {
            if (string.IsNullOrEmpty(uri)) return false;

            try
            {
                Uri parsedUri = new Uri(uri);
                string host = parsedUri.Host.ToLower();
                string path = parsedUri.AbsolutePath.ToLower();

                foreach (var rule in allowRules)
                {
                    if (string.IsNullOrWhiteSpace(rule))
                        continue;

                    if (MatchesRule(uri, host, path, rule))
                    {
                        return true;
                    }
                }
            }
            catch
            {
                // 忽略无效的 URI
            }

            return false;
        }

        private static readonly Dictionary<string, Regex> ruleRegexCache = new Dictionary<string, Regex>();

        private bool MatchesRule(string uri, string host, string path, string rule)
        {
            if (string.IsNullOrWhiteSpace(rule)) return false;

            Regex regex = GetRuleRegex(rule.Trim());
            return regex != null && regex.IsMatch(uri);
        }

        internal static Regex GetRuleRegex(string rule)
        {
            lock (ruleRegexCache)
            {
                if (!ruleRegexCache.TryGetValue(rule, out Regex regex))
                {
                    regex = BuildRuleRegex(rule);
                    ruleRegexCache[rule] = regex;
                }
                return regex;
            }
        }

        // AdBlock 语法：||域名、|开头/结尾锚点、* 通配符、^ 分隔符；/.../ 只有包含正则元字符时才视为正则
        private static Regex BuildRuleRegex(string rule)
        {
            int optionsIndex = rule.IndexOf('$');
            if (optionsIndex > 0 && !IsRegexRule(rule))
            {
                rule = rule.Substring(0, optionsIndex);
            }
            if (rule.Length == 0) return null;

            try
            {
                if (IsRegexRule(rule))
                {
                    return new Regex(rule.Substring(1, rule.Length - 2), RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
                }

                var pattern = new System.Text.StringBuilder();
                int index = 0;
                if (rule.StartsWith("||"))
                {
                    pattern.Append(@"^[a-z][a-z0-9+.-]*://([^/?#]*\.)?");
                    index = 2;
                }
                else if (rule.StartsWith("|"))
                {
                    pattern.Append('^');
                    index = 1;
                }

                bool anchorEnd = rule.Length > index && rule.EndsWith("|");
                int endIndex = anchorEnd ? rule.Length - 1 : rule.Length;
                for (int i = index; i < endIndex; i++)
                {
                    char ch = rule[i];
                    switch (ch)
                    {
                        case '*':
                            pattern.Append(".*");
                            break;
                        case '^':
                            pattern.Append(@"(?:[^\w.%-]|$)");
                            break;
                        default:
                            pattern.Append(Regex.Escape(ch.ToString()));
                            break;
                    }
                }
                if (anchorEnd) pattern.Append('$');

                return new Regex(pattern.ToString(), RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
            }
            catch
            {
                return null;
            }
        }

        private static bool IsRegexRule(string rule)
        {
            return rule.Length > 2 && rule.StartsWith("/") && rule.EndsWith("/")
                && rule.Substring(1, rule.Length - 2).IndexOfAny(@"\^$*+?()[]{}|.".ToCharArray()) >= 0;
        }
        public void LoadDefaultRules()
        {
            // 加载一些常见的广告域名规则
            blockRules = new List<string>
            {
                "||doubleclick.net^",
                "||googleadservices.com^",
                "||googlesyndication.com^",
                "||google-analytics.com^",
                "||facebook.com/tr^",
                "||amazon-adsystem.com^",
                "||adservice.google^",
                "||adsafeprotected.com^",
                "||advertising.com^",
                "||adnxs.com^",
                "||adform.net^",
                "/ads/",
                "/advertisement",
                "/banner",
                "/popup"
            };
        }
    }
}

