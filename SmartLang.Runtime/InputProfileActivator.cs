using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Text;

namespace SmartLang;

internal interface IInputProfileActivator : IDisposable
{
    bool ActivateKeyboardLayout(
        uint threadId,
        nint targetWindow,
        nint layoutHandle);
}

internal sealed class NativeInputProfileActivator : IInputProfileActivator
{
    private const int WhGetMessage = 3;
    private const uint SmartLangActivateLayout = NativeMethods.WmApp + 0x534;
    private const string Native64FileName = "SmartLang.NativeHook.dll";
    private const string Native32FileName = "SmartLang.NativeHook32.dll";
    private const string Native32HostFileName = "SmartLang.NativeHost32.exe";
    private const string Native64EnvironmentVariable = "SMARTLANG_NATIVE_HOOK_PATH";
    private const string Native32EnvironmentVariable = "SMARTLANG_NATIVE_HOOK32_PATH";

    private NativeHook? _hook64;
    private Process? _host32;
    private string? _host32ShadowDirectory;

    public NativeInputProfileActivator()
    {
        NativeHookShadowCopy.CleanupStaleCopies();

        _hook64 = NativeHook.Install(
            ResolveLibraryPath(Native64FileName, Native64EnvironmentVariable),
            required: true);

        try
        {
            Start32BitHost();
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException or
            InvalidOperationException or Win32Exception)
        {
            AppLog.Write(
                $"Optional 32-bit hook host did not start: {exception.Message}");
        }
    }

    public bool ActivateKeyboardLayout(
        uint threadId,
        nint targetWindow,
        nint layoutHandle)
    {
        if (IsCoreWindow(targetWindow))
        {
            return ActivateCoreWindow(
                threadId,
                targetWindow,
                layoutHandle);
        }

        var accepted = NativeMethods.PostThreadMessage(
            threadId,
            SmartLangActivateLayout,
            layoutHandle,
            0);

        AppLog.Write(
            $"Native layout request: thread={threadId}, HKL=0x{layoutHandle:X}, " +
            $"accepted={accepted}, error={Marshal.GetLastWin32Error()}.");
        return accepted;
    }

    internal static bool IsCoreWindowClass(string className) =>
        string.Equals(
            className,
            "Windows.UI.Core.CoreWindow",
            StringComparison.Ordinal);

    private static bool IsCoreWindow(nint windowHandle)
    {
        var className = new StringBuilder(128);
        return NativeMethods.GetClassName(
            windowHandle,
            className,
            className.Capacity) > 0 &&
            IsCoreWindowClass(className.ToString());
    }

    private static bool ActivateCoreWindow(
        uint threadId,
        nint windowHandle,
        nint layoutHandle)
    {
        var accepted = NativeMethods.PostMessage(
            windowHandle,
            NativeMethods.WmInputLangChangeRequest,
            0,
            layoutHandle);
        AppLog.Write(
            $"CoreWindow layout request: HWND=0x{windowHandle:X}, " +
            $"thread={threadId}, HKL=0x{layoutHandle:X}, accepted={accepted}, " +
            $"error={Marshal.GetLastWin32Error()}.");
        if (!accepted)
        {
            return false;
        }

        for (var attempt = 0; attempt < 20; attempt++)
        {
            Thread.Sleep(10);
            if (NativeMethods.GetKeyboardLayout(threadId) == layoutHandle)
            {
                return true;
            }
        }

        AppLog.Write(
            $"CoreWindow did not activate HKL=0x{layoutHandle:X}.");
        return false;
    }

    public void Dispose()
    {
        if (_host32 is not null)
        {
            try
            {
                if (!_host32.HasExited)
                {
                    _host32.Kill(entireProcessTree: true);
                    _host32.WaitForExit(2000);
                }
            }
            catch (InvalidOperationException)
            {
            }

            _host32.Dispose();
            _host32 = null;
        }

        if (_host32ShadowDirectory is not null)
        {
            NativeHookShadowCopy.TryDelete(_host32ShadowDirectory);
            _host32ShadowDirectory = null;
        }

        _hook64?.Dispose();
        _hook64 = null;
    }

    private void Start32BitHost()
    {
        var hostPath = Path.Combine(AppContext.BaseDirectory, Native32HostFileName);
        var libraryPath = ResolveLibraryPath(
            Native32FileName,
            Native32EnvironmentVariable);
        if (!File.Exists(hostPath) || !File.Exists(libraryPath))
        {
            AppLog.Write(
                $"Optional 32-bit hook host is unavailable. Host={hostPath}, DLL={libraryPath}.");
            return;
        }

        var (shadowDirectory, shadowPath) = NativeHookShadowCopy.Create(libraryPath);
        try
        {
            _host32 = Process.Start(new ProcessStartInfo
            {
                FileName = hostPath,
                Arguments = $"\"{shadowPath}\" {Environment.ProcessId}",
                UseShellExecute = false,
                CreateNoWindow = true,
                WindowStyle = ProcessWindowStyle.Hidden
            });
            if (_host32 is null)
            {
                throw new InvalidOperationException("The 32-bit hook host did not start.");
            }

            if (_host32.WaitForExit(250))
            {
                var exitCode = _host32.ExitCode;
                _host32.Dispose();
                _host32 = null;
                throw new Win32Exception(
                    exitCode,
                    "The 32-bit hook host exited during startup.");
            }

            _host32ShadowDirectory = shadowDirectory;
            AppLog.Write(
                $"32-bit hook host started. PID={_host32.Id}, DLL={shadowPath}.");
        }
        catch
        {
            NativeHookShadowCopy.TryDelete(shadowDirectory);
            throw;
        }
    }

