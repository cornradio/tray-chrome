using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Data;
using Microsoft.Win32;

namespace TrayChrome
{
    public partial class BookmarkManagerWindow
    {
        private void BookmarkTree_ContextMenuOpening(object sender, ContextMenuEventArgs e)
        {
            e.Handled = true;
            var container = FindBookmarkAncestor<TreeViewItem>(e.OriginalSource as DependencyObject)
                ?? BookmarkListBox.ItemContainerGenerator.ContainerFromItem(BookmarkListBox.SelectedItem) as TreeViewItem;
            if (container?.DataContext is not Bookmark bookmark) return;
            container.IsSelected = true;
            var menu = new ContextMenu { PlacementTarget = container, DataContext = bookmark };
            void Add(string title, RoutedEventHandler handler, string gesture = "")
            {
                var item = new MenuItem { Header = title, DataContext = bookmark, InputGestureText = gesture };
                item.Click += handler;
                menu.Items.Add(item);
            }
            Add("打开", OpenBookmarkMenu_Click);
            Add("编辑", EditBookmarkButton_Click);
            Add("移动...", MoveBookmark_Click);
            menu.Items.Add(new Separator());
            Add("删除", DeleteBookmarkButton_Click, "Del");
            menu.IsOpen = true;
        }

        private Bookmark currentBookmarkFolder;
        private bool synchronizingBookmarkNavigation;
        private string bookmarkSearch = "";

        private bool MatchesBookmarkSearch(Bookmark item)
        {
            return BookmarkFolders.All(new[] { item }).Any(entry =>
                entry.Title.Contains(bookmarkSearch, StringComparison.OrdinalIgnoreCase)
                || entry.Url.Contains(bookmarkSearch, StringComparison.OrdinalIgnoreCase));
        }

