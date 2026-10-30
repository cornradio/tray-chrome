using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;

namespace TrayChrome
{
    public partial class MainWindow
    {
        private sealed class ScriptMenuCommand
        {
            public string Id { get; init; }
            public string Caption { get; init; }
            public string ScriptName { get; init; }
            public object Owner { get; init; }
            public Action<string> Post { get; init; }
        }

        private UserScriptRuntime userScripts;
        private FileSystemWatcher userScriptWatcher;
        private DispatcherTimer userScriptReloadTimer;
        private DispatcherTimer userScriptValuesTimer;
        private readonly SemaphoreSlim userScriptLock = new(1, 1);

        private UserScriptRuntime UserScripts
        {
            get
            {
                if (userScripts != null) return userScripts;
                userScripts = new UserScriptRuntime();
                try { userScripts.Reload(); }
                catch (Exception ex) { (Application.Current as App)?.WriteDiagnostic("Loading userscripts failed", ex); }
                userScripts.ValuesChanged += () => { userScriptValuesTimer.Stop(); userScriptValuesTimer.Start(); };
                userScripts.DependenciesReady += () => Dispatcher.BeginInvoke(new Action(ApplyUserScriptsToAllTabs));
                userScriptValuesTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(300) };
                userScriptValuesTimer.Tick += (_, _) =>
                {
                    userScriptValuesTimer.Stop();
                    userScripts.FlushValues();
                    ApplyUserScriptsToAllTabs();
                };
                userScriptReloadTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(500) };
                userScriptReloadTimer.Tick += (_, _) =>
                {
                    userScriptReloadTimer.Stop();
                    ReloadUserScripts();
                };
                if (Application.Current != null) Application.Current.Exit += (_, _) => userScripts.Dispose();
                try
                {
                    UserScriptStore.EnsureFolder();
                    userScriptWatcher = new FileSystemWatcher(UserScriptStore.Folder)
                    {
                        IncludeSubdirectories = true,
                        NotifyFilter = NotifyFilters.FileName | NotifyFilters.LastWrite | NotifyFilters.Size
                    };
                    FileSystemEventHandler changed = (_, args) =>
                    {
                        if (args.FullPath.StartsWith(UserScriptStore.CacheFolder, StringComparison.OrdinalIgnoreCase)) return;
                        Dispatcher.BeginInvoke(new Action(() => { userScriptReloadTimer.Stop(); userScriptReloadTimer.Start(); }));
                    };
                    userScriptWatcher.Changed += changed;
                    userScriptWatcher.Created += changed;
                    userScriptWatcher.Deleted += changed;
                    userScriptWatcher.Renamed += (sender, args) => changed(sender, args);
                    userScriptWatcher.EnableRaisingEvents = true;
                }
                catch (Exception ex) { (Application.Current as App)?.WriteDiagnostic("Watching userscripts folder failed", ex); }
                return userScripts;
            }
        }

        // 重新读取脚本并注册到所有标签页；已打开的网页刷新后生效。
        public void ReloadUserScripts()
        {
            try { UserScripts.Reload(); }
            catch (Exception ex) { (Application.Current as App)?.WriteDiagnostic("Reloading userscripts failed", ex); }
            ApplyUserScriptsToAllTabs();
        }

        private async void ApplyUserScriptsToAllTabs()
        {
            foreach (var tab in tabs.ToArray()) await ApplyUserScriptsAsync(tab);
        }

        private async Task ApplyUserScriptsAsync(BrowserTab tab)
        {
            await userScriptLock.WaitAsync();
            try
            {
                var core = tab.View.CoreWebView2;
                if (core == null) return;
                foreach (string id in tab.UserScriptIds) core.RemoveScriptToExecuteOnDocumentCreated(id);
                tab.UserScriptIds.Clear();
                foreach (string script in UserScripts.BuildDocumentScripts())
                    tab.UserScriptIds.Add(await core.AddScriptToExecuteOnDocumentCreatedAsync(script));
            }
            catch (Exception ex)
            {
                (Application.Current as App)?.WriteDiagnostic("Registering userscripts failed", ex);
            }
            finally { userScriptLock.Release(); }
        }

        private bool HandleUserScriptMessage(WebView2 view, CoreWebView2WebMessageReceivedEventArgs args, CoreWebView2Frame frame)
        {
            if (userScripts == null) return false;
            var tab = tabs.FirstOrDefault(item => item.View == view);
            if (tab == null) return false;
            string json;
            try { json = args.WebMessageAsJson; }
            catch (Exception) { return false; }
            object owner = (object)frame ?? view;
            void Post(string message)
            {
                try
                {
                    if (frame != null) { if (frame.IsDestroyed() == 0) frame.PostWebMessageAsJson(message); }
                    else view.CoreWebView2?.PostWebMessageAsJson(message);
                }
                catch (Exception ex) { (Application.Current as App)?.WriteDiagnostic("Userscript reply failed", ex); }
            }
            var host = new UserScriptHost
            {
                Reply = Post,
                OpenTab = OpenTabFromScript,
                SetClipboard = text =>
                {
                    try { Clipboard.SetText(text); }
                    catch (Exception ex) { (Application.Current as App)?.WriteDiagnostic("Userscript clipboard failed", ex); }
                },
                Notify = (title, text) => (Application.Current as App)?.ShowTrayNotification(title, text),
                AddMenuCommand = (id, caption, scriptName) =>
                {
                    tab.ScriptMenus.RemoveAll(command => command.Id == id);
                    tab.ScriptMenus.Add(new ScriptMenuCommand { Id = id, Caption = caption, ScriptName = scriptName, Owner = owner, Post = Post });
                },
                RemoveMenuCommand = id => tab.ScriptMenus.RemoveAll(command => command.Id == id),
                GetCookieHeaderAsync = async url =>
                {
                    if (view.CoreWebView2 == null) return string.Empty;
                    var cookies = await view.CoreWebView2.CookieManager.GetCookiesAsync(url);
                    return string.Join("; ", cookies.Select(cookie => $"{cookie.Name}={cookie.Value}"));
                },
                Log = message => (Application.Current as App)?.WriteDiagnostic(message),
                UserAgent = view.CoreWebView2?.Settings.UserAgent ?? DesktopUA
            };
            return userScripts.TryHandleMessage(json, args.Source, host);
        }

        private void UserScriptNavigationStarting(WebView2 view, CoreWebView2NavigationStartingEventArgs e)
        {
            var tab = tabs.FirstOrDefault(item => item.View == view);
            tab?.ScriptMenus.Clear();
            if (Uri.TryCreate(e.Uri, UriKind.Absolute, out var uri)
                && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps)
                && uri.AbsolutePath.EndsWith(".user.js", StringComparison.OrdinalIgnoreCase))
            {
                e.Cancel = true;
                Dispatcher.BeginInvoke(new Action(() => InstallUserScriptFromUrl(uri.AbsoluteUri)));
            }
        }

        private async void InstallUserScriptFromUrl(string url)
        {
            try
            {
                string code = await UserScriptStore.DownloadScriptAsync(url);
                var script = UserScriptStore.Parse(code);
                var existing = UserScriptStore.LoadAll().FirstOrDefault(item => item.Name == script.Name && item.Namespace == script.Namespace);
                string matches = script.MatchSummary.Length > 300 ? script.MatchSummary[..300] + "..." : script.MatchSummary;
                string text = $"{(existing == null ? "安装" : "更新")}油猴脚本？\n\n名称：{script.Name}\n"
                    + $"版本：{(existing == null ? "" : existing.Version + " → ")}{script.Version}\n"
                    + (script.Description.Length > 0 ? $"说明：{script.Description}\n" : "")
                    + $"匹配：{matches}\n权限：{string.Join(", ", script.Grants.DefaultIfEmpty("none"))}\n来源：{url}\n\n"
                    + "脚本可以读取和修改匹配网页的内容，请只安装信任的脚本。";
                if (MessageBox.Show(text, "安装脚本", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;
                var saved = UserScriptStore.Save(code, existing?.FileName);
                var errors = await UserScriptStore.EnsureDependenciesAsync(saved);
                ReloadUserScripts();
                MessageBox.Show(errors.Count == 0
                        ? $"已{(existing == null ? "安装" : "更新")}「{saved.Name}」，刷新匹配的网页后生效。"
                        : $"已保存「{saved.Name}」，但以下依赖下载失败：\n{string.Join("\n", errors)}",
                    "脚本", MessageBoxButton.OK, errors.Count == 0 ? MessageBoxImage.Information : MessageBoxImage.Warning);
            }
            catch (Exception ex)
            {
                (Application.Current as App)?.WriteDiagnostic("Installing userscript failed", ex);
                MessageBox.Show($"安装脚本失败: {ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        private async void OpenTabFromScript(string url, bool active)
        {
            var previous = activeTab;
            await OpenTabAsync(url);
            if (!active && previous != null && tabs.Contains(previous)) SelectTab(previous);
        }

        private void AddUserScriptMenuItems(CoreWebView2ContextMenuRequestedEventArgs e, CoreWebView2ContextMenuItem trayChromeMenu, int rootIndex)
        {
            var environment = webView.CoreWebView2.Environment;
            var manage = environment.CreateContextMenuItem("管理脚本...", null, CoreWebView2ContextMenuItemKind.Command);
            manage.CustomItemSelected += (_, _) => Dispatcher.BeginInvoke(new Action(ShowUserScriptSettings));
            trayChromeMenu.Children.Add(manage);

            var commands = activeTab?.ScriptMenus.ToList() ?? new List<ScriptMenuCommand>();
            if (commands.Count == 0) return;
            var menu = environment.CreateContextMenuItem("脚本命令", null, CoreWebView2ContextMenuItemKind.Submenu);
            bool showScriptName = commands.Select(command => command.ScriptName).Distinct().Count() > 1;
            foreach (var command in commands)
            {
                var item = environment.CreateContextMenuItem(
                    showScriptName ? $"{command.ScriptName}：{command.Caption}" : command.Caption, null, CoreWebView2ContextMenuItemKind.Command);
                item.CustomItemSelected += (_, _) => Dispatcher.BeginInvoke(new Action(() => command.Post(UserScripts.MenuClickMessage(command.Id))));
                menu.Children.Add(item);
            }
            e.MenuItems.Insert(rootIndex, menu);
        }

        private void ShowUserScriptSettings()
        {
            var settingsWindow = new SettingsWindow(appSettings, this, Application.Current as App);
            settingsWindow.ShowUserScripts();
            settingsWindow.Show();
        }
    }
}
