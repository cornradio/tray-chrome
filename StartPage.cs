using System;
using System.IO;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;

namespace TrayChrome
{
    internal static class StartPage
    {
        public const string Url = "https://traychrome.invalid/";

        public static Stream Open()
        {
            return typeof(StartPage).Assembly.GetManifestResourceStream("TrayChrome.Resources.start-page.html")
                ?? throw new InvalidOperationException("内置起始页资源缺失。");
        }
    }

    public partial class MainWindow
    {
        private void InitializeStartPage(WebView2 view)
        {
            var core = view.CoreWebView2;
            core.AddWebResourceRequestedFilter("https://traychrome.invalid/*", CoreWebView2WebResourceContext.All);
            core.WebResourceRequested += (_, e) =>
            {
                var uri = new Uri(e.Request.Uri);
                if (uri.Host != "traychrome.invalid") return;
                e.Response = uri.AbsolutePath == "/"
                    ? core.Environment.CreateWebResourceResponse(StartPage.Open(), 200, "OK",
                        "Content-Type: text/html; charset=utf-8\r\nCache-Control: no-store")
                    : core.Environment.CreateWebResourceResponse(new MemoryStream(Array.Empty<byte>()), 404, "Not Found",
                        "Content-Type: text/plain");
            };
        }
    }
}
