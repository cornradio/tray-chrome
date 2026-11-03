using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;

namespace TrayChrome
{
    public partial class HistoryWindow : Window
    {
        private readonly MainWindow browser;
        private List<VisitRecord> records = new();
        private bool busy;

        public HistoryWindow(MainWindow browser)
        {
            AppTheme.EnsureInitialized();
            InitializeComponent();
            this.browser = browser;
            Activated += Refresh_Click;
        }

        private async void Refresh_Click(object sender, EventArgs e)
        {
            if (busy) return;
            busy = true;
            try { records = await VisitHistory.ReadAsync(); Filter(); }
            catch (Exception ex) { ShowError(ex); }
            finally { busy = false; }
        }

        private void Search_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (RecordsGrid != null) Filter();
        }

        private void Filter()
        {
            string query = SearchBox.Text.Trim();
            var matches = records.Where(record => record.Title.Contains(query, StringComparison.OrdinalIgnoreCase)
                || record.Url.Contains(query, StringComparison.OrdinalIgnoreCase)).ToList();
            RecordsGrid.ItemsSource = matches;
            Status.Text = $"最近 7 天 · {matches.Count} 条（全部实例）";
        }

        private async void Clear_Click(object sender, RoutedEventArgs e)
        {
            if (busy) return;
            if (MessageBox.Show(this, "清空所有实例的访问历史？此操作无法撤销。", "清空历史",
                MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes) return;
            busy = true;
            try { await VisitHistory.ClearAsync(); records.Clear(); Filter(); }
            catch (Exception ex) { ShowError(ex); }
            finally { busy = false; }
        }

        private async void Open_Click(object sender, RoutedEventArgs e)
        {
            if (RecordsGrid.SelectedItem is not VisitRecord record) return;
            try { browser.ShowWithAnimation(); browser.Activate(); await browser.OpenTabAsync(record.Url); }
            catch (Exception ex) { ShowError(ex); }
        }

        private void ShowError(Exception ex)
        {
            (Application.Current as App)?.WriteDiagnostic("Visit history operation failed", ex);
            MessageBox.Show(this, $"历史记录操作失败: {ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }
}
