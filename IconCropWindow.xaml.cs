#nullable enable
using System;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media.Imaging;

namespace TrayChrome
{
    public partial class IconCropWindow : Window
    {
        private readonly BitmapSource source;
        private readonly double scale;
        private double selectionX;
        private double selectionY;
        private double selectionSize;
        private Point dragOffset;
        private bool dragging;
        private bool ready;
        public BitmapSource? CroppedImage { get; private set; }

        public IconCropWindow(BitmapSource image)
        {
            InitializeComponent();
            source = image;
            scale = Math.Min(320.0 / image.PixelWidth, 320.0 / image.PixelHeight);
            CropCanvas.Width = image.PixelWidth * scale;
            CropCanvas.Height = image.PixelHeight * scale;
            SourceImage.Source = image;
            SourceImage.Width = CropCanvas.Width;
            SourceImage.Height = CropCanvas.Height;
            selectionSize = Math.Min(image.PixelWidth, image.PixelHeight);
            selectionX = (image.PixelWidth - selectionSize) / 2;
            selectionY = (image.PixelHeight - selectionSize) / 2;
            ready = true;
            UpdateSelection();
        }

        private Int32Rect CropBounds()
        {
            int size = Math.Clamp((int)Math.Round(selectionSize), 1, Math.Min(source.PixelWidth, source.PixelHeight));
            int x = Math.Clamp((int)Math.Round(selectionX), 0, source.PixelWidth - size);
            int y = Math.Clamp((int)Math.Round(selectionY), 0, source.PixelHeight - size);
            return new Int32Rect(x, y, size, size);
        }

        private void UpdateSelection()
        {
            selectionX = Math.Clamp(selectionX, 0, source.PixelWidth - selectionSize);
            selectionY = Math.Clamp(selectionY, 0, source.PixelHeight - selectionSize);
            System.Windows.Controls.Canvas.SetLeft(SelectionRectangle, selectionX * scale);
            System.Windows.Controls.Canvas.SetTop(SelectionRectangle, selectionY * scale);
            SelectionRectangle.Width = selectionSize * scale;
            SelectionRectangle.Height = selectionSize * scale;
            var bounds = CropBounds();
            PreviewImage.Source = new CroppedBitmap(source, bounds);
            CropSizeText.Text = $"{bounds.Width} x {bounds.Height} px";
        }

        private void SizeSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (!ready) return;
            double size = Math.Max(1, Math.Min(source.PixelWidth, source.PixelHeight) * SizeSlider.Value / 100);
            selectionX += (selectionSize - size) / 2;
            selectionY += (selectionSize - size) / 2;
            selectionSize = size;
            UpdateSelection();
        }

        private void CropCanvas_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            var point = e.GetPosition(CropCanvas);
            double x = point.X / scale;
            double y = point.Y / scale;
            if (x < selectionX || x > selectionX + selectionSize || y < selectionY || y > selectionY + selectionSize)
            {
                selectionX = x - selectionSize / 2;
                selectionY = y - selectionSize / 2;
                UpdateSelection();
            }
            dragOffset = new Point(x - selectionX, y - selectionY);
            dragging = CropCanvas.CaptureMouse();
            e.Handled = true;
        }

        private void CropCanvas_MouseMove(object sender, MouseEventArgs e)
        {
            if (!dragging || e.LeftButton != MouseButtonState.Pressed) return;
            var point = e.GetPosition(CropCanvas);
            selectionX = point.X / scale - dragOffset.X;
            selectionY = point.Y / scale - dragOffset.Y;
            UpdateSelection();
        }

        private void CropCanvas_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            dragging = false;
            CropCanvas.ReleaseMouseCapture();
        }

        private void Confirm_Click(object sender, RoutedEventArgs e)
        {
            var result = new CroppedBitmap(source, CropBounds());
            result.Freeze();
            CroppedImage = result;
            DialogResult = true;
        }
    }
}
