#nullable enable
using System;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using System.Windows;

namespace TrayChrome
{
    public partial class App
    {
        private readonly object logLock = new object();
        public string LogFilePath { get; } = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "TrayChrome", "logs", $"TrayChrome-{DateTime.Now:yyyyMMdd}-{Environment.ProcessId}.log");

        public App()
        {
            DispatcherUnhandledException += (sender, e) =>
            {
                WriteDiagnostic("Unhandled UI exception", e.Exception);
                // 不继续运行状态不确定的窗口；写入日志后保留默认终止行为。
            };
            AppDomain.CurrentDomain.UnhandledException += (sender, e) =>
                WriteDiagnostic("Unhandled process exception", e.ExceptionObject as Exception);
            TaskScheduler.UnobservedTaskException += (sender, e) =>
                WriteDiagnostic("Unobserved task exception", e.Exception);
        }

        internal void WriteDiagnostic(string message, Exception? exception = null)
        {
            string entry = $"{DateTimeOffset.Now:O} [PID {Environment.ProcessId}] {message}"
                + (exception == null ? string.Empty : Environment.NewLine + exception)
                + Environment.NewLine;
            Trace.WriteLine(entry);
            try
            {
                lock (logLock)
                {
                    string directory = Path.GetDirectoryName(LogFilePath)
                        ?? throw new InvalidOperationException("Diagnostic log directory is missing.");
                    Directory.CreateDirectory(directory);
                    File.AppendAllText(LogFilePath, entry);
                }
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
            {
                // 日志写入失败不能替换正在报告的原始异常。
                Trace.WriteLine($"Unable to write diagnostic log '{LogFilePath}': {ex}");
            }
        }
    }
}
