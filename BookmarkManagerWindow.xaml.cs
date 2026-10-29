using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace TrayChrome
{
    public partial class BookmarkManagerWindow : Window
    {
        private readonly App app;
        private readonly MainWindow? mainWindow;
        private readonly string bookmarksFilePath = BookmarkFolders.FilePath;
        private List<Bookmark> bookmarks = new();
        private Window? bookmarkDialog;

        public BookmarkManagerWindow(App app, MainWindow? mainWindow)
        {
            InitializeComponent();
            this.app = app;
            this.mainWindow = mainWindow;
            ReloadBookmarks();
            Closed += (_, _) => bookmarkDialog?.Close();
        }

        public void ActivateManager()
        {
            if (WindowState == WindowState.Minimized) WindowState = WindowState.Normal;
            Show();
            Activate();
            if (bookmarkDialog != null)
            {
                if (bookmarkDialog.WindowState == WindowState.Minimized)
                    bookmarkDialog.WindowState = WindowState.Normal;
                bookmarkDialog.Activate();
            }
        }

        public void ReloadBookmarks()
        {
            // External changes invalidate objects captured by an open editor.
            bookmarkDialog?.Close();
            currentBookmarkFolder = null;
            LoadBookmarks();
            RefreshBookmarkList(preserveTreeState: false);
        }

        public void EditBookmarkFromMenu(Bookmark source, List<Bookmark> sourceTree)
        {
            if (bookmarkDialog != null)
            {
                bookmarkDialog.Activate();
                return;
            }
            var path = new List<int>();
            bool Find(List<Bookmark> entries)
            {
                for (int i = 0; i < entries.Count; i++)
                {
                    path.Add(i);
                    if (ReferenceEquals(entries[i], source)) return true;
                    if (Find(entries[i].Children)) return true;
                    path.RemoveAt(path.Count - 1);
                }
                return false;
            }
            if (!Find(sourceTree)) return;
            var entries = bookmarks;
            Bookmark? target = null;
            foreach (int index in path)
            {
                if (index >= entries.Count) return;
                target = entries[index];
                entries = target.Children;
            }
            if (target == null || target.Title != source.Title || target.Url != source.Url
                || target.IsFolder != source.IsFolder) return;
            EditBookmark(target);
        }

        private void ShowBookmarkDialog(Window dialog)
        {
            if (bookmarkDialog != null)
            {
                bookmarkDialog.Activate();
                return;
            }
            bookmarkDialog = dialog;
            IsEnabled = false;
            dialog.Closed += (_, _) =>
            {
                bookmarkDialog = null;
                IsEnabled = true;
            };
            try { dialog.Show(); }
            catch
            {
                bookmarkDialog = null;
                IsEnabled = true;
                throw;
            }
        }

        private void LoadBookmarks()
        {
            try
            {
                bookmarks = File.Exists(bookmarksFilePath)
                    ? BookmarkFolders.Load(bookmarksFilePath) : new List<Bookmark>();
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, $"加载收藏夹失败: {ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Warning);
                bookmarks = new List<Bookmark>();
            }
        }

        private bool SaveBookmarks()
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(bookmarksFilePath));
                BookmarkFolders.Save(bookmarksFilePath, bookmarks);
                app.OnBookmarksUpdated(this, EventArgs.Empty);
                return true;
            }
            catch (Exception ex)
            {
                MessageBox.Show(bookmarkDialog ?? this, $"保存收藏夹失败: {ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Warning);
                return false;
            }
        }

        private void RefreshBookmarkList(bool preserveTreeState = true)
        {
            var treeState = preserveTreeState ? CaptureBookmarkTreeState() : null;
            if (currentBookmarkFolder != null && !BookmarkFolders.All(bookmarks).Contains(currentBookmarkFolder))
                currentBookmarkFolder = null;
            var options = FolderOptions();
            synchronizingBookmarkNavigation = true;
            try
            {
                BookmarkFolderCombo.ItemsSource = options;
                BookmarkFolderCombo.SelectedItem = options.FirstOrDefault(option => option.Folder == currentBookmarkFolder) ?? options[0];
            }
            finally { synchronizingBookmarkNavigation = false; }
            BookmarkListBox.ItemsSource = null;
            BookmarkListBox.ItemsSource = bookmarks;
            ApplyBookmarkSearch(expandMatches: !preserveTreeState);
            if (treeState != null) RestoreBookmarkTreeState(treeState);
        }

        private void AddBookmarkButton_Click(object sender, RoutedEventArgs e)
        {
            EditBookmark(new Bookmark(), isNew: true);
        }

        private void EditBookmarkButton_Click(object sender, RoutedEventArgs e)
        {
            if (sender is FrameworkElement { DataContext: Bookmark bookmark }) EditBookmark(bookmark);
        }

        private void DeleteBookmarkButton_Click(object sender, RoutedEventArgs e)
        {
            if (sender is FrameworkElement { DataContext: Bookmark bookmark }) DeleteBookmark(bookmark);
        }

        private void DeleteBookmark(Bookmark bookmark)
        {
            var parent = BookmarkFolders.FindParent(bookmarks, bookmark);
            if (parent == null) return;
            int index = parent.IndexOf(bookmark);
            parent.RemoveAt(index);
            if (!SaveBookmarks()) parent.Insert(index, bookmark);
            RefreshBookmarkList(preserveTreeState: true);
        }

        private void RefreshBookmarkButton_Click(object sender, RoutedEventArgs e) => ReloadBookmarks();

        private void BookmarkListBox_MouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            if (FindBookmarkAncestor<Button>(e.OriginalSource as DependencyObject) != null) return;
            if (BookmarkListBox.SelectedItem is Bookmark { IsFolder: true } folder)
            {
                currentBookmarkFolder = folder;
                return;
            }
            if (BookmarkListBox.SelectedItem is Bookmark bookmark) OpenBookmark(bookmark);
        }

        private void EditBookmark(Bookmark bookmark, bool isNew = false)
        {
            if (bookmarkDialog != null) return;
            var dialog = new Window
            {
                Title = isNew ? "添加收藏" : "编辑收藏夹", Width = 420, SizeToContent = SizeToContent.Height,
                WindowStartupLocation = WindowStartupLocation.CenterOwner, Owner = this, ResizeMode = ResizeMode.NoResize
            };
            var grid = new Grid { Margin = new Thickness(10) };
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            grid.ColumnDefinitions.Add(new ColumnDefinition());
            for (int i = 0; i < 3; i++) grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            var titleLabel = new Label { Content = "标题:" };
            var titleBox = new TextBox { Text = bookmark.Title, Margin = new Thickness(5), Padding = new Thickness(5) };
            Grid.SetColumn(titleBox, 1);
            grid.Children.Add(titleLabel);
            grid.Children.Add(titleBox);
            var urlLabel = new Label { Content = "URL:" };
            var urlBox = new TextBox { Text = bookmark.Url, Margin = new Thickness(5), Padding = new Thickness(5) };
            Grid.SetRow(urlLabel, 1);
            Grid.SetRow(urlBox, 1);
            Grid.SetColumn(urlBox, 1);
            if (bookmark.IsFolder) urlLabel.Visibility = urlBox.Visibility = Visibility.Collapsed;
            grid.Children.Add(urlLabel);
            grid.Children.Add(urlBox);
            var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
            var save = new Button { Content = "确定", IsDefault = true, MinWidth = 80, Margin = new Thickness(5) };
            var cancel = new Button { Content = "取消", MinWidth = 80, Margin = new Thickness(5) };
            cancel.Click += (_, _) => dialog.Close();
            dialog.PreviewKeyDown += (_, args) =>
            {
                if (args.Key == Key.Escape) { args.Handled = true; dialog.Close(); }
            };
            save.Click += (_, _) =>
            {
                if (string.IsNullOrWhiteSpace(titleBox.Text) || (!bookmark.IsFolder && string.IsNullOrWhiteSpace(urlBox.Text)))
                {
                    MessageBox.Show(dialog, "标题和URL不能为空！", "错误", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }
                if (!bookmark.IsFolder && (!Uri.TryCreate(urlBox.Text.Trim(), UriKind.Absolute, out var address)
                    || (address.Scheme != Uri.UriSchemeHttp && address.Scheme != Uri.UriSchemeHttps)))
                {
                    MessageBox.Show(dialog, "URL 必须是 HTTP 或 HTTPS 地址。", "错误", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }
                if (isNew && !bookmark.IsFolder && CurrentBookmarks.Any(b => !b.IsFolder &&
                    b.Url.Equals(urlBox.Text.Trim(), StringComparison.OrdinalIgnoreCase)))
                {
                    MessageBox.Show(dialog, "该URL已在收藏夹中！", "提示", MessageBoxButton.OK, MessageBoxImage.Information);
                    return;
                }
                string oldTitle = bookmark.Title, oldUrl = bookmark.Url;
                bookmark.Title = titleBox.Text.Trim();
                bookmark.Url = urlBox.Text.Trim();
                if (isNew) CurrentBookmarks.Add(bookmark);
                if (!SaveBookmarks())
                {
                    if (isNew) CurrentBookmarks.Remove(bookmark);
                    bookmark.Title = oldTitle;
                    bookmark.Url = oldUrl;
                    return;
                }
                RefreshBookmarkList();
                dialog.Close();
            };
            buttons.Children.Add(save);
            buttons.Children.Add(cancel);
            Grid.SetRow(buttons, 2);
            Grid.SetColumnSpan(buttons, 2);
            grid.Children.Add(buttons);
            dialog.Content = grid;
            ShowBookmarkDialog(dialog);
            titleBox.Focus();
        }
    }
}
