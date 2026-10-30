using System;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;
using Microsoft.Web.WebView2.Wpf;

namespace TrayChrome
{
    public partial class MainWindow
    {
        private DispatcherTimer pageLoadingSlowTimer;

        private void TrackPageLoading(WebView2 view)
        {
            var tab = FindTab(view);
            var core = view.CoreWebView2;
            core.NavigationStarting += (_, e) =>
            {
                if (e.Cancel) return;
                tab.NavigationId = e.NavigationId;
                tab.IsLoading = true;
                tab.HasPresented = false;
                tab.PaintMessage = null;
                tab.LoadingStartedAt = DateTime.UtcNow;
                tab.LoadingUrl = e.Uri;
                UpdatePageLoadingIndicator();
            };
            core.ContentLoading += (_, e) =>
            {
                if (e.NavigationId == tab.NavigationId) tab.HasContent = true;
            };
            core.DOMContentLoaded += (_, e) =>
            {
                if (e.NavigationId == tab.NavigationId) _ = RequestPagePresentationAsync(tab);
            };
            core.NavigationCompleted += (_, e) =>
            {
                if (e.NavigationId != tab.NavigationId) return;
                tab.HasContent = true;
                tab.IsLoading = false;
                if (e.IsSuccess)
                    _ = RequestPagePresentationAsync(tab);
                else
                {
                    // 浏览器的内置错误页不一定允许执行脚本，直接交给它显示错误。
                    tab.HasPresented = true;
                    tab.PaintMessage = null;
                    (Application.Current as App)?.WriteDiagnostic($"Navigation failed: {e.WebErrorStatus}");
                }
                UpdatePageLoadingIndicator();
            };
            core.WebMessageReceived += (_, e) =>
            {
                if (tab.PaintMessage == null || e.WebMessageAsJson != JsonSerializer.Serialize(tab.PaintMessage))
                    return;
                tab.PaintMessage = null;
                tab.HasPresented = true;
                UpdatePageLoadingIndicator();
            };
        }

        private BrowserTab FindTab(WebView2 view)
        {
            foreach (var tab in tabs)
                if (tab.View == view) return tab;
            return null;
        }

        private void RefreshPagePresentation(BrowserTab tab)
        {
            if (!tab.View.IsVisible)
            {
                tab.PaintMessage = null;
                tab.HasPresented = false;
            }
            else
            {
                tab.LoadingStartedAt = DateTime.UtcNow;
                _ = RequestPagePresentationAsync(tab);
            }
            UpdatePageLoadingIndicator();
        }

        private async Task RequestPagePresentationAsync(BrowserTab tab)
        {
            var core = tab.View.CoreWebView2;
            if (core == null || !tab.HasContent || !tab.View.IsVisible || tab.PaintMessage != null)
                return;
            string message = "traychrome-painted-" + Guid.NewGuid().ToString("N");
            tab.PaintMessage = message;
            try
            {
                // 让 WPF 先画出提示；两个 rAF 之间给浏览器一次绘制机会。
                await Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ContextIdle);
                if (tab.PaintMessage != message || !tab.View.IsVisible) return;
                await core.ExecuteScriptAsync($$"""
                    (() => {
                        const ready = () => requestAnimationFrame(() =>
                            requestAnimationFrame(() => chrome.webview.postMessage({{JsonSerializer.Serialize(message)}})));
                        if (document.readyState === 'loading')
                            document.addEventListener('DOMContentLoaded', ready, { once: true });
                        else ready();
                    })();
                    """);
            }
            catch (Exception ex) when (ex is InvalidOperationException || ex is System.Runtime.InteropServices.COMException)
            {
                if (tab.PaintMessage != message) return;
                tab.PaintMessage = null;
                (Application.Current as App)?.WriteDiagnostic("Unable to confirm page presentation", ex);
                PageLoadingText.Text = "页面显示异常，请刷新";
            }
        }

        private void UpdatePageLoadingIndicator()
        {
            var tab = activeTab;
            if (tab == null) return;
            bool waiting = tab.View.CoreWebView2 == null || tab.IsLoading || !tab.HasPresented;
            PageLoadingPanel.Visibility = waiting ? Visibility.Visible : Visibility.Collapsed;
            PageLoadingText.Text = tab.HasContent && !tab.HasPresented ? "正在显示网页…" : "正在加载…";
            string url = tab.LoadingUrl ?? tab.Url ?? string.Empty;
            PageLoadingHost.Text = Uri.TryCreate(url, UriKind.Absolute, out var uri) && !string.IsNullOrEmpty(uri.Host)
                ? uri.Host : url;
            PageLoadingHost.ToolTip = url;

            bool slow = waiting && DateTime.UtcNow - tab.LoadingStartedAt >= TimeSpan.FromSeconds(5);
            PageLoadingSlowHint.Text = tab.IsLoading ? "网站响应较慢，仍在等待…" : "正在等待网页绘制…";
            PageLoadingSlowHint.Visibility = slow ? Visibility.Visible : Visibility.Collapsed;
            if (waiting && IsVisible && !slow)
            {
                if (pageLoadingSlowTimer == null)
                {
                    pageLoadingSlowTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
                    pageLoadingSlowTimer.Tick += (_, _) => UpdatePageLoadingIndicator();
                    Closed += (_, _) => pageLoadingSlowTimer.Stop();
                }
                pageLoadingSlowTimer.Start();
            }
            else pageLoadingSlowTimer?.Stop();
        }
    }
}