    private static string ResolveLibraryPath(string fileName, string environmentVariable) =>
        Environment.GetEnvironmentVariable(environmentVariable) ??
        Path.Combine(AppContext.BaseDirectory, fileName);

    private sealed class NativeHook : IDisposable
    {
        private readonly string _shadowDirectory;
        private nint _moduleHandle;
        private nint _hookHandle;

        private NativeHook(
            string shadowDirectory,
            nint moduleHandle,
            nint hookHandle)
        {
            _shadowDirectory = shadowDirectory;
            _moduleHandle = moduleHandle;
            _hookHandle = hookHandle;
        }

        public static NativeHook? Install(string libraryPath, bool required)
        {
            string shadowDirectory;
            string shadowPath;
            try
            {
                (shadowDirectory, shadowPath) =
                    NativeHookShadowCopy.Create(libraryPath);
            }
            catch (Exception exception) when (
                !required &&
                exception is IOException or UnauthorizedAccessException)
            {
                AppLog.Write(
                    $"Optional native hook shadow copy failed. Path={libraryPath}, " +
                    $"error={exception.Message}");
                return null;
            }

            var moduleHandle = NativeMethods.LoadLibrary(shadowPath);
            if (moduleHandle == 0)
            {
                var error = Marshal.GetLastWin32Error();
                NativeHookShadowCopy.TryDelete(shadowDirectory);
                if (required)
                {
                    throw new Win32Exception(error, $"Could not load {shadowPath}.");
                }

                AppLog.Write(
                    $"Optional native hook DLL not loaded. Path={shadowPath}, error={error}.");
                return null;
            }

            var hookProcedure = NativeMethods.GetProcAddress(
                moduleHandle,
                "SmartLangGetMessageHook");
            if (hookProcedure == 0)
            {
                var error = Marshal.GetLastWin32Error();
                NativeMethods.FreeLibrary(moduleHandle);
                NativeHookShadowCopy.TryDelete(shadowDirectory);
                if (required)
                {
                    throw new Win32Exception(error, $"Could not find the native hook procedure in {shadowPath}.");
                }

                AppLog.Write(
                    $"Optional native hook procedure missing. Path={shadowPath}, error={error}.");
                return null;
            }

            var hookHandle = NativeMethods.SetWindowsHookEx(
                WhGetMessage,
                hookProcedure,
                moduleHandle,
                0);
            if (hookHandle == 0)
            {
                var error = Marshal.GetLastWin32Error();
                NativeMethods.FreeLibrary(moduleHandle);
                NativeHookShadowCopy.TryDelete(shadowDirectory);
                if (required)
                {
                    throw new Win32Exception(error, $"Could not install the layout activation hook from {shadowPath}.");
                }

                AppLog.Write(
                    $"Optional native hook not installed. Path={shadowPath}, error={error}.");
                return null;
            }

            AppLog.Write(
                $"Native layout hook installed. Source={libraryPath}, shadow={shadowPath}, " +
                $"handle=0x{hookHandle:X}.");
            return new NativeHook(shadowDirectory, moduleHandle, hookHandle);
        }

        public void Dispose()
        {
            if (_hookHandle != 0)
            {
                NativeMethods.UnhookWindowsHookEx(_hookHandle);
                _hookHandle = 0;
            }

            if (_moduleHandle != 0)
            {
                NativeMethods.FreeLibrary(_moduleHandle);
                _moduleHandle = 0;
            }

            NativeHookShadowCopy.TryDelete(_shadowDirectory);
        }
    }
}

internal static class NativeHookShadowCopy {
    private const string DirectoryName = "NativeHooks";

    internal static (string Directory, string LibraryPath) Create(
        string sourcePath) {
        if(!File.Exists(sourcePath)) {
            throw new FileNotFoundException(
                "The native hook library was not found.",
                sourcePath);
        }

        var processId = Environment.ProcessId;
        var directory = Path.Combine(
            GetRootDirectory(),
            $"{processId}-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        File.WriteAllText(
            Path.Combine(directory, ".owner"),
            processId.ToString(CultureInfo.InvariantCulture));

        var destinationPath = Path.Combine(
            directory,
            Path.GetFileName(sourcePath));
        try {
            File.Copy(sourcePath, destinationPath, overwrite: false);
            return (directory, destinationPath);
        } catch {
            TryDelete(directory);
            throw;
        }
    }

    internal static void CleanupStaleCopies() {
        var root = GetRootDirectory();
        if(!Directory.Exists(root)) {
            return;
        }

        foreach(var directory in Directory.EnumerateDirectories(root)) {
            if(IsOwnedByLiveProcess(directory)) {
                continue;
            }

            TryDelete(directory);
        }
    }

    internal static void TryDelete(string directory) {
        try {
            Directory.Delete(directory, recursive: true);
        } catch(IOException) {
        } catch(UnauthorizedAccessException) {
        }
    }

    private static string GetRootDirectory() {
        var baseDirectory = ProcessSecurity.IsCurrentProcessElevated()
            ? Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData)
            : Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        return Path.Combine(baseDirectory, "SmartLang", DirectoryName);
    }

    private static bool IsOwnedByLiveProcess(string directory) {
        var ownerPath = Path.Combine(directory, ".owner");
        if(!File.Exists(ownerPath)) {
            return false;
        }

        try {
            var text = File.ReadAllText(ownerPath);
            if(!int.TryParse(
                text,
                NumberStyles.Integer,
                CultureInfo.InvariantCulture,
                out var processId)) {
                return false;
            }

            using var process = Process.GetProcessById(processId);
            return !process.HasExited;
        } catch(ArgumentException) {
            return false;
        } catch(InvalidOperationException) {
            return false;
        } catch(IOException) {
            return true;
        } catch(UnauthorizedAccessException) {
            return true;
        }
    }
}
