using System.IO.Pipes;
using System.Text;
using GamepadNav.Core;

namespace GamepadNav.Service;

/// <summary>
/// Named pipe server that streams status to the tray app and receives commands.
/// </summary>
public sealed class IpcServer : IDisposable
{
    private readonly CancellationTokenSource _cts = new();
    private readonly ILogger<IpcServer> _logger;
    private Task? _listenTask;
    private NamedPipeServerStream? _currentPipe;
    private StreamWriter? _writer;
    private readonly object _writeLock = new();

    public event Action<CommandMessage>? CommandReceived;

    public IpcServer(ILogger<IpcServer> logger)
    {
        _logger = logger;
    }

    public void Start()
    {
        _listenTask = Task.Run(ListenLoop);
    }

    /// <summary>
    /// Sends a command to the tray app. Non-blocking.
    /// </summary>
    public void SendCommand(string action)
    {
        var json = IpcProtocol.Serialize(new CommandMessage { Action = action });
        ThreadPool.QueueUserWorkItem(_ => WriteMessage(json));
    }

    /// <summary>
    /// Sends controller state to the tray app for keyboard overlay navigation. Non-blocking.
    /// </summary>
    public void SendControllerState(ControllerState state)
    {
        var msg = new ControllerStateMessage { Buttons = (ushort)state.Buttons };
        var json = IpcProtocol.Serialize(msg);
        ThreadPool.QueueUserWorkItem(_ => WriteMessage(json));
    }

    /// <summary>
    /// Sends a status update to the tray app. Non-blocking.
    /// </summary>
    public void SendStatus(StatusMessage status)
    {
        var json = IpcProtocol.Serialize(status);
        ThreadPool.QueueUserWorkItem(_ => WriteMessage(json));
    }

    private void WriteMessage(string json)
    {
        lock (_writeLock)
        {
            if (_writer == null) return;
            try { _writer.WriteLine(json); }
            catch { }
        }
    }

    private async Task ListenLoop()
    {
        while (!_cts.IsCancellationRequested)
        {
            try
            {
                var pipe = new NamedPipeServerStream(
                    IpcProtocol.PipeName,
                    PipeDirection.InOut,
                    1,
                    PipeTransmissionMode.Byte,
                    PipeOptions.Asynchronous);

                _currentPipe = pipe;
                _logger.LogInformation("IPC: waiting for tray app connection...");

                await pipe.WaitForConnectionAsync(_cts.Token);
                _logger.LogInformation("IPC: tray app connected");

                var reader = new StreamReader(pipe, Encoding.UTF8);
                lock (_writeLock)
                {
                    _writer = new StreamWriter(pipe, Encoding.UTF8) { AutoFlush = true };
                }

                // Read commands from the tray app
                while (pipe.IsConnected && !_cts.IsCancellationRequested)
                {
                    var line = await reader.ReadLineAsync(_cts.Token);
                    if (line == null) break; // Disconnected

                    var msg = IpcProtocol.Deserialize(line);
                    if (msg is CommandMessage cmd)
                        CommandReceived?.Invoke(cmd);
                }

                _logger.LogInformation("IPC: tray app disconnected");
            }
            catch (OperationCanceledException) { break; }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "IPC: pipe error, will retry");
            }
            finally
            {
                lock (_writeLock) _writer = null;
                _currentPipe?.Dispose();
                _currentPipe = null;
            }
        }
    }

    public void Dispose()
    {
        _cts.Cancel();
        _currentPipe?.Dispose();
        _cts.Dispose();
    }
}
