using System.ComponentModel;
using System.Runtime.InteropServices;

namespace SmartLang;

public sealed class KeyboardHook: IDisposable {
    private readonly KeyboardShortcutEngine _engine;
    private readonly Action<ShortcutKind> _shortcutTriggered;
    private readonly NativeMethods.LowLevelKeyboardProc _keyboardCallback;
    private readonly NativeMethods.LowLevelMouseProc _mouseCallback;
    private nint _keyboardHookHandle;
    private nint _mouseHookHandle;

    public KeyboardHook(
        IEnumerable<ShortcutKind> enabledShortcuts,
        Action<ShortcutKind> shortcutTriggered) {
        _engine = new KeyboardShortcutEngine(enabledShortcuts);
        _shortcutTriggered = shortcutTriggered;
        _keyboardCallback = KeyboardHookCallback;
        _mouseCallback = MouseHookCallback;
    }

    public void Start() {
        if(_keyboardHookHandle != 0 || _mouseHookHandle != 0) {
            return;
        }

        _keyboardHookHandle = NativeMethods.SetWindowsHookEx(
            NativeMethods.WhKeyboardLl,
            _keyboardCallback,
            NativeMethods.GetModuleHandle(null),
            0);

        if(_keyboardHookHandle == 0) {
            throw new Win32Exception(Marshal.GetLastWin32Error(), "Could not install the keyboard hook.");
        }

        _mouseHookHandle = NativeMethods.SetWindowsHookEx(
            NativeMethods.WhMouseLl,
            _mouseCallback,
            NativeMethods.GetModuleHandle(null),
            0);

        if(_mouseHookHandle == 0) {
            var error = Marshal.GetLastWin32Error();
            NativeMethods.UnhookWindowsHookEx(_keyboardHookHandle);
            _keyboardHookHandle = 0;
            throw new Win32Exception(error, "Could not install the mouse hook.");
        }

        AppLog.Write(
            $"Input hooks installed. Keyboard=0x{_keyboardHookHandle:X}, mouse=0x{_mouseHookHandle:X}.");
    }

    public void Dispose() {
        if(_keyboardHookHandle == 0 && _mouseHookHandle == 0) {
            return;
        }

        Unhook(ref _mouseHookHandle, "mouse");
        Unhook(ref _keyboardHookHandle, "keyboard");
        GC.SuppressFinalize(this);
    }

    private static void Unhook(ref nint hookHandle, string hookName) {
        if(hookHandle == 0) {
            return;
        }

        if(!NativeMethods.UnhookWindowsHookEx(hookHandle)) {
            System.Diagnostics.Debug.WriteLine(
                $"SmartLang could not uninstall the {hookName} hook. " +
                $"Win32 error: {Marshal.GetLastWin32Error()}.");
        }

        hookHandle = 0;
    }

    public void Refresh() {
        if(_keyboardHookHandle == 0 || _mouseHookHandle == 0) {
            return;
        }

        var newKeyboardHandle = NativeMethods.SetWindowsHookEx(
            NativeMethods.WhKeyboardLl,
            _keyboardCallback,
            NativeMethods.GetModuleHandle(null),
            0);

        if(newKeyboardHandle == 0) {
            System.Diagnostics.Debug.WriteLine(
                $"SmartLang could not refresh the keyboard hook. " +
                $"Win32 error: {Marshal.GetLastWin32Error()}.");
            return;
        }

        var newMouseHandle = NativeMethods.SetWindowsHookEx(
            NativeMethods.WhMouseLl,
            _mouseCallback,
            NativeMethods.GetModuleHandle(null),
            0);

        if(newMouseHandle == 0) {
            System.Diagnostics.Debug.WriteLine(
                $"SmartLang could not refresh the mouse hook. " +
                $"Win32 error: {Marshal.GetLastWin32Error()}.");
            NativeMethods.UnhookWindowsHookEx(newKeyboardHandle);
            return;
        }

        var previousKeyboardHandle = _keyboardHookHandle;
        var previousMouseHandle = _mouseHookHandle;
        _keyboardHookHandle = newKeyboardHandle;
        _mouseHookHandle = newMouseHandle;

        // Any key events during the gap (or before an eviction) were missed, so
        // discard accumulated modifier state to avoid a stuck-modifier replay.
        _engine.Reset();

        Unhook(ref previousMouseHandle, "previous mouse");
        Unhook(ref previousKeyboardHandle, "previous keyboard");
    }

