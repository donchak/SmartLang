using System.ComponentModel;
using System.Runtime.InteropServices;

namespace SmartLang;

internal interface IInputProfileActivator : IDisposable
{
    bool ActivateKeyboardLayout(uint threadId, nint layoutHandle);
}

internal sealed class NativeInputProfileActivator : IInputProfileActivator
{
    private const int WhGetMessage = 3;
    private const uint SmartLangActivateLayout = NativeMethods.WmApp + 0x534;
    private const string Native64FileName = "SmartLang.NativeHook.dll";
    private const string Native32FileName = "SmartLang.NativeHook32.dll";
    private const string Native64EnvironmentVariable = "SMARTLANG_NATIVE_HOOK_PATH";
    private const string Native32EnvironmentVariable = "SMARTLANG_NATIVE_HOOK32_PATH";

    private NativeHook? _hook64;
    private NativeHook? _hook32;

    public NativeInputProfileActivator()
    {
        _hook64 = NativeHook.Install(
            ResolveLibraryPath(Native64FileName, Native64EnvironmentVariable),
            required: true);

        try
        {
            _hook32 = NativeHook.Install(
                ResolveLibraryPath(Native32FileName, Native32EnvironmentVariable),
                required: false);
        }
        catch
        {
            _hook64?.Dispose();
            _hook64 = null;
            throw;
        }
    }

    public bool ActivateKeyboardLayout(uint threadId, nint layoutHandle)
    {
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

    public void Dispose()
    {
        _hook32?.Dispose();
        _hook32 = null;
        _hook64?.Dispose();
        _hook64 = null;
    }

    private static string ResolveLibraryPath(string fileName, string environmentVariable) =>
        Environment.GetEnvironmentVariable(environmentVariable) ??
        Path.Combine(AppContext.BaseDirectory, fileName);

    private sealed class NativeHook : IDisposable
    {
        private nint _moduleHandle;
        private nint _hookHandle;

        private NativeHook(nint moduleHandle, nint hookHandle)
        {
            _moduleHandle = moduleHandle;
            _hookHandle = hookHandle;
        }

        public static NativeHook? Install(string libraryPath, bool required)
        {
            var moduleHandle = NativeMethods.LoadLibrary(libraryPath);
            if (moduleHandle == 0)
            {
                var error = Marshal.GetLastWin32Error();
                if (required)
                {
                    throw new Win32Exception(error, $"Could not load {libraryPath}.");
                }

                AppLog.Write(
                    $"Optional native hook DLL not loaded. Path={libraryPath}, error={error}.");
                return null;
            }

            var hookProcedure = NativeMethods.GetProcAddress(
                moduleHandle,
                "SmartLangGetMessageHook");
            if (hookProcedure == 0)
            {
                var error = Marshal.GetLastWin32Error();
                NativeMethods.FreeLibrary(moduleHandle);
                if (required)
                {
                    throw new Win32Exception(error, $"Could not find the native hook procedure in {libraryPath}.");
                }

                AppLog.Write(
                    $"Optional native hook procedure missing. Path={libraryPath}, error={error}.");
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
                if (required)
                {
                    throw new Win32Exception(error, $"Could not install the layout activation hook from {libraryPath}.");
                }

                AppLog.Write(
                    $"Optional native hook not installed. Path={libraryPath}, error={error}.");
                return null;
            }

            AppLog.Write(
                $"Native layout hook installed. Path={libraryPath}, handle=0x{hookHandle:X}.");
            return new NativeHook(moduleHandle, hookHandle);
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
        }
    }
}
