#nullable enable
using System;
using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Media.Imaging;
using Microsoft.Win32;

namespace TrayChrome
{
    public class IconSettings
    {
        public const string DefaultPath = "pack://application:,,,/Resources/Ampeross-Ampola-Chrome.ico";
        public string IconType { get; set; } = "default";
        public string IconPath { get; set; } = DefaultPath;
        private static string FilePath => Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "icon-settings.json");

        public static IconSettings Load()
        {
            return File.Exists(FilePath)
                ? JsonSerializer.Deserialize<IconSettings>(File.ReadAllText(FilePath))
                    ?? throw new InvalidDataException("图标设置文件为空。")
                : new IconSettings();
        }

        public void Save()
        {
            File.WriteAllText(FilePath, JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true }));
        }

        public static void ValidatePath(string path)
        {
            LoadImage(path);
        }

        public static string ImportFile(string path)
        {
            ValidatePath(path);
            string directory = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Resources", "custom-icons");
            Directory.CreateDirectory(directory);
            string target = Path.Combine(directory, $"custom_{Guid.NewGuid():N}{Path.GetExtension(path)}");
            File.Copy(path, target);
            return target;
        }

        public static string? SelectAndImport(Window? owner)
        {
            var app = Application.Current as App;
            app?.WriteDiagnostic("Icon picker: creating file dialog");
            var picker = new OpenFileDialog
            {
                Title = "选择图标文件",
                Filter = "PNG图片 (*.png)|*.png|图标文件 (*.ico)|*.ico",
                CheckFileExists = true
            };
            app?.WriteDiagnostic("Icon picker: showing file dialog");
            if ((owner == null ? picker.ShowDialog() : picker.ShowDialog(owner)) != true)
            {
                app?.WriteDiagnostic("Icon picker: canceled");
                return null;
            }
            var imported = ImportFile(picker.FileName);
            app?.WriteDiagnostic("Icon picker: image imported without cropping");
            return imported;

#if false // 暂停裁剪流程，排查自定义图标闪退。
            app?.WriteDiagnostic("Icon picker: decoding selected image");
            var image = LoadImage(picker.FileName);
            app?.WriteDiagnostic($"Icon picker: decoded {image.PixelWidth}x{image.PixelHeight}");
            if (image.PixelWidth == image.PixelHeight)
            {
                var imported = ImportFile(picker.FileName);
                app?.WriteDiagnostic("Icon picker: square image imported");
                return imported;
            }

            app?.WriteDiagnostic("Icon picker: creating crop window");
            var crop = new IconCropWindow(image);
            if (owner != null && owner.IsVisible)
            {
                crop.Owner = owner;
                crop.WindowStartupLocation = WindowStartupLocation.CenterOwner;
            }
            app?.WriteDiagnostic("Icon picker: showing crop window");
            if (crop.ShowDialog() != true)
            {
                app?.WriteDiagnostic("Icon picker: crop canceled");
                return null;
            }
            var croppedImage = crop.CroppedImage ?? throw new InvalidOperationException("裁剪结果为空。");
            string directory = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Resources", "custom-icons");
            Directory.CreateDirectory(directory);
            string target = Path.Combine(directory, $"custom_{Guid.NewGuid():N}.png");
            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(croppedImage));
            using (var stream = File.Create(target)) encoder.Save(stream);
            app?.WriteDiagnostic("Icon picker: cropped PNG saved");
            return target;
#endif
        }

        public static BitmapSource LoadImage(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
                throw new InvalidDataException("请选择图标文件。");
            string extension = Path.GetExtension(path);
            if (!extension.Equals(".png", StringComparison.OrdinalIgnoreCase)
                && !extension.Equals(".ico", StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("图标仅支持 PNG 或 ICO 文件。");

            BitmapFrame image;
            if (path.StartsWith("pack://application:,,,/", StringComparison.Ordinal))
            {
                var resource = Application.GetResourceStream(new Uri(path))
                    ?? throw new FileNotFoundException("找不到图标资源。", path);
                using (resource.Stream)
                    image = BitmapFrame.Create(resource.Stream, BitmapCreateOptions.None, BitmapCacheOption.OnLoad);
            }
            else
            {
                var file = new FileInfo(path);
                if (!file.Exists) throw new FileNotFoundException("找不到图标文件。", path);
                if (file.Length > 5 * 1024 * 1024) throw new InvalidDataException("图标文件必须小于 5MB。");
                using var stream = File.OpenRead(path);
                image = BitmapFrame.Create(stream, BitmapCreateOptions.None, BitmapCacheOption.OnLoad);
            }
            image.Freeze();
            return image;
        }
    }
}
