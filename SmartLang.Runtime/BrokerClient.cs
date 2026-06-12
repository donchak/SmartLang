using System.ComponentModel;
using System.Diagnostics;
using System.IO.Pipes;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace SmartLang;

public sealed class BrokerClient {
    readonly string expectedExecutablePath;
    readonly string expectedVersion;

    public BrokerClient(string expectedExecutablePath, string expectedVersion) {
        this.expectedExecutablePath = Path.GetFullPath(expectedExecutablePath);
        this.expectedVersion = expectedVersion;
    }

    public async Task<BrokerResponse> SendAsync(
        BrokerCommand command,
        AppSettings? settings = null,
        TimeSpan? timeout = null) {
        using var cancellation = new CancellationTokenSource(timeout ?? TimeSpan.FromSeconds(2));
        await using var pipe = new NamedPipeClientStream(
            ".",
            BrokerProtocol.PipeName,
            PipeDirection.InOut,
            // The server ACL restricts access to this user. CurrentUserOnly
            // additionally rejects a pipe owned by the user's elevated token.
            PipeOptions.Asynchronous);
        await pipe.ConnectAsync(cancellation.Token);
        VerifyServer(pipe.SafePipeHandle);

        var request = new BrokerRequest(BrokerProtocol.CurrentVersion, Guid.NewGuid(), command, settings?.Copy());
        await BrokerProtocol.WriteAsync(pipe, request, cancellation.Token);
        var response = await BrokerProtocol.ReadAsync<BrokerResponse>(pipe, cancellation.Token);

        if(response.ProtocolVersion != BrokerProtocol.CurrentVersion ||
            response.RequestId != request.RequestId) {
            throw new InvalidDataException("Broker response identity is invalid.");
        }

        if(!string.Equals(response.Status.Version, expectedVersion, StringComparison.Ordinal)) {
            throw new InvalidDataException("The SmartLang broker version does not match the tray.");
        }

        return response;
    }

    void VerifyServer(SafePipeHandle pipeHandle) {
        if(!NativeMethods.GetNamedPipeServerProcessId(pipeHandle, out var processId)) {
            throw new Win32Exception(Marshal.GetLastWin32Error(), "Could not identify the SmartLang broker process.");
        }

        using var processHandle = NativeMethods.OpenProcess(NativeMethods.ProcessQueryLimitedInformation, false, processId);
        if(processHandle.IsInvalid) {
            throw new Win32Exception(Marshal.GetLastWin32Error(), "Could not inspect the SmartLang broker process.");
        }

        var path = NativeMethods.QueryProcessImagePath(processHandle);
        if(!string.Equals(Path.GetFullPath(path), expectedExecutablePath, StringComparison.OrdinalIgnoreCase)) {
            throw new InvalidDataException("The pipe server is not the installed SmartLang broker.");
        }

        if(!NativeMethods.IsProcessElevated(processHandle)) {
            throw new InvalidDataException("The SmartLang broker is not elevated.");
        }

        if(!NativeMethods.ProcessIdToSessionId(processId, out var sessionId) ||
            sessionId != (uint)Process.GetCurrentProcess().SessionId) {
            throw new InvalidDataException("The SmartLang broker is running in another session.");
        }
    }
}
