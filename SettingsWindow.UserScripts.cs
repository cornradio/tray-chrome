#nullable enable
using System;
using System.Diagnostics;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace TrayChrome
{
    public partial class SettingsWindow
    {
        private bool userScriptsLoaded;

        public void ShowUserScripts()
        {
            UserScriptsTab.IsSelected = true;
            RefreshUserScripts();
        }

        private void RefreshUserScripts(string? selectFileName = null)
        {
            userScriptsLoaded = true;
            string? selected = selectFileName ?? (UserScriptList.SelectedItem as UserScript)?.FileName;
            try
            {
                var scripts = UserScriptStore.LoadAll();
                UserScriptList.ItemsSource = scripts;
                UserScriptList.SelectedItem = scripts.FirstOrDefault(script => script.FileName == selected);
            }
            catch (Exception ex) { ShowUserScriptError(ex); }
        }

        // 脚本改动立即写入文件；运行中的实例通过文件监视自动重新加载。
        private void UserScriptsChanged(string? selectFileName = null)
        {
            RefreshUserScripts(selectFileName);
            mainWindow?.ReloadUserScripts();
        }

        private void MainTabControl_UserScriptsSelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (e.Source == MainTabControl && UserScriptsTab.IsSelected && !userScriptsLoaded) RefreshUserScripts();
        }

        private void NewUserScript_Click(object sender, RoutedEventArgs e) => EditUserScript(null);

        private void EditUserScript_Click(object sender, RoutedEventArgs e)
        {
            if (UserScriptList.SelectedItem is UserScript script) EditUserScript(script);
        }

        private void UserScriptList_MouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            if (FindVisualParent<ListViewItem>(e.OriginalSource as DependencyObject)?.Content is UserScript script) EditUserScript(script);
        }

        private void UserScriptList_KeyDown(object sender, KeyEventArgs e)
        {
            if (UserScriptList.SelectedItem is not UserScript script) return;
            if (e.Key == Key.Enter) { EditUserScript(script); e.Handled = true; }
            else if (e.Key == Key.Delete) { DeleteUserScript(script); e.Handled = true; }
        }

        private void DeleteUserScript_Click(object sender, RoutedEventArgs e)
        {
            if (UserScriptList.SelectedItem is UserScript script) DeleteUserScript(script);
        }

        private void DeleteUserScript(UserScript script)
        {
            try
            {
                UserScriptStore.Delete(script.FileName);
                UserScriptsChanged();
            }
            catch (Exception ex) { ShowUserScriptError(ex); }
        }

        private void UserScriptEnabled_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not CheckBox { DataContext: UserScript script } box) return;
            try
            {
                UserScriptStore.SetEnabled(script.FileName, box.IsChecked == true);
                UserScriptsChanged(script.FileName);
            }
            catch (Exception ex) { ShowUserScriptError(ex); }
        }

        private void OpenUserScriptFolder_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                UserScriptStore.EnsureFolder();
                Process.Start(new ProcessStartInfo(UserScriptStore.Folder) { UseShellExecute = true });
            }
            catch (Exception ex) { ShowUserScriptError(ex); }
        }

        private async void InstallUserScript_Click(object sender, RoutedEventArgs e)
        {
            string? url = PromptUserScriptUrl();
            if (string.IsNullOrWhiteSpace(url)) return;
            url = url.Trim();
            if (!url.Contains("://")) url = "https://" + url;
            IsEnabled = false;
            try
            {
                string code = await UserScriptStore.DownloadScriptAsync(url);
                var parsed = UserScriptStore.Parse(code);
                var existing = UserScriptStore.LoadAll().FirstOrDefault(item => item.Name == parsed.Name && item.Namespace == parsed.Namespace);
                var saved = UserScriptStore.Save(code, existing?.FileName);
                var errors = await UserScriptStore.EnsureDependenciesAsync(saved);
                UserScriptsChanged(saved.FileName);
                if (errors.Count > 0)
                    MessageBox.Show(this, $"已保存「{saved.Name}」，但以下依赖下载失败：\n{string.Join("\n", errors)}",
                        "脚本", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
            catch (Exception ex) { ShowUserScriptError(ex); }
            finally { IsEnabled = true; }
        }

        private void EditUserScript(UserScript? script)
        {
            string code;
            try
            {
                code = script == null ? UserScriptStore.Template
                    : System.IO.File.ReadAllText(System.IO.Path.Combine(UserScriptStore.Folder, script.FileName));
            }
            catch (Exception ex) { ShowUserScriptError(ex); return; }

            var editor = new Window
            {
                Title = script == null ? "新建脚本" : $"编辑脚本 - {script.Name}",
                Owner = this,
                Width = 760,
                Height = 560,
                ShowInTaskbar = false,
                WindowStartupLocation = WindowStartupLocation.CenterOwner
            };
            var root = new DockPanel { Margin = new Thickness(10) };
            var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
            DockPanel.SetDock(buttons, Dock.Bottom);
            var save = new Button { Content = "保存 (Ctrl+S)", Margin = new Thickness(5, 10, 0, 0), Padding = new Thickness(10, 4, 10, 4) };
            var cancel = new Button { Content = "取消", IsCancel = true, Margin = new Thickness(5, 10, 0, 0), Padding = new Thickness(10, 4, 10, 4) };
            buttons.Children.Add(save);
            buttons.Children.Add(cancel);
            root.Children.Add(buttons);
            var box = new TextBox
            {
                Text = code,
                AcceptsReturn = true,
                AcceptsTab = true,
                FontFamily = new FontFamily("Consolas, Microsoft YaHei UI"),
                FontSize = 13,
                TextWrapping = TextWrapping.NoWrap,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto
            };
            root.Children.Add(box);
            editor.Content = root;

            async void Save()
            {
                try
                {
                    var saved = UserScriptStore.Save(box.Text, script?.FileName);
                    editor.DialogResult = true;
                    var errors = await UserScriptStore.EnsureDependenciesAsync(saved);
                    UserScriptsChanged(saved.FileName);
                    if (errors.Count > 0)
                        MessageBox.Show(this, $"已保存，但以下依赖下载失败：\n{string.Join("\n", errors)}",
                            "脚本", MessageBoxButton.OK, MessageBoxImage.Warning);
                }
                catch (Exception ex)
                {
                    MessageBox.Show(editor, $"保存失败：{ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
            save.Click += (_, _) => Save();
            editor.PreviewKeyDown += (_, args) =>
            {
                if (args.Key == Key.S && Keyboard.Modifiers == ModifierKeys.Control)
                {
                    args.Handled = true;
                    Save();
                }
            };
            editor.Loaded += (_, _) => box.Focus();
            editor.ShowDialog();
        }

        private string? PromptUserScriptUrl()
        {
            var dialog = new Window
            {
                Title = "从 URL 安装脚本",
                Owner = this,
                Width = 460,
                SizeToContent = SizeToContent.Height,
                ResizeMode = ResizeMode.NoResize,
                ShowInTaskbar = false,
                WindowStartupLocation = WindowStartupLocation.CenterOwner
            };
            var panel = new StackPanel { Margin = new Thickness(10) };
            panel.Children.Add(new TextBlock { Text = "脚本地址（.user.js）：", Margin = new Thickness(5, 0, 5, 0) });
            var box = new TextBox { Margin = new Thickness(5), Padding = new Thickness(4) };
            panel.Children.Add(box);
            var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
            var ok = new Button { Content = "安装", IsDefault = true, Margin = new Thickness(5, 10, 0, 0), Padding = new Thickness(10, 4, 10, 4) };
            ok.Click += (_, _) => dialog.DialogResult = true;
            buttons.Children.Add(ok);
            buttons.Children.Add(new Button { Content = "取消", IsCancel = true, Margin = new Thickness(5, 10, 0, 0), Padding = new Thickness(10, 4, 10, 4) });
            panel.Children.Add(buttons);
            dialog.Content = panel;
            dialog.Loaded += (_, _) =>
            {
                if (Clipboard.ContainsText() && Clipboard.GetText().Trim() is { } text
                    && text.EndsWith(".user.js", StringComparison.OrdinalIgnoreCase) && !text.Contains('\n')) box.Text = text;
                box.Focus();
                box.SelectAll();
            };
            return dialog.ShowDialog() == true ? box.Text : null;
        }

        private static T? FindVisualParent<T>(DependencyObject? source) where T : DependencyObject
        {
            while (source != null && source is not T) source = VisualTreeHelper.GetParent(source);
            return source as T;
        }

        private void ShowUserScriptError(Exception ex)
        {
            MessageBox.Show(this, $"脚本操作失败：{ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }
}
