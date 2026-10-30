using System;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using Microsoft.Web.WebView2.Core;

namespace TrayChrome
{
    public partial class MainWindow
    {
        private CoreWebView2Find findSession;
        private string findSessionTerm;
        private DispatcherTimer findTimer;

        private async void ShowFindBar()
        {
            FindBar.Visibility = Visibility.Visible;
            if (FindBox.Text.Length == 0 && webView.CoreWebView2 != null)
            {
                try
                {
                    string json = await webView.CoreWebView2.ExecuteScriptAsync("String(window.getSelection ? window.getSelection() : '')");
                    string selection = JsonSerializer.Deserialize<string>(json) ?? string.Empty;
                    if (selection.Length is > 0 and <= 200 && !selection.Contains('\n') && FindBox.Text.Length == 0)
                        FindBox.Text = selection.Trim();
                }
                catch (Exception) { }
            }
            FindBox.Focus();
            Keyboard.Focus(FindBox);
            FindBox.SelectAll();
            if (FindBox.Text.Length > 0 && findSession == null) await StartFindAsync();
        }

        private void CloseFindBar()
        {
            findTimer?.Stop();
            StopFind();
            FindBar.Visibility = Visibility.Collapsed;
            FindCount.Text = string.Empty;
            webView.Focus();
        }

        private void StopFind()
        {
            if (findSession == null) return;
            findSession.MatchCountChanged -= FindSession_Changed;
            findSession.ActiveMatchIndexChanged -= FindSession_Changed;
            try { findSession.Stop(); }
            catch (Exception) { }
            findSession = null;
            findSessionTerm = null;
        }

        private async Task StartFindAsync()
        {
            StopFind();
            string term = FindBox.Text;
            var core = webView.CoreWebView2;
            if (term.Length == 0 || core == null)
            {
                FindCount.Text = string.Empty;
                return;
            }
            try
            {
                var options = core.Environment.CreateFindOptions();
                options.FindTerm = term;
                options.SuppressDefaultFindDialog = true;
                options.ShouldHighlightAllMatches = true;
                var session = core.Find;
                session.MatchCountChanged += FindSession_Changed;
                session.ActiveMatchIndexChanged += FindSession_Changed;
                findSession = session;
                findSessionTerm = term;
                await session.StartAsync(options);
                UpdateFindCount();
            }
            catch (Exception ex)
            {
                (Application.Current as App)?.WriteDiagnostic("Find in page failed", ex);
                FindCount.Text = "查找失败";
            }
        }

        private void FindSession_Changed(object sender, object e) => Dispatcher.BeginInvoke(new Action(UpdateFindCount));

        private void UpdateFindCount()
        {
            if (findSession == null) return;
            int count = findSession.MatchCount;
            int index = findSession.ActiveMatchIndex;
            FindCount.Text = count <= 0 ? "无结果" : $"{Math.Max(index, 1)}/{count}";
        }

        private async void FindStep(bool forward)
        {
            if (FindBar.Visibility != Visibility.Visible) { ShowFindBar(); return; }
            if (findSession == null || findSessionTerm != FindBox.Text || findSession != webView.CoreWebView2?.Find)
            {
                findTimer?.Stop();
                await StartFindAsync();
                if (forward) return;
            }
            try
            {
                if (forward) findSession?.FindNext(); else findSession?.FindPrevious();
            }
            catch (Exception ex) { (Application.Current as App)?.WriteDiagnostic("Find next failed", ex); }
        }

        // 切换标签页时在新页面上继续查找。
        private void RefreshFindForActiveTab()
        {
            if (FindBar.Visibility != Visibility.Visible) return;
            _ = StartFindAsync();
        }

        private void FindBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (findTimer == null)
            {
                findTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(200) };
                findTimer.Tick += async (_, _) =>
                {
                    findTimer.Stop();
                    await StartFindAsync();
                };
            }
            findTimer.Stop();
            findTimer.Start();
        }

        private void FindBox_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Escape)
            {
                CloseFindBar();
                e.Handled = true;
            }
            else if (e.Key == Key.Enter)
            {
                FindStep(!Keyboard.Modifiers.HasFlag(ModifierKeys.Shift));
                e.Handled = true;
            }
        }

        private void FindNext_Click(object sender, RoutedEventArgs e) => FindStep(true);
        private void FindPrevious_Click(object sender, RoutedEventArgs e) => FindStep(false);
        private void FindClose_Click(object sender, RoutedEventArgs e) => CloseFindBar();
    }
}
