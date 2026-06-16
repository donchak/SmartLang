using System.Security.Cryptography;
using System.Security.Principal;
using System.Text;

namespace SmartLang;

public sealed class HookRuntimeController: IDisposable {
    readonly Action<Action> dispatch;
    readonly Action<string> failure;
    Mutex? ownershipMutex;
    KeyboardLayoutService? layoutService;
    KeyboardHook? keyboardHook;
    AppSettings? settings;

    public HookRuntimeController(Action<Action> dispatch, Action<string> failure) {
        this.dispatch = dispatch;
        this.failure = failure;
    }

    public bool IsRunning => keyboardHook is not null;

    public bool TryStart(AppSettings settings) {
        if(IsRunning) {
            return true;
        }

        var ownershipMutex = new Mutex(false, BuildMutexName());
        var acquired = false;
        try {
            try {
                acquired = ownershipMutex.WaitOne(0);
            } catch(AbandonedMutexException) {
                acquired = true;
            }

            if(!acquired) {
                ownershipMutex.Dispose();
                return false;
            }

            this.settings = settings.Copy();
            layoutService = new KeyboardLayoutService(new LanguageCatalog());
            keyboardHook = new KeyboardHook(
                GetEnabledShortcuts(this.settings),
                result => dispatch(() => HandleShortcut(result)));
            keyboardHook.Start();
            this.ownershipMutex = ownershipMutex;
            AppLog.Write("Hook runtime acquired input ownership.");
            return true;
        } catch {
            keyboardHook?.Dispose();
            keyboardHook = null;
            layoutService?.Dispose();
            layoutService = null;
            this.settings = null;
            if(acquired) {
                ownershipMutex.ReleaseMutex();
            }

            ownershipMutex.Dispose();
            throw;
        }
    }

    public bool Restart(AppSettings settings) {
        Stop();
        return TryStart(settings);
    }

    public void Refresh() => keyboardHook?.Refresh();

    public void Stop() {
        keyboardHook?.Dispose();
        keyboardHook = null;
        layoutService?.Dispose();
        layoutService = null;
        settings = null;

        if(ownershipMutex is not null) {
            try {
                ownershipMutex.ReleaseMutex();
            } catch(ApplicationException) {
            }

            ownershipMutex.Dispose();
            ownershipMutex = null;
            AppLog.Write("Hook runtime released input ownership.");
        }
    }

    public void Dispose() {
        Stop();
        GC.SuppressFinalize(this);
    }

    void HandleShortcut(ShortcutProcessingResult result) {
        if(settings is null || layoutService is null) {
            return;
        }

        var shortcut = result.TriggeredShortcut;
        if(shortcut is null) {
            return;
        }

        try {
            var switched = settings.SwitchingMode == SwitchingMode.RecentLanguages
                ? HandleRecentLanguagesShortcut(shortcut.Value, result.ShortcutPressCount)
                : HandlePrimaryLanguagesShortcut(shortcut.Value);

            if(!switched) {
                failure($"Windows did not accept the {shortcut} layout change.");
            }
        } catch(Exception exception) {
            AppLog.Write($"Shortcut {shortcut} failed with {exception.GetType().Name}: {exception.Message}");
            failure($"Unexpected language-switch error: {exception.Message}");
        }
    }

    bool HandlePrimaryLanguagesShortcut(ShortcutKind shortcut) =>
        shortcut == settings!.PrimaryShortcut
            ? layoutService!.TogglePrimaryLanguages(settings)
            : settings.AllLayoutsShortcut != ShortcutKind.None &&
              shortcut == settings.AllLayoutsShortcut &&
              layoutService!.CycleAllLayouts();

    bool HandleRecentLanguagesShortcut(ShortcutKind shortcut, int shortcutPressCount) =>
        shortcut == settings!.PrimaryShortcut &&
        (shortcutPressCount <= 1
            ? layoutService!.SwitchToPreviousObservedLayout()
            : layoutService!.CycleAllLayouts());

    static IReadOnlyCollection<ShortcutKind> GetEnabledShortcuts(AppSettings settings) {
        var shortcuts = new HashSet<ShortcutKind> { settings.PrimaryShortcut };
        if(settings.SwitchingMode == SwitchingMode.PrimaryLanguages &&
            settings.AllLayoutsShortcut != ShortcutKind.None) {
            shortcuts.Add(settings.AllLayoutsShortcut);
        }

        return shortcuts;
    }

    static string BuildMutexName() {
        var sid = WindowsIdentity.GetCurrent().User?.Value ?? Environment.UserName;
        var sessionId = Environment.ProcessId;
        try {
            sessionId = System.Diagnostics.Process.GetCurrentProcess().SessionId;
        } catch(InvalidOperationException) {
        }

        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(sid)))[..16];
        return $@"Local\SmartLang.HookOwner.{sessionId}.{hash}";
    }
}
