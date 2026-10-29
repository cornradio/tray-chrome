#nullable enable
using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Microsoft.Web.WebView2.Core;

namespace TrayChrome
{
    public partial class MainWindow
    {
        // 记录自动填充时间；同一站点短时间内再次要求认证说明已保存的密码无效，改为弹窗。
        private readonly Dictionary<string, DateTime> basicAuthFilled = new Dictionary<string, DateTime>();

        private void CoreWebView2_BasicAuthenticationRequested(object? sender, CoreWebView2BasicAuthenticationRequestedEventArgs e)
        {
            string? origin = BasicAuthStore.GetOrigin(e.Uri);
            if (origin == null) return;
            (string UserName, string Password)? saved = null;
            try { saved = BasicAuthStore.Find(origin); }
            catch (Exception ex) { (Application.Current as App)?.WriteDiagnostic("Read basic auth failed", ex); }
            bool retry = basicAuthFilled.TryGetValue(origin, out var filledAt)
                && DateTime.UtcNow - filledAt < TimeSpan.FromSeconds(10);
            if (saved != null && !retry)
            {
                e.Response.UserName = saved.Value.UserName;
                e.Response.Password = saved.Value.Password;
                basicAuthFilled[origin] = DateTime.UtcNow;
                return;
            }
            basicAuthFilled.Remove(origin);
            var deferral = e.GetDeferral();
            Dispatcher.BeginInvoke(new Action(() =>
            {
                try
                {
                    var result = PromptBasicAuth(origin, e.Challenge, saved?.UserName ?? string.Empty, retry && saved != null);
                    if (result == null)
                    {
                        e.Cancel = true;
                        return;
                    }
                    e.Response.UserName = result.Value.UserName;
                    e.Response.Password = result.Value.Password;
                    basicAuthFilled[origin] = DateTime.UtcNow;
                    try
                    {
                        if (result.Value.Remember) BasicAuthStore.Save(origin, result.Value.UserName, result.Value.Password);
                        else BasicAuthStore.Remove(origin);
                    }
                    catch (Exception ex)
                    {
                        MessageBox.Show($"保存登录信息失败: {ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Warning);
                    }
                }
                finally
                {
                    deferral.Complete();
                }
            }));
        }

        private (string UserName, string Password, bool Remember)? PromptBasicAuth(string origin, string? challenge, string userName, bool savedFailed)
        {
            var dialog = new Window
            {
                Title = "登录",
                Width = 380,
                SizeToContent = SizeToContent.Height,
                ResizeMode = ResizeMode.NoResize,
                Topmost = true,
                WindowStartupLocation = IsVisible ? WindowStartupLocation.CenterOwner : WindowStartupLocation.CenterScreen
            };
            if (IsVisible) dialog.Owner = this;
            var panel = new StackPanel { Margin = new Thickness(15) };
            panel.Children.Add(new TextBlock { Text = origin, FontWeight = FontWeights.SemiBold, TextWrapping = TextWrapping.Wrap });
            if (!string.IsNullOrWhiteSpace(challenge))
                panel.Children.Add(new TextBlock { Text = challenge, Foreground = Brushes.Gray, TextWrapping = TextWrapping.Wrap });
            if (savedFailed)
                panel.Children.Add(new TextBlock { Text = "已保存的账号登录失败，请重新输入。", Foreground = Brushes.IndianRed, Margin = new Thickness(0, 5, 0, 0) });
            panel.Children.Add(new TextBlock { Text = "用户名", Margin = new Thickness(0, 10, 0, 3) });
            var userBox = new TextBox { Text = userName };
            panel.Children.Add(userBox);
            panel.Children.Add(new TextBlock { Text = "密码", Margin = new Thickness(0, 8, 0, 3) });
            var passwordBox = new PasswordBox();
            panel.Children.Add(passwordBox);
            var remember = new CheckBox { Content = "记住此站点的账号（加密保存，下次自动登录）", IsChecked = true, Margin = new Thickness(0, 10, 0, 0) };
            panel.Children.Add(remember);
            var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 12, 0, 0) };
            var ok = new Button { Content = "登录", IsDefault = true, MinWidth = 70 };
            var cancel = new Button { Content = "取消", IsCancel = true, MinWidth = 70, Margin = new Thickness(5, 0, 0, 0) };
            ok.Click += (_, _) => dialog.DialogResult = true;
            buttons.Children.Add(ok);
            buttons.Children.Add(cancel);
            panel.Children.Add(buttons);
            dialog.Content = panel;
            dialog.Loaded += (_, _) =>
            {
                if (string.IsNullOrEmpty(userBox.Text)) userBox.Focus();
                else passwordBox.Focus();
            };
            if (dialog.ShowDialog() != true) return null;
            return (userBox.Text, passwordBox.Password, remember.IsChecked == true);
        }
    }
}
