using System;
using System.Diagnostics;
using System.IO;
using System.IO.Pipes;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace ClipShelfShared;

internal sealed record UiRequest(string Command, long Target = 0, int TargetPid = 0, long Started = 0, int Left = 0, int Top = 0, string? Message = null);

// One bounded, authenticated transport. Every request carries its own target; no
// shared target file or clipboard is used for the hand-off.
internal static class UiChannel
{
    public static string Name => "ClipShelf.UI." + (OperatingSystem.IsWindows() ? SessionId() : Environment.ProcessId);
    private static int SessionId() { using var process = Process.GetCurrentProcess(); return process.SessionId; }
    public static async Task SendAsync(UiRequest request, CancellationToken cancellationToken = default)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(5000);
        using var pipe = new NamedPipeClientStream(".", Name, PipeDirection.InOut, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
        await pipe.ConnectAsync(timeout.Token);
        using var writer = new StreamWriter(pipe, leaveOpen: true) { AutoFlush = true };
        using var reader = new StreamReader(pipe, leaveOpen: true);
        await writer.WriteLineAsync(JsonSerializer.Serialize(request).AsMemory(), timeout.Token);
        var reply = await reader.ReadLineAsync(timeout.Token);
        if (reply != "OK") throw new IOException(reply ?? "介面沒有回應");
    }
}

internal sealed class UiServer : IDisposable
{
    private readonly CancellationTokenSource _stop = new();
    public UiServer(Func<UiRequest, Task> handler) => _ = Run(handler);
    private async Task Run(Func<UiRequest, Task> handler)
    {
        while (!_stop.IsCancellationRequested)
        {
            try
            {
                using var pipe = new NamedPipeServerStream(UiChannel.Name, PipeDirection.InOut, 1,
                    PipeTransmissionMode.Byte, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
                await pipe.WaitForConnectionAsync(_stop.Token);
                using var timeout = CancellationTokenSource.CreateLinkedTokenSource(_stop.Token);
                timeout.CancelAfter(5000);
                using var reader = new StreamReader(pipe, leaveOpen: true);
                using var writer = new StreamWriter(pipe, leaveOpen: true) { AutoFlush = true };
                var line = await reader.ReadLineAsync(timeout.Token);
                if (line == null || line.Length > 4096) continue;
                try
                {
                    var request = JsonSerializer.Deserialize<UiRequest>(line) ?? throw new InvalidDataException();
                    await handler(request);
                    await writer.WriteLineAsync("OK");
                }
                catch (Exception ex) { await writer.WriteLineAsync("ERROR: " + ex.Message); }
            }
            catch (OperationCanceledException) { }
            catch (IOException) { }
        }
    }
    public void Dispose() => _stop.Cancel();
}
