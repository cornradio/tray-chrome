using System;
using System.Globalization;
using System.Windows;
using System.Windows.Input;

namespace TrayChrome
{
    internal static class ZoomPercentage
    {
        public static string Format(double factor) => (factor * 100).ToString("0.##", CultureInfo.CurrentCulture);

        public static bool TryParse(string text, out double factor)
        {
            bool valid = double.TryParse(text.Trim().TrimEnd('%'), out double percent)
                && double.IsFinite(percent) && percent >= 30 && percent <= 300;
            factor = valid ? percent / 100 : 0;
            return valid;
        }
    }

    public partial class MainWindow
    {
        private bool applyingBrowserZoom;
        public double BrowserZoomFactor => currentZoomFactor;
        public event EventHandler BrowserZoomChanged;

        private void TrackTabZoom(BrowserTab tab, Microsoft.Web.WebView2.Wpf.WebView2 view)
        {
            view.ZoomFactorChanged += (_, _) =>
            {
                if (!applyingBrowserZoom && tab == activeTab && view.ZoomFactor != currentZoomFactor)
                    SetBrowserZoom(Math.Clamp(view.ZoomFactor, 0.3, 3.0));
            };
        }

        private void SetBrowserZoom(double factor)
        {
            string url = activeTab?.View.Source?.AbsoluteUri ?? activeTab?.Url;
            string site = SiteZoomStore.Site(url);
            try
            {
                if (site != null) SiteZoomStore.Save(url, factor);
            }
            catch (Exception ex) { ReportSiteZoomError(ex); return; }
            currentZoomFactor = factor;
            if (activeTab != null)
            {
                applyingBrowserZoom = true;
                try
                {
                    foreach (var tab in tabs)
                        if (tab == activeTab || (site != null && SiteZoomStore.Site(tab.View.Source?.AbsoluteUri ?? tab.Url) == site))
                        {
                            tab.ZoomFactor = factor;
                            tab.View.ZoomFactor = factor;
                        }
                }
                finally { applyingBrowserZoom = false; }
            }
            UpdateZoomControls();
            BrowserZoomChanged?.Invoke(this, EventArgs.Empty);
        }

        private double ReadSiteZoom(string url)
        {
            try { return SiteZoomStore.Read(url, appSettings.ZoomFactor); }
            catch (Exception ex) { ReportSiteZoomError(ex); return appSettings.ZoomFactor; }
        }

        private void ReportSiteZoomError(Exception ex)
        {
            (Application.Current as App)?.WriteDiagnostic("Website zoom operation failed", ex);
            MessageBox.Show(this, $"网站缩放设置失败: {ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Warning);
        }

        private void ApplyNavigationZoom(Microsoft.Web.WebView2.Wpf.WebView2 view, string url)
        {
            var tab = FindTab(view);
            if (tab == null) return;
            tab.ZoomFactor = ReadSiteZoom(url);
            applyingBrowserZoom = true;
            try { view.ZoomFactor = tab.ZoomFactor; }
            finally { applyingBrowserZoom = false; }
            if (tab == activeTab)
            {
                currentZoomFactor = tab.ZoomFactor;
                UpdateZoomControls();
                BrowserZoomChanged?.Invoke(this, EventArgs.Empty);
            }
        }

        private void UpdateZoomControls()
        {
            ZoomButton.ToolTip = "调整缩放比例（" + ZoomPercentage.Format(currentZoomFactor) + "%）";
            ZoomPercentInput.Text = ZoomPercentage.Format(currentZoomFactor);
        }

        private void SearchButton_Click(object sender, RoutedEventArgs e) => ShowFindBar();

        private void ZoomButton_Click(object sender, RoutedEventArgs e)
        {
            UpdateZoomControls();
            ZoomPopup.PlacementTarget = ZoomButton.Visibility == Visibility.Visible ? ZoomButton : VisibleMoreButton;
            ZoomPopup.Placement = VisibleMoreButton == TopMoreButton
                ? System.Windows.Controls.Primitives.PlacementMode.Bottom
                : System.Windows.Controls.Primitives.PlacementMode.Top;
            ZoomPopup.IsOpen = !ZoomPopup.IsOpen;
            if (ZoomPopup.IsOpen)
            {
                ZoomPercentInput.Focus();
                ZoomPercentInput.SelectAll();
            }
        }

        private bool ApplyZoomInput()
        {
            if (!ZoomPercentage.TryParse(ZoomPercentInput.Text, out double factor))
            {
                MessageBox.Show("缩放请输入 30–300 的百分比，例如 120 表示 120%。", "错误",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                UpdateZoomControls();
                return false;
            }
            if (factor != currentZoomFactor) SetBrowserZoom(factor);
            return true;
        }

        private void ZoomPercentInput_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter)
            {
                e.Handled = true;
                if (ApplyZoomInput()) ZoomPopup.IsOpen = false;
            }
            else if (e.Key == Key.Escape)
            {
                e.Handled = true;
                UpdateZoomControls();
                ZoomPopup.IsOpen = false;
            }
        }

        private void ZoomPercentInput_LostKeyboardFocus(object sender, KeyboardFocusChangedEventArgs e)
        {
            if (ZoomPopup.IsOpen) ApplyZoomInput();
        }
    }
}
