using System.Buffers.Binary;
using System.IO.Pipes;
using System.Net.Sockets;
using System.Text.Json;

namespace Hibike.Desktop;

/// <summary>
/// Native Discord IPC, matching Kouki-Rust's handshake and SET_ACTIVITY framing.
/// No user token or Discord SDK is needed on the desktop.
/// </summary>
internal sealed class DiscordIpcClient : IDisposable
{
    private readonly CancellationTokenSource _stop = new();
    private Stream? _connection;
    private const int MaxMessageLength = 1024 * 1024;
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
    };

    public async Task SetActivityAsync(DiscordPresence.DiscordActivity? activity)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(_stop.Token);
        timeout.CancelAfter(TimeSpan.FromSeconds(5));
        try
        {
            if (_connection is null) await ConnectAsync(timeout.Token);
            await ExchangeAsync(1, new
            {
                cmd = "SET_ACTIVITY", nonce = Guid.NewGuid().ToString("N"),
                args = new { pid = Environment.ProcessId, activity }
            }, timeout.Token);
        }
        catch
        {
            Reset();
            throw;
        }
    }

    private async Task ConnectAsync(CancellationToken token)
    {
        foreach (var name in Enumerable.Range(0, 10).Select(i => $"discord-ipc-{i}"))
        {
            token.ThrowIfCancellationRequested();
            if (OperatingSystem.IsWindows())
            {
                var pipe = new NamedPipeClientStream(".", name, PipeDirection.InOut, PipeOptions.Asynchronous);
                try
                {
                    await pipe.ConnectAsync(200, token);
                    _connection = pipe;
                    break;
                }
                catch (Exception error) when (error is IOException or TimeoutException)
                {
                    pipe.Dispose();
                }
                catch
                {
                    pipe.Dispose();
                    throw;
                }
            }
            else
            {
                var bases = new[]
                {
                    Environment.GetEnvironmentVariable("XDG_RUNTIME_DIR"),
                    Environment.GetEnvironmentVariable("TMPDIR"),
                    Path.GetTempPath()
                };
                foreach (var directory in bases.Where(s => !string.IsNullOrWhiteSpace(s)).Distinct())
                {
                    var socket = new Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);
                    try
                    {
                        await socket.ConnectAsync(new UnixDomainSocketEndPoint(Path.Combine(directory!, name)), token);
                        _connection = new NetworkStream(socket, ownsSocket: true);
                        break;
                    }
                    catch (Exception error) when (error is SocketException or IOException)
                    {
                        socket.Dispose();
                    }
                    catch
                    {
                        socket.Dispose();
                        throw;
                    }
                }
                if (_connection is not null) break;
            }
        }
        if (_connection is null) throw new IOException("Discord desktop não encontrado.");
        try
        {
            await ExchangeAsync(0, new { v = 1, client_id = DiscordPresence.ApplicationId }, token);
        }
        catch { Reset(); throw; }
    }

    private async Task ExchangeAsync(int opcode, object payload, CancellationToken token)
    {
        var connection = _connection ?? throw new IOException("Discord desconectado.");
        var bytes = JsonSerializer.SerializeToUtf8Bytes(payload, JsonOptions);
        if (bytes.Length > MaxMessageLength) throw new IOException("Atividade Discord muito grande.");
        var header = new byte[8];
        BinaryPrimitives.WriteInt32LittleEndian(header.AsSpan(0, 4), opcode);
        BinaryPrimitives.WriteInt32LittleEndian(header.AsSpan(4, 4), bytes.Length);
        await connection.WriteAsync(header, token);
        await connection.WriteAsync(bytes, token);
        await connection.FlushAsync(token);

        await connection.ReadExactlyAsync(header, token);
        var replyCode = BinaryPrimitives.ReadInt32LittleEndian(header.AsSpan(0, 4));
        var length = BinaryPrimitives.ReadInt32LittleEndian(header.AsSpan(4, 4));
        if (replyCode == 2 || length <= 0 || length > MaxMessageLength)
            throw new IOException("Discord encerrou a conexão ou enviou resposta inválida.");
        var response = new byte[length];
        await connection.ReadExactlyAsync(response, token);
        using var json = JsonDocument.Parse(response);
        if (json.RootElement.TryGetProperty("evt", out var evt) && evt.ValueKind == JsonValueKind.String
            && evt.GetString() == "ERROR")
            throw new IOException("Discord recusou a atividade.");
    }

    private void Reset()
    {
        _connection?.Dispose();
        _connection = null;
    }

    public void Dispose()
    {
        _stop.Cancel();
        Reset();
        _stop.Dispose();
    }
}
