using System.ComponentModel;
using System.Runtime.InteropServices;

namespace SmartLang;

public sealed class KeyboardHook : IDisposable
{
    private readonly KeyboardShortcutEngine _engine;
    private readonly Action<ShortcutKind> _shortcutTriggered;
    private readonly NativeMethods.LowLevelKeyboardProc _callback;
    private nint _hookHandle;

    public KeyboardHook(
        IEnumerable<ShortcutKind> enabledShortcuts,
        Action<ShortcutKind> shortcutTriggered)
    {
        _engine = new KeyboardShortcutEngine(enabledShortcuts);
        _shortcutTriggered = shortcutTriggered;
        _callback = HookCallback;
    }

    public void Start()
    {
        if (_hookHandle != 0)
        {
            return;
        }

        _hookHandle = NativeMethods.SetWindowsHookEx(
            NativeMethods.WhKeyboardLl,
            _callback,
            NativeMethods.GetModuleHandle(null),
            0);

        if (_hookHandle == 0)
        {
            throw new Win32Exception(Marshal.GetLastWin32Error(), "Could not install the keyboard hook.");
        }
    }

    public void Dispose()
    {
        if (_hookHandle == 0)
        {
            return;
        }

        if (!NativeMethods.UnhookWindowsHookEx(_hookHandle))
        {
            System.Diagnostics.Debug.WriteLine(
                $"SmartLang could not uninstall the keyboard hook. " +
                $"Win32 error: {Marshal.GetLastWin32Error()}.");
        }

        _hookHandle = 0;
        GC.SuppressFinalize(this);
    }

    public void Refresh()
    {
        if (_hookHandle == 0)
        {
            return;
        }

        var previousHandle = _hookHandle;
        var newHandle = NativeMethods.SetWindowsHookEx(
            NativeMethods.WhKeyboardLl,
            _callback,
            NativeMethods.GetModuleHandle(null),
            0);

        if (newHandle == 0)
        {
            System.Diagnostics.Debug.WriteLine(
                $"SmartLang could not refresh the keyboard hook. " +
                $"Win32 error: {Marshal.GetLastWin32Error()}.");
            return;
        }

        _hookHandle = newHandle;

        // Any key events during the gap (or before an eviction) were missed, so
        // discard accumulated modifier state to avoid a stuck-modifier replay.
        _engine.Reset();

        if (!NativeMethods.UnhookWindowsHookEx(previousHandle))
        {
            System.Diagnostics.Debug.WriteLine(
                $"SmartLang could not uninstall the previous keyboard hook during refresh. " +
                $"Win32 error: {Marshal.GetLastWin32Error()}.");
        }
    }

    private nint HookCallback(int code, nint wParam, nint lParam)
    {
        try
        {
            return ProcessCallback(code, wParam, lParam);
        }
        catch (Exception exception)
        {
            System.Diagnostics.Debug.WriteLine(
                $"SmartLang keyboard hook callback threw {exception.GetType().Name}: {exception.Message}");
            return NativeMethods.CallNextHookEx(_hookHandle, code, wParam, lParam);
        }
    }

    private nint ProcessCallback(int code, nint wParam, nint lParam)
    {
        if (code < 0)
        {
            return NativeMethods.CallNextHookEx(_hookHandle, code, wParam, lParam);
        }

        var message = unchecked((int)wParam);
        var isKeyDown = message is NativeMethods.WmKeyDown or NativeMethods.WmSysKeyDown;
        var isKeyUp = message is NativeMethods.WmKeyUp or NativeMethods.WmSysKeyUp;
        if (!isKeyDown && !isKeyUp)
        {
            return NativeMethods.CallNextHookEx(_hookHandle, code, wParam, lParam);
        }

        var data = Marshal.PtrToStructure<NativeMethods.KeyboardHookData>(lParam);
        var isInjected = (data.Flags &
            (NativeMethods.LlkhfInjected | NativeMethods.LlkhfLowerIlInjected)) != 0;

        var result = _engine.Process(
            unchecked((int)data.VirtualKeyCode),
            isKeyDown,
            isInjected);

        if (result.ReplayEvents is { Count: > 0 })
        {
            Replay(result.ReplayEvents);
        }

        if (result.TriggeredShortcut is { } shortcut)
        {
            _shortcutTriggered(shortcut);
        }

        return result.Suppress
            ? 1
            : NativeMethods.CallNextHookEx(_hookHandle, code, wParam, lParam);
    }

    private static void Replay(IReadOnlyList<SyntheticKeyEvent> events)
    {
        var inputs = events.Select(keyEvent => new NativeMethods.Input
        {
            Type = NativeMethods.InputKeyboard,
            Data = new NativeMethods.InputUnion
            {
                Keyboard = new NativeMethods.KeyboardInput
                {
                    VirtualKey = checked((ushort)keyEvent.VirtualKey),
                    Flags =
                        (keyEvent.IsKeyDown ? 0u : NativeMethods.KeyeventfKeyUp) |
                        (NativeMethods.IsExtendedKey(keyEvent.VirtualKey)
                            ? NativeMethods.KeyeventfExtendedKey
                            : 0u)
                }
            }
        }).ToArray();

        var sent = NativeMethods.SendInput(
            checked((uint)inputs.Length),
            inputs,
            Marshal.SizeOf<NativeMethods.Input>());

        if (sent != inputs.Length)
        {
            System.Diagnostics.Debug.WriteLine(
                $"SmartLang could not replay {inputs.Length - sent} keyboard event(s). " +
                $"Win32 error: {Marshal.GetLastWin32Error()}.");
        }
    }
}