        private void ApplyBookmarkSearch(bool expandMatches = true)
        {
            void Filter(List<Bookmark> entries)
            {
                var view = CollectionViewSource.GetDefaultView(entries);
                view.Filter = string.IsNullOrEmpty(bookmarkSearch) ? null
                    : value => value is Bookmark item && MatchesBookmarkSearch(item);
                foreach (var folder in entries.Where(item => item.IsFolder)) Filter(folder.Children);
            }
            Filter(bookmarks);
            if (expandMatches && !string.IsNullOrEmpty(bookmarkSearch))
            {
                void Expand(ItemsControl parent)
                {
                    parent.UpdateLayout();
                    foreach (var entry in parent.Items)
                    {
                        if (parent.ItemContainerGenerator.ContainerFromItem(entry) is not TreeViewItem item) continue;
                        item.IsExpanded = true;
                        Expand(item);
                    }
                }
                Expand(BookmarkListBox);
            }
            BookmarkStatusText.Text = string.IsNullOrEmpty(bookmarkSearch)
                ? $"共 {BookmarkFolders.All(bookmarks).Count(item => !item.IsFolder)} 个书签"
                : $"匹配 {BookmarkFolders.All(bookmarks).Count(item => !item.IsFolder &&
                    (item.Title.Contains(bookmarkSearch, StringComparison.OrdinalIgnoreCase)
                    || item.Url.Contains(bookmarkSearch, StringComparison.OrdinalIgnoreCase)))} 个书签（名称 / URL）";
        }

        private void BookmarkSearch_Click(object sender, RoutedEventArgs e)
        {
            if (BookmarkListBox == null) return;
            bookmarkSearch = BookmarkSearchBox.Text.Trim();
            ApplyBookmarkSearch();
        }

        private void BookmarkSearch_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key != Key.Enter) return;
            e.Handled = true;
            BookmarkSearch_Click(sender, e);
        }
        private List<Bookmark> CurrentBookmarks => currentBookmarkFolder?.Children ?? bookmarks;

        private sealed class FolderOption
        {
            public string Path { get; set; }
            public Bookmark Folder { get; set; }
        }

        private List<FolderOption> FolderOptions(Bookmark excluded = null)
        {
            var options = new List<FolderOption> { new FolderOption { Path = "全部书签 /" } };
            void Visit(IEnumerable<Bookmark> entries, string path)
            {
                foreach (var folder in entries.Where(item => item.IsFolder && item != excluded))
                {
                    string childPath = path + folder.Title + " /";
                    options.Add(new FolderOption { Path = childPath, Folder = folder });
                    Visit(folder.Children, childPath);
                }
            }
            Visit(bookmarks, "全部书签 / ");
            return options;
        }

        private void BookmarkFolder_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (BookmarkFolderCombo.SelectedItem is not FolderOption option) return;
            currentBookmarkFolder = option.Folder;
            BookmarkStatusText.Text = $"当前文件夹 {CurrentBookmarks.Count} 项；共 {BookmarkFolders.All(bookmarks).Count(item => !item.IsFolder)} 个书签";
            if (!synchronizingBookmarkNavigation) NavigateToBookmarkFolder(option.Folder);
        }

        private void CollapseAllBookmarks_Click(object sender, RoutedEventArgs e)
        {
            synchronizingBookmarkNavigation = true;
            try
            {
                void Collapse(ItemsControl parent)
                {
                    parent.UpdateLayout();
                    foreach (var entry in parent.Items)
                    {
                        if (parent.ItemContainerGenerator.ContainerFromItem(entry) is not TreeViewItem item) continue;
                        if (item.HasItems)
                        {
                            item.IsExpanded = true;
                            Collapse(item);
                        }
                        item.IsExpanded = false;
                    }
                }
                Collapse(BookmarkListBox);
            }
            finally { synchronizingBookmarkNavigation = false; }
        }

        private void NavigateToBookmarkFolder(Bookmark folder)
        {
            BookmarkSearchBox.Clear();
            if (!string.IsNullOrEmpty(bookmarkSearch))
            {
                bookmarkSearch = "";
                ApplyBookmarkSearch();
            }
            if (folder == null)
            {
                BookmarkListBox.UpdateLayout();
                if (BookmarkListBox.Items.Count > 0
                    && BookmarkListBox.ItemContainerGenerator.ContainerFromIndex(0) is TreeViewItem first)
                    first.BringIntoView();
                return;
            }
            var path = new List<Bookmark>();
            bool Find(IEnumerable<Bookmark> entries)
            {
                foreach (var entry in entries)
                {
                    path.Add(entry);
                    if (entry == folder || Find(entry.Children)) return true;
                    path.RemoveAt(path.Count - 1);
                }
                return false;
            }
            if (!Find(bookmarks)) return;
            synchronizingBookmarkNavigation = true;
            try
            {
                ItemsControl parent = BookmarkListBox;
                foreach (var entry in path)
                {
                    parent.UpdateLayout();
                    if (parent.ItemContainerGenerator.ContainerFromItem(entry) is not TreeViewItem item) return;
                    item.IsExpanded = true;
                    if (entry == folder)
                    {
                        item.IsSelected = true;
                        item.BringIntoView();
                    }
                    parent = item;
                }
            }
            finally { synchronizingBookmarkNavigation = false; }
        }

        private void NewBookmarkFolder_Click(object sender, RoutedEventArgs e)
        {
            var folder = new Bookmark { Title = "新文件夹", IsFolder = true };
            EditBookmark(folder, isNew: true);
        }

        private void ImportBookmarks_Click(object sender, RoutedEventArgs e)
        {
            var picker = new OpenFileDialog { Title = "导入浏览器导出的 HTML 书签", Filter = "HTML 书签 (*.html;*.htm)|*.html;*.htm" };
            if (picker.ShowDialog(this) != true) return;
            try
            {
                var imported = BookmarkFolders.ImportHtml(File.ReadAllText(picker.FileName), out int skipped);
                CurrentBookmarks.AddRange(imported);
                bool saved = SaveBookmarks();
                if (!saved)
                    foreach (var item in imported) CurrentBookmarks.Remove(item);
                RefreshBookmarkList();
                if (saved) MessageBox.Show($"已导入 {BookmarkFolders.All(imported).Count(item => !item.IsFolder)} 个书签，跳过 {skipped} 个不支持或无效地址。",
                    "导入完成", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"导入书签失败：{ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        private void OpenBookmarkConfig_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (!File.Exists(bookmarksFilePath)) BookmarkFolders.Save(bookmarksFilePath, bookmarks);
                Process.Start(new ProcessStartInfo(bookmarksFilePath) { UseShellExecute = true });
            }
            catch (Exception ex)
            {
                MessageBox.Show($"打开书签配置失败：{ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        private void MoveBookmark_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not FrameworkElement { DataContext: Bookmark bookmark }) return;
            var choices = FolderOptions(bookmark);
            var combo = new ComboBox { ItemsSource = choices, DisplayMemberPath = "Path", SelectedIndex = 0, Margin = new Thickness(12) };
            var save = new Button { Content = "移动", Margin = new Thickness(12), IsDefault = true };
            var panel = new StackPanel();
            panel.Children.Add(combo);
            panel.Children.Add(save);
            var dialog = new Window { Title = "移动到文件夹", Owner = this, Width = 400,
                SizeToContent = SizeToContent.Height, Content = panel, WindowStartupLocation = WindowStartupLocation.CenterOwner };
            save.Click += (_, _) =>
            {
                if (combo.SelectedItem is not FolderOption option) return;
                var destination = option.Folder?.Children ?? bookmarks;
                var source = BookmarkFolders.FindParent(bookmarks, bookmark);
                int sourceIndex = source?.IndexOf(bookmark) ?? -1;
                if (BookmarkFolders.Move(bookmarks, bookmark, option.Folder))
                {
                    if (!SaveBookmarks())
                    {
                        destination.Remove(bookmark);
                        source.Insert(sourceIndex, bookmark);
                    }
                    RefreshBookmarkList();
                }
                dialog.Close();
            };
            ShowBookmarkDialog(dialog);
        }

        private void ExportBookmarks_Click(object sender, RoutedEventArgs e)
        {
            var picker = new SaveFileDialog { Title = "导出 Chrome 兼容 HTML 书签", Filter = "HTML 书签 (*.html)|*.html", FileName = "bookmarks.html" };
            if (picker.ShowDialog(this) != true) return;
            try
            {
                File.WriteAllText(picker.FileName, BookmarkFolders.ExportHtml(bookmarks), new System.Text.UTF8Encoding(false));
                BookmarkStatusText.Text = $"已导出全部书签到 {picker.FileName}";
            }
            catch (Exception ex) { MessageBox.Show($"导出书签失败：{ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Warning); }
        }

        private Point bookmarkDragStart;
        private Bookmark draggedBookmark;
        private bool bookmarkDragAllowed;

        private void BookmarkTree_RightClick(object sender, MouseButtonEventArgs e)
        {
            var item = FindBookmarkAncestor<TreeViewItem>(e.OriginalSource as DependencyObject);
            if (item == null) return;
            item.IsSelected = true;
            item.Focus();
        }

        private void BookmarkTree_KeyDown(object sender, KeyEventArgs e)
        {
            if (Keyboard.Modifiers != ModifierKeys.None || BookmarkListBox.SelectedItem is not Bookmark bookmark) return;
            if (e.Key == Key.Delete)
            {
                e.Handled = true;
                DeleteBookmark(bookmark);
            }
            else if (e.Key == Key.Enter)
            {
                e.Handled = true;
                OpenBookmark(bookmark);
            }
        }

        private void OpenBookmarkMenu_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not MenuItem { DataContext: Bookmark bookmark }) return;
            e.Handled = true;
            OpenBookmark(bookmark);
        }

        private void OpenBookmark(Bookmark bookmark)
        {
            if (bookmark.IsFolder)
            {
                NavigateToBookmarkFolder(bookmark);
                return;
            }
            if (mainWindow?.webView?.CoreWebView2 == null)
            {
                MessageBox.Show("浏览器尚未就绪，请稍后重试。", "提示", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }
            mainWindow.webView.CoreWebView2.Navigate(bookmark.Url);
            if (!mainWindow.IsVisible) mainWindow.ShowWithAnimation();
            else if (mainWindow.WindowState == WindowState.Minimized)
                mainWindow.WindowState = WindowState.Normal;
            mainWindow.Activate();
            mainWindow.webView.Focus();
        }

        private static T FindBookmarkAncestor<T>(DependencyObject source) where T : DependencyObject
        {
            while (source != null)
            {
                if (source is T result) return result;
                source = source is Visual || source is System.Windows.Media.Media3D.Visual3D
                    ? VisualTreeHelper.GetParent(source) : LogicalTreeHelper.GetParent(source);
            }
            return null;
        }

        private void BookmarkTree_MouseDown(object sender, MouseButtonEventArgs e)
        {
            bookmarkDragStart = e.GetPosition(BookmarkListBox);
            var source = e.OriginalSource as DependencyObject;
            draggedBookmark = FindBookmarkAncestor<TreeViewItem>(source)?.DataContext as Bookmark;
            bookmarkDragAllowed = FindBookmarkAncestor<Button>(source) == null;
        }

        private void BookmarkTree_MouseMove(object sender, MouseEventArgs e)
        {
            if (!bookmarkDragAllowed || draggedBookmark == null || e.LeftButton != MouseButtonState.Pressed) return;
            var delta = e.GetPosition(BookmarkListBox) - bookmarkDragStart;
            if (Math.Abs(delta.X) < SystemParameters.MinimumHorizontalDragDistance
                && Math.Abs(delta.Y) < SystemParameters.MinimumVerticalDragDistance) return;
            bookmarkDragAllowed = false;
            DragDrop.DoDragDrop(BookmarkListBox, new DataObject(typeof(Bookmark), draggedBookmark), DragDropEffects.Move);
            draggedBookmark = null;
        }

        private Bookmark BookmarkDropFolder(DragEventArgs e)
        {
            var target = FindBookmarkAncestor<TreeViewItem>(e.OriginalSource as DependencyObject)?.DataContext as Bookmark;
            if (target == null || target.IsFolder) return target;
            var parent = BookmarkFolders.FindParent(bookmarks, target);
            return BookmarkFolders.All(bookmarks).FirstOrDefault(item => item.IsFolder && item.Children == parent);
        }

        private void BookmarkTree_DragOver(object sender, DragEventArgs e)
        {
            var item = e.Data.GetData(typeof(Bookmark)) as Bookmark;
            var folder = BookmarkDropFolder(e);
            bool valid = item != null && item == draggedBookmark && BookmarkFolders.FindParent(bookmarks, item) != null
                && (folder == null || !BookmarkFolders.All(new[] { item }).Contains(folder))
                && BookmarkFolders.FindParent(bookmarks, item) != (folder?.Children ?? bookmarks);
            e.Effects = valid ? DragDropEffects.Move : DragDropEffects.None;
            e.Handled = true;
        }

        private void BookmarkTree_Drop(object sender, DragEventArgs e)
        {
            e.Handled = true;
            var item = e.Data.GetData(typeof(Bookmark)) as Bookmark;
            if (item == null || item != draggedBookmark) return;
            var source = BookmarkFolders.FindParent(bookmarks, item);
            if (source == null) return;
            int index = source.IndexOf(item);
            var folder = BookmarkDropFolder(e);
            if (!BookmarkFolders.Move(bookmarks, item, folder)) return;
            if (!SaveBookmarks())
            {
                (folder?.Children ?? bookmarks).Remove(item);
                source.Insert(index, item);
            }
            RefreshBookmarkList();
        }

        private void BookmarkTree_SelectedItemChanged(object sender, RoutedPropertyChangedEventArgs<object> e)
        {
            if (synchronizingBookmarkNavigation || e.NewValue is not Bookmark item) return;
            var parent = BookmarkFolders.FindParent(bookmarks, item);
            currentBookmarkFolder = item.IsFolder ? item
                : BookmarkFolders.All(bookmarks).FirstOrDefault(folder => folder.IsFolder && folder.Children == parent);
            synchronizingBookmarkNavigation = true;
            try
            {
                var options = FolderOptions();
                BookmarkFolderCombo.ItemsSource = options;
                BookmarkFolderCombo.SelectedItem = options.FirstOrDefault(option => option.Folder == currentBookmarkFolder) ?? options[0];
            }
            finally { synchronizingBookmarkNavigation = false; }
        }
    }
}
