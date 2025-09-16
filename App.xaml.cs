using System;
using System.Diagnostics;
using System.Windows;
using Hardcodet.Wpf.TaskbarNotification;

namespace TrayChrome
{
    public partial class App : Application
    {
        private TaskbarIcon? trayIcon;
        private MainWindow? mainWindow;
        private static int instanceCounter = 0;
        private int currentInstanceId;

        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);

            // 分配实例ID
            currentInstanceId = ++instanceCounter;

            // 创建托盘图标
            trayIcon = (TaskbarIcon)FindResource("TrayIcon");
            
            // 更新托盘图标标识
            if (trayIcon != null)
            {
                trayIcon.ToolTipText = $"Tray Chrome Browser - 实例 {currentInstanceId}";
            }
            
            // 创建主窗口但不显示
            mainWindow = new MainWindow();
            mainWindow.Hide();
            
            // 订阅标题变化事件
            mainWindow.TitleChanged += OnMainWindowTitleChanged;
            
            // 隐藏主窗口，只显示托盘图标
            MainWindow = mainWindow;
            MainWindow.WindowState = WindowState.Minimized;
            MainWindow.ShowInTaskbar = false;
        }

        private void TrayIcon_TrayLeftMouseUp(object sender, RoutedEventArgs e)
        {
            if (mainWindow != null)
            {
                if (mainWindow.IsVisible)
                {
                    mainWindow.HideWithAnimation();
                }
                else
                {
                    mainWindow.ShowWithAnimation();
                    mainWindow.WindowState = WindowState.Normal;
                    mainWindow.Activate();
                }
            }
        }

        private void AddInstance_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                // 启动新的应用程序实例
                string currentExecutable = Process.GetCurrentProcess().MainModule?.FileName ?? "";
                if (!string.IsNullOrEmpty(currentExecutable))
                {
                    Process.Start(currentExecutable);
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"启动新实例失败: {ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void TrayIcon_TrayMiddleMouseUp(object sender, RoutedEventArgs e)
        {
            // 鼠标中键点击关闭实例
            Application.Current.Shutdown();
        }

        private void RestartInstance_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                // 启动新的应用程序实例
                string currentExecutable = Process.GetCurrentProcess().MainModule?.FileName ?? "";
                if (!string.IsNullOrEmpty(currentExecutable))
                {
                    Process.Start(currentExecutable);
                }
                
                // 关闭当前实例
                Application.Current.Shutdown();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"重启实例失败: {ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void CloseInstance_Click(object sender, RoutedEventArgs e)
        {
            // 关闭当前实例
            Application.Current.Shutdown();
        }

        private void OnMainWindowTitleChanged(string title)
        {
            if (trayIcon != null)
            {
                // 更新托盘图标的提示文本，只显示当前网页标题
                trayIcon.ToolTipText = title;
            }
        }

        protected override void OnExit(ExitEventArgs e)
        {
            // 取消订阅事件
            if (mainWindow != null)
            {
                mainWindow.TitleChanged -= OnMainWindowTitleChanged;
            }
            
            trayIcon?.Dispose();
            base.OnExit(e);
        }
    }
}