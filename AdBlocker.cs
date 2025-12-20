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

        private bool MatchesRule(string uri, string host, string path, string rule)
        {
            if (string.IsNullOrWhiteSpace(rule)) return false;

            rule = rule.Trim();

            // 支持简单的通配符匹配
            // 将 AdBlock 规则转换为正则表达式
            string pattern = rule
                .Replace(".", "\\.")
                .Replace("*", ".*")
                .Replace("^", ".*")
                .Replace("|", ".*");

            // 检查域名匹配
            if (rule.StartsWith("||"))
            {
                // ||example.com 匹配 example.com 及其子域名
                string domain = rule.Substring(2).Split('/')[0].Split('^')[0].Split('|')[0];
                if (host == domain || host.EndsWith("." + domain))
                {
                    return true;
                }
            }
            // 检查完整 URL 匹配
            else if (rule.StartsWith("|"))
            {
                // |https://example.com 精确匹配
                string urlPattern = rule.Substring(1);
                if (uri.StartsWith(urlPattern, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }
            // 检查路径匹配
            else if (rule.StartsWith("/") && rule.EndsWith("/"))
            {
                // /regex/ 正则表达式匹配
                try
                {
                    string regexPattern = rule.Substring(1, rule.Length - 2);
                    if (Regex.IsMatch(uri, regexPattern, RegexOptions.IgnoreCase))
                    {
                        return true;
                    }
                }
                catch
                {
                    // 无效的正则表达式
                }
            }
            // 简单字符串匹配
            else
            {
                // 检查域名
                if (host.Contains(rule, StringComparison.OrdinalIgnoreCase) || host.Equals(rule, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
                // 检查完整 URL
                if (uri.Contains(rule, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }

            return false;
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
                "/popup",
                "||ad.*\\.com^",
                "||ads.*\\.com^"
            };
        }
    }
}

