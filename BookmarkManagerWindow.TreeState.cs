using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace TrayChrome
{
    public partial class BookmarkManagerWindow
    {
        private sealed class BookmarkTreeState
        {
            public Dictionary<Bookmark, bool> Expanded { get; } = new();
            public Bookmark Selected { get; set; }
            public double VerticalOffset { get; set; }
            public double HorizontalOffset { get; set; }
        }

        private static ScrollViewer FindBookmarkScrollViewer(DependencyObject parent)
        {
            if (parent is ScrollViewer viewer) return viewer;
            for (int i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
            {
                var result = FindBookmarkScrollViewer(VisualTreeHelper.GetChild(parent, i));
                if (result != null) return result;
            }
            return null;
        }

        private BookmarkTreeState CaptureBookmarkTreeState()
        {
            var scroll = FindBookmarkScrollViewer(BookmarkListBox);
            var state = new BookmarkTreeState
            {
                Selected = BookmarkListBox.SelectedItem as Bookmark,
                VerticalOffset = scroll?.VerticalOffset ?? 0,
                HorizontalOffset = scroll?.HorizontalOffset ?? 0
            };
            void Capture(ItemsControl parent)
            {
                foreach (var entry in parent.Items)
                {
                    if (parent.ItemContainerGenerator.ContainerFromItem(entry) is not TreeViewItem item
                        || entry is not Bookmark bookmark) continue;
                    state.Expanded[bookmark] = item.IsExpanded;
                    Capture(item);
                }
            }
            Capture(BookmarkListBox);
            return state;
        }

        private void RestoreBookmarkTreeState(BookmarkTreeState state)
        {
            synchronizingBookmarkNavigation = true;
            try
            {
                void Restore(ItemsControl parent)
                {
                    parent.UpdateLayout();
                    foreach (var entry in parent.Items)
                    {
                        if (parent.ItemContainerGenerator.ContainerFromItem(entry) is not TreeViewItem item
                            || entry is not Bookmark bookmark) continue;
                        if (state.Expanded.TryGetValue(bookmark, out bool expanded))
                        {
                            // Generate descendants before restoring a collapsed parent's state.
                            item.IsExpanded = true;
                            Restore(item);
                            item.IsExpanded = expanded;
                        }
                        if (bookmark == state.Selected) item.IsSelected = true;
                    }
                }
                Restore(BookmarkListBox);
                BookmarkListBox.UpdateLayout();
                var scroll = FindBookmarkScrollViewer(BookmarkListBox);
                scroll?.ScrollToHorizontalOffset(state.HorizontalOffset);
                scroll?.ScrollToVerticalOffset(state.VerticalOffset);
            }
            finally { synchronizingBookmarkNavigation = false; }
        }
    }
}
