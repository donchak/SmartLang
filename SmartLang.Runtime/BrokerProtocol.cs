using System.Buffers.Binary;
using System.IO.Pipes;
using System.Security.AccessControl;
using System.Security.Cryptography;
using System.Security.Principal;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace SmartLang;

public enum BrokerCommand
{
    GetStatus,
    SaveSettings,
    ActivateHooks,
    ConfigureStartup,
    Stop
}

public sealed record BrokerRequest(
    int ProtocolVersion,
    Guid RequestId,
    BrokerCommand Command,
    AppSettings? Settings = null);

public sealed record BrokerResponse(
    int ProtocolVersion,
    Guid RequestId,
    bool Success,
    BrokerStatus Status,
    string? Error = null);

public sealed record BrokerStatus(
    bool IsElevated,
    bool HooksActive,
    string Version,
    string? LastError = null);

public static class BrokerProtocol
{
    public const int CurrentVersion = 1;
    public const int MaximumMessageLength = 64 * 1024;

    static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new JsonStringEnumConverter() }
    };

    public static string PipeName
    {
        get
        {
            var sid = WindowsIdentity.GetCurrent().User?.Value ?? Environment.UserName;
            var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(sid)))[..16];
            var sessionId = System.Diagnostics.Process.GetCurrentProcess().SessionId;
            return $"SmartLang.Broker.{sessionId}.{hash}";
        }
    }

    public static async Task WriteAsync<T>(
        Stream stream,
        T message,
        CancellationToken cancellationToken)
    {
        var payload = JsonSerializer.SerializeToUtf8Bytes(message, JsonOptions);
        if (payload.Length > MaximumMessageLength)
        {
            throw new InvalidDataException("Broker message exceeds the size limit.");
        }

        var header = new byte[sizeof(int)];
        BinaryPrimitives.WriteInt32LittleEndian(header, payload.Length);
        await stream.WriteAsync(header, cancellationToken);
        await stream.WriteAsync(payload, cancellationToken);
        await stream.FlushAsync(cancellationToken);
    }

    public static async Task<T> ReadAsync<T>(
        Stream stream,
        CancellationToken cancellationToken)
    {
        var header = new byte[sizeof(int)];
        await stream.ReadExactlyAsync(header, cancellationToken);
        var length = BinaryPrimitives.ReadInt32LittleEndian(header);
        if (length is <= 0 or > MaximumMessageLength)
        {
            throw new InvalidDataException("Broker message has an invalid length.");
        }

        var payload = new byte[length];
        await stream.ReadExactlyAsync(payload, cancellationToken);
        return JsonSerializer.Deserialize<T>(payload, JsonOptions)
            ?? throw new InvalidDataException("Broker message could not be deserialized.");
    }
}

public sealed class BrokerPipeServer: IAsyncDisposable {
    readonly Func<BrokerRequest, CancellationToken, Task<BrokerResponse>> handler;
    readonly CancellationTokenSource stop = new();
    Task? listenTask;

    public BrokerPipeServer(
        Func<BrokerRequest, CancellationToken, Task<BrokerResponse>> handler) {
        this.handler = handler;
    }

    public void Start() {
        listenTask ??= Task.Run(() => ListenAsync(stop.Token));
    }

    public async ValueTask DisposeAsync() {
        await stop.CancelAsync();
        if(listenTask is not null) {
            try {
                await listenTask;
            } catch(OperationCanceledException) {
            }
        }

        stop.Dispose();
    }

    async Task ListenAsync(CancellationToken cancellationToken) {
        while(!cancellationToken.IsCancellationRequested) {
            var userSid = WindowsIdentity.GetCurrent().User
                ?? throw new InvalidOperationException("The current Windows user SID is unavailable.");
            var pipeSecurity = new PipeSecurity();
            pipeSecurity.SetAccessRuleProtection(isProtected: true, preserveInheritance: false);
            pipeSecurity.SetOwner(userSid);
            pipeSecurity.AddAccessRule(new PipeAccessRule(userSid, PipeAccessRights.FullControl, AccessControlType.Allow));

            await using var pipe = NamedPipeServerStreamAcl.Create(
                BrokerProtocol.PipeName,
                PipeDirection.InOut,
                1,
                PipeTransmissionMode.Byte,
                PipeOptions.Asynchronous,
                0,
                0,
                pipeSecurity);
            await pipe.WaitForConnectionAsync(cancellationToken);

            try {
                var request = await BrokerProtocol.ReadAsync<BrokerRequest>(pipe, cancellationToken);
                BrokerResponse response;
                if(request.ProtocolVersion != BrokerProtocol.CurrentVersion) {
                    response = new BrokerResponse(
                        BrokerProtocol.CurrentVersion,
                        request.RequestId,
                        false,
                        new BrokerStatus(false, false, string.Empty),
                        "Unsupported broker protocol version.");
                } else {
                    try {
                        response = await handler(request, cancellationToken);
                    } catch(Exception exception) {
                        AppLog.Write($"Broker request handler failed with " + $"{exception.GetType().Name}: {exception.Message}");
                        response = new BrokerResponse(
                            BrokerProtocol.CurrentVersion,
                            request.RequestId,
                            false,
                            new BrokerStatus(false, false, string.Empty),
                            "The broker could not process the request.");
                    }
                }

                await BrokerProtocol.WriteAsync(pipe, response, cancellationToken);
            } catch(Exception exception) when(
                  exception is IOException or InvalidDataException or JsonException) {
                AppLog.Write($"Broker pipe request failed: {exception.Message}");
            }
        }
    }
}
