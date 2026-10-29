using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Media;

namespace TrayChrome
{
    public partial class MainWindow
    {
        protected override void OnDpiChanged(DpiScale oldDpi, DpiScale newDpi)
        {
            base.OnDpiChanged(oldDpi, newDpi);
            Dispatcher.BeginInvoke(new Action(ApplyRoundedCorners), System.Windows.Threading.DispatcherPriority.Loaded);
        }

        [DllImport("gdi32.dll")]
        private static extern IntPtr CreateRoundRectRgn(int left, int top, int right, int bottom, int ellipseWidth, int ellipseHeight);

        [DllImport("user32.dll")]
        private static extern int SetWindowRgn(IntPtr window, IntPtr region, bool redraw);

        [DllImport("gdi32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool DeleteObject(IntPtr handle);

        private void ApplyBrowserCorners(double radius)
        {
            foreach (var tab in tabs)
            {
                var view = tab.View;
                if (view.Handle == IntPtr.Zero || !view.IsVisible || view.ActualWidth <= 0 || view.ActualHeight <= 0) continue;
                var offset = view.TranslatePoint(new Point(), MainContentGrid);
                var dpi = VisualTreeHelper.GetDpi(view);
                IntPtr region = radius <= 0 ? IntPtr.Zero : CreateRoundRectRgn(
                    (int)Math.Round(-offset.X * dpi.DpiScaleX),
                    (int)Math.Round(-offset.Y * dpi.DpiScaleY),
                    (int)Math.Round((MainContentGrid.ActualWidth - offset.X) * dpi.DpiScaleX),
                    (int)Math.Round((MainContentGrid.ActualHeight - offset.Y) * dpi.DpiScaleY),
                    (int)Math.Round(radius * 2 * dpi.DpiScaleX),
                    (int)Math.Round(radius * 2 * dpi.DpiScaleY));
                if (radius > 0 && region == IntPtr.Zero)
                {
                    (Application.Current as App)?.WriteDiagnostic("Unable to create browser rounded region.");
                    continue;
                }
                if (SetWindowRgn(view.Handle, region, true) == 0)
                {
                    if (region != IntPtr.Zero) DeleteObject(region);
                    (Application.Current as App)?.WriteDiagnostic("Unable to apply browser rounded region.");
                }
            }
        }
    }
}
