using System.IO;
using System.IO.Pipes;
using System.Text;
using GamepadNav.Core;

namespace GamepadNav.App;

/// <summary>
/// Named pipe client that connects to the GamepadNav service.
/// </summary>
public sealed class IpcClient : IDisposable
{
    private readonly CancellationTokenSource _cts = new();
    private Task? _readTask;
    private NamedPipeClientStream? _pipe;
    private StreamWriter? _writer;
    private readonly object _writeLock = new();

    public event Action<StatusMessage>? StatusReceived;

    public void Start()
    {
        _readTask = Task.Run(ReadLoop);
    }

    public void SendCommand(string action)
    {
        lock (_writeLock)
        {
            if (_writer == null) return;
            try
            {
                var msg = IpcProtocol.Serialize(new CommandMessage { Action = action });
                _writer.WriteLine(msg);
                _writer.Flush();
            }
            catch { /* Will reconnect */ }
        }
    }

    private async Task ReadLoop()
    {
        while (!_cts.IsCancellationRequested)
        {
            try
            {
                _pipe = new NamedPipeClientStream(".", IpcProtocol.PipeName, PipeDirection.InOut, PipeOptions.Asynchronous);
                await _pipe.ConnectAsync(5000, _cts.Token);

                var reader = new StreamReader(_pipe, Encoding.UTF8);
                lock (_writeLock)
                {
                    _writer = new StreamWriter(_pipe, Encoding.UTF8) { AutoFlush = true };
                }

                while (_pipe.IsConnected && !_cts.IsCancellationRequested)
                {
                    var line = await reader.ReadLineAsync(_cts.Token);
                    if (line == null) break;

                    var msg = IpcProtocol.Deserialize(line);
                    if (msg is StatusMessage status)
                        StatusReceived?.Invoke(status);
                }
            }
            catch (TimeoutException) { }
            catch (OperationCanceledException) { break; }
            catch { }
            finally
            {
                lock (_writeLock) _writer = null;
                _pipe?.Dispose();
                _pipe = null;
            }

            // Wait before reconnecting
            try { await Task.Delay(2000, _cts.Token); }
            catch (OperationCanceledException) { break; }
        }
    }

    public void Dispose()
    {
        _cts.Cancel();
        _pipe?.Dispose();
        _cts.Dispose();
    }
}