    private nint KeyboardHookCallback(int code, nint wParam, nint lParam) {
        try {
            return ProcessKeyboardCallback(code, wParam, lParam);
        } catch(Exception exception) {
            System.Diagnostics.Debug.WriteLine(
                $"SmartLang keyboard hook callback threw {exception.GetType().Name}: {exception.Message}");
            return NativeMethods.CallNextHookEx(_keyboardHookHandle, code, wParam, lParam);
        }
    }

    private nint ProcessKeyboardCallback(int code, nint wParam, nint lParam) {
        if(code < 0) {
            return NativeMethods.CallNextHookEx(_keyboardHookHandle, code, wParam, lParam);
        }

        var message = unchecked((int)wParam);
        var isKeyDown = message is NativeMethods.WmKeyDown or NativeMethods.WmSysKeyDown;
        var isKeyUp = message is NativeMethods.WmKeyUp or NativeMethods.WmSysKeyUp;
        if(!isKeyDown && !isKeyUp) {
            return NativeMethods.CallNextHookEx(_keyboardHookHandle, code, wParam, lParam);
        }

        var data = Marshal.PtrToStructure<NativeMethods.KeyboardHookData>(lParam);
        var isInjected = (data.Flags &
            (NativeMethods.LlkhfInjected | NativeMethods.LlkhfLowerIlInjected)) != 0;

        var result = _engine.Process(
            unchecked((int)data.VirtualKeyCode),
            isKeyDown,
            isInjected);

        if(result.ReplayEvents is { Count: > 0 }) {
            Replay(result.ReplayEvents);
        }

        if(result.TriggeredShortcut is { } shortcut) {
            AppLog.Write($"Keyboard hook recognized {shortcut}.");
            _shortcutTriggered(shortcut);
        }

        return result.Suppress
            ? 1
            : NativeMethods.CallNextHookEx(_keyboardHookHandle, code, wParam, lParam);
    }

    private nint MouseHookCallback(int code, nint wParam, nint lParam) {
        try {
            if(code >= 0 && IsPointerInteractionMessage(unchecked((int)wParam))) {
                var data = Marshal.PtrToStructure<NativeMethods.MouseHookData>(lParam);
                var isInjected = (data.Flags &
                    (NativeMethods.LlmhfInjected | NativeMethods.LlmhfLowerIlInjected)) != 0;
                var result = _engine.ProcessPointerInput(isInjected);
                if(result.ReplayEvents is { Count: > 0 }) {
                    Replay(result.ReplayEvents);
                }
            }
        } catch(Exception exception) {
            System.Diagnostics.Debug.WriteLine(
                $"SmartLang mouse hook callback threw {exception.GetType().Name}: {exception.Message}");
        }

        return NativeMethods.CallNextHookEx(_mouseHookHandle, code, wParam, lParam);
    }

    internal static bool IsPointerInteractionMessage(int message) =>
        message is NativeMethods.WmLButtonDown
            or NativeMethods.WmLButtonUp
            or NativeMethods.WmRButtonDown
            or NativeMethods.WmRButtonUp
            or NativeMethods.WmMButtonDown
            or NativeMethods.WmMButtonUp
            or NativeMethods.WmMouseWheel
            or NativeMethods.WmXButtonDown
            or NativeMethods.WmXButtonUp
            or NativeMethods.WmMouseHWheel;

    private static void Replay(IReadOnlyList<SyntheticKeyEvent> events) {
        var inputs = events.Select(keyEvent => new NativeMethods.Input {
            Type = NativeMethods.InputKeyboard,
            Data = new NativeMethods.InputUnion {
                Keyboard = new NativeMethods.KeyboardInput {
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

        if(sent != inputs.Length) {
            System.Diagnostics.Debug.WriteLine(
                $"SmartLang could not replay {inputs.Length - sent} keyboard event(s). " +
                $"Win32 error: {Marshal.GetLastWin32Error()}.");
        }
    }
}
