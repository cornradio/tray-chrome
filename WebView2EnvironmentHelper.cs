using System;
using System.Threading.Tasks;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;

namespace TrayChrome
{
    public static class WebView2EnvironmentHelper
    {
        public static async Task EnsureCoreWebView2WithFallbackAsync(WebView2 webView, AppSettings appSettings)
        {
            var options = new CoreWebView2EnvironmentOptions();
            var optionsType = typeof(CoreWebView2EnvironmentOptions);
            
            // 设置 FluentOverlay 滚动条
            try
            {
                var scrollBarStyleProperty = optionsType.GetProperty("ScrollBarStyle");
                if (scrollBarStyleProperty != null)
                {
                    scrollBarStyleProperty.SetValue(options, Enum.Parse(scrollBarStyleProperty.PropertyType, "FluentOverlay"));
                    System.Diagnostics.Debug.WriteLine("已设置 ScrollBarStyle 为 FluentOverlay");
                }
                else
                {
                    var argsProp = optionsType.GetProperty("AdditionalBrowserArguments");
                    if (argsProp != null)
                    {
                        var currentArgs = argsProp.GetValue(options) as string ?? "";
                        argsProp.SetValue(options, string.IsNullOrEmpty(currentArgs) ? "--enable-features=msEdgeFluentOverlayScrollbar" : currentArgs + " --enable-features=msEdgeFluentOverlayScrollbar");
                        System.Diagnostics.Debug.WriteLine("已使用浏览器标志启用 FluentOverlay 滚动条");
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"设置 FluentOverlay 滚动条失败: {ex.Message}");
            }
            
            // 配置代理
            bool hasProxy = appSettings.IsProxyEnabled && !string.IsNullOrEmpty(appSettings.ProxyServer);
            if (hasProxy)
            {
                try
                {
                    var argsProp = optionsType.GetProperty("AdditionalBrowserArguments");
                    if (argsProp != null)
                    {
                        var currentArgs = argsProp.GetValue(options) as string ?? "";
                        string proxyArgs = $"--proxy-server={appSettings.ProxyServer} --proxy-bypass-list=localhost;127.0.0.1";
                        argsProp.SetValue(options, string.IsNullOrEmpty(currentArgs) ? proxyArgs : currentArgs + " " + proxyArgs);
                        System.Diagnostics.Debug.WriteLine($"已设置启动代理: {appSettings.ProxyServer}");
                    }
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"设置启动代理失败: {ex.Message}");
                }
            }
            
            try 
            {
                var environment = await CoreWebView2Environment.CreateAsync(null, null, options);
                await webView.EnsureCoreWebView2Async(environment);
            }
            catch (Exception ex) when (ex.HResult == unchecked((int)0x8007139F) || ex.Message.Contains("0x8007139F") || ex.Message.Contains("组或资源的状态不是执行请求操作的正确状态"))
            {
                System.Diagnostics.Debug.WriteLine("检测到环境参数冲突(0x8007139F)，尝试使用相反的代理参数连接现有环境...");
                try
                {
                    var fallbackOptions = new CoreWebView2EnvironmentOptions();
                    
                    // 保持 FluentOverlay 滚动条参数一致
                    try
                    {
                        var scrollBarStyleProperty = optionsType.GetProperty("ScrollBarStyle");
                        if (scrollBarStyleProperty != null)
                        {
                            scrollBarStyleProperty.SetValue(fallbackOptions, Enum.Parse(scrollBarStyleProperty.PropertyType, "FluentOverlay"));
                        }
                        else
                        {
                            var argsProp = optionsType.GetProperty("AdditionalBrowserArguments");
                            if (argsProp != null) argsProp.SetValue(fallbackOptions, "--enable-features=msEdgeFluentOverlayScrollbar");
                        }
                    }
                    catch { }

                    // 如果当前有代理，备用无代理；如果当前无代理，备用有代理
                    if (!hasProxy && !string.IsNullOrEmpty(appSettings.ProxyServer))
                    {
                        var argsProp = optionsType.GetProperty("AdditionalBrowserArguments");
                        if (argsProp != null)
                        {
                            var currentArgs = argsProp.GetValue(fallbackOptions) as string ?? "";
                            string proxyArgs = $"--proxy-server={appSettings.ProxyServer} --proxy-bypass-list=localhost;127.0.0.1";
                            argsProp.SetValue(fallbackOptions, string.IsNullOrEmpty(currentArgs) ? proxyArgs : currentArgs + " " + proxyArgs);
                        }
                    }
                    
                    var fallbackEnvironment = await CoreWebView2Environment.CreateAsync(null, null, fallbackOptions);
                    await webView.EnsureCoreWebView2Async(fallbackEnvironment);
                    
                    System.Diagnostics.Debug.WriteLine("已成功连接到现有的 WebView2 实例。注意：由于实例共享，新窗口将使用现有进程的代理配置。");
                }
                catch (Exception fallbackEx)
                {
                    System.Diagnostics.Debug.WriteLine($"备用连接也失败了: {fallbackEx.Message}");
                    throw;
                }
            }
        }
    }
}
