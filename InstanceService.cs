#nullable enable
using System;
using System.IO;
using System.IO.Pipes;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;

namespace TrayChrome
{
    public class InstanceResponse
    {
        public AppGroupEntry? Entry { get; set; }
        public string? Error { get; set; }
        public bool Ready { get; set; }
    }

    public sealed class InstanceService : IDisposable
    {
        private readonly Func<AppGroupEntry> snapshot;
        private readonly Action close;
        private readonly Func<bool> isReady;
        private readonly CancellationTokenSource shutdown = new CancellationTokenSource();

        public InstanceService(Func<AppGroupEntry> snapshot, Func<bool> isReady, Action close)
        {
            this.snapshot = snapshot;
            this.close = close;
            this.isReady = isReady;
        }

        private static string PipeName(int id) => $"TrayChrome.Instance.{id}";

        public void Start() => _ = ServeAsync();

        private async Task ServeAsync()
        {
            try
            {
                while (!shutdown.IsCancellationRequested)
                {
                    using var pipe = new NamedPipeServerStream(PipeName(Environment.ProcessId), PipeDirection.InOut,
                        1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
                    await pipe.WaitForConnectionAsync(shutdown.Token);
                    using var requestTimeout = CancellationTokenSource.CreateLinkedTokenSource(shutdown.Token);
                    requestTimeout.CancelAfter(TimeSpan.FromSeconds(10));
                    using var reader = new StreamReader(pipe, Encoding.UTF8, false, 1024, true);
                    using var writer = new StreamWriter(pipe, new UTF8Encoding(false), 1024, true) { AutoFlush = true };
                    bool shouldClose = false;
                    var response = new InstanceResponse();
                    try
                    {
                        string? command = await reader.ReadLineAsync(requestTimeout.Token);
                        switch (command)
                        {
                            case "snapshot":
                                response.Entry = await Application.Current.Dispatcher.InvokeAsync(snapshot);
                                break;
                            case "close":
                                shouldClose = true;
                                break;
                            case "ping":
                                response.Ready = await Application.Current.Dispatcher.InvokeAsync(isReady);
                                break;
                            default:
                                response.Error = "未知实例命令。";
                                break;
                        }
                        await writer.WriteLineAsync(JsonSerializer.Serialize(response).AsMemory(), requestTimeout.Token);
                    }
                    catch (Exception ex) when (ex is IOException || ex is OperationCanceledException)
                    {
                        System.Diagnostics.Trace.WriteLine($"实例连接已中断: {ex.Message}");
                        continue;
                    }
                    if (shouldClose)
                    {
                        await Application.Current.Dispatcher.InvokeAsync(close);
                        return;
                    }
                }
            }
            catch (OperationCanceledException) when (shutdown.IsCancellationRequested) { }
            catch (Exception ex)
            {
                await Application.Current.Dispatcher.InvokeAsync(() =>
                    MessageBox.Show($"实例通信服务失败: {ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error));
            }
        }

        public static async Task<InstanceResponse> RequestAsync(int id, string command, CancellationToken cancellationToken = default)
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            if (!cancellationToken.CanBeCanceled) timeout.CancelAfter(TimeSpan.FromSeconds(5));
            using var pipe = new NamedPipeClientStream(".", PipeName(id), PipeDirection.InOut,
                PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
            try
            {
                await pipe.ConnectAsync(timeout.Token);
                using var reader = new StreamReader(pipe, Encoding.UTF8, false, 1024, true);
                using var writer = new StreamWriter(pipe, new UTF8Encoding(false), 1024, true) { AutoFlush = true };
                await writer.WriteLineAsync(command.AsMemory(), timeout.Token);
                string json = await reader.ReadLineAsync(timeout.Token)
                    ?? throw new IOException("实例连接已关闭。");
                var response = JsonSerializer.Deserialize<InstanceResponse>(json)
                    ?? throw new InvalidDataException("实例响应为空。");
                if (response.Error != null) throw new InvalidOperationException(response.Error);
                return response;
            }
            catch (Exception ex) when (ex is IOException || ex is OperationCanceledException || ex is TimeoutException)
            {
                throw new IOException($"无法连接实例 {id}，请确认所有实例均已更新至当前版本。", ex);
            }
        }

        public void Dispose() => shutdown.Cancel();
    }
}
