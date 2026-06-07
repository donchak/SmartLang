using System.ComponentModel;
using System.Runtime.InteropServices;

namespace SmartLang;

public sealed class KeyboardHook : IDisposable
{
    private readonly KeyboardShortcutEngine _engine = new();
    private readonly Action<ShortcutKind> _shortcutTriggered;
    private readonly NativeMethods.LowLevelKeyboardProc _callback;
    private nint _hookHandle;

    public KeyboardHook(Action<ShortcutKind> shortcutTriggered)
    {
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

        NativeMethods.UnhookWindowsHookEx(_hookHandle);
        _hookHandle = 0;
        GC.SuppressFinalize(this);
    }

    private nint HookCallback(int code, nint wParam, nint lParam)
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

        NativeMethods.SendInput(
            checked((uint)inputs.Length),
            inputs,
            Marshal.SizeOf<NativeMethods.Input>());
    }
}
