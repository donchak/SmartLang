using System.Runtime.InteropServices;

namespace SmartLang;

public sealed class KeyboardLayoutService: IDisposable {
    readonly LanguageCatalog catalog;
    readonly IInputProfileActivator profileActivator;
    readonly Dictionary<string, nint> lastLayouts =
        new(StringComparer.OrdinalIgnoreCase);
    InstalledLayout? currentObservedLayout;
    InstalledLayout? previousObservedLayout;

    public KeyboardLayoutService(LanguageCatalog catalog)
        : this(catalog, new NativeInputProfileActivator()) {
    }

    internal KeyboardLayoutService(
        LanguageCatalog catalog,
        IInputProfileActivator profileActivator) {
        this.catalog = catalog;
        this.profileActivator = profileActivator;
    }

    internal IReadOnlyDictionary<string, nint> RememberedLayouts => lastLayouts;

    internal InstalledLayout? CurrentObservedLayout => currentObservedLayout;

    internal InstalledLayout? PreviousObservedLayout => previousObservedLayout;

    public bool TogglePrimaryLanguages(AppSettings settings) {
        var layouts = catalog.GetInstalledLayouts();
        if(!TryGetForegroundTarget(out var targetWindow, out var threadId)) {
            AppLog.Write("Primary switch failed: no foreground input thread.");
            return false;
        }

        var currentHandle = NativeMethods.GetKeyboardLayout(threadId);
        var currentLayout = layouts.FirstOrDefault(layout => layout.Handle == currentHandle);
        ObserveCurrentLayout(currentLayout);

        var targetLanguage = LanguageSwitchPolicy.GetPrimaryTargetLanguage(
            currentLayout?.LanguageTag,
            settings.PrimaryLanguageTag,
            settings.SecondaryLanguageTag);

        var targetLayout = ResolveLayout(targetLanguage, layouts);
        if(targetLayout is null) {
            AppLog.Write($"Primary switch failed: no layout for {targetLanguage}.");
            return false;
        }

        AppLog.Write(
            $"Primary switch: thread={threadId}, current=0x{currentHandle:X}, " +
            $"target=0x{targetLayout.Handle:X} ({targetLanguage}).");
        return ActivateAndRemember(threadId, targetWindow, currentLayout, currentHandle, targetLayout);
    }

    public bool CycleAllLayouts() {
        var layouts = catalog.GetInstalledLayouts();
        if(layouts.Count == 0 ||
            !TryGetForegroundTarget(out var targetWindow, out var threadId)) {
            AppLog.Write("Cycle switch failed: no layouts or foreground input thread.");
            return false;
        }

        var currentHandle = NativeMethods.GetKeyboardLayout(threadId);
        var currentIndex = -1;
        InstalledLayout? currentLayout = null;
        for(var index = 0; index < layouts.Count; index++) {
            if(layouts[index].Handle == currentHandle) {
                currentIndex = index;
                currentLayout = layouts[index];
                ObserveCurrentLayout(currentLayout);
                break;
            }
        }

        var targetIndex = LanguageSwitchPolicy.GetNextLayoutIndex(currentIndex, layouts.Count);
        if(targetIndex >= 0) {
            AppLog.Write(
                $"Cycle switch: thread={threadId}, current=0x{currentHandle:X}, " +
                $"target=0x{layouts[targetIndex].Handle:X}.");
        }

        return targetIndex >= 0 &&
            ActivateAndRemember(threadId, targetWindow, currentLayout, currentHandle, layouts[targetIndex]);
    }

    public bool SwitchToPreviousObservedLayout() {
        var layouts = catalog.GetInstalledLayouts();
        if(layouts.Count == 0 ||
            !TryGetForegroundTarget(out var targetWindow, out var threadId)) {
            AppLog.Write("Recent switch failed: no layouts or foreground input thread.");
            return false;
        }

        var currentHandle = NativeMethods.GetKeyboardLayout(threadId);
        var currentLayout = layouts.FirstOrDefault(layout => layout.Handle == currentHandle);
        ObserveCurrentLayout(currentLayout);

        var targetLayout = previousObservedLayout is null
            ? null
            : layouts.FirstOrDefault(layout => layout.Handle == previousObservedLayout.Handle);
        if(targetLayout is null || targetLayout.Handle == currentHandle) {
            AppLog.Write("Recent switch has no previous layout yet; cycling all layouts instead.");
            return CycleAllLayouts();
        }

        AppLog.Write(
            $"Recent switch: thread={threadId}, current=0x{currentHandle:X}, " +
            $"target=0x{targetLayout.Handle:X}.");
        return ActivateAndRemember(threadId, targetWindow, currentLayout, currentHandle, targetLayout);
    }

    internal InstalledLayout? ResolveLayout(
        string languageTag,
        IReadOnlyList<InstalledLayout> layouts) {
        var matchingLayouts = layouts
            .Where(layout => string.Equals(layout.LanguageTag, languageTag, StringComparison.OrdinalIgnoreCase))
            .ToArray();

        if(matchingLayouts.Length == 0) {
            return null;
        }

        if(lastLayouts.TryGetValue(languageTag, out var lastHandle)) {
            return matchingLayouts.FirstOrDefault(layout => layout.Handle == lastHandle)
                ?? matchingLayouts[0];
        }

        return matchingLayouts[0];
    }

    internal void Remember(InstalledLayout? layout) {
        if(layout is not null) {
            lastLayouts[layout.LanguageTag] = layout.Handle;
        }
    }

    internal void ObserveCurrentLayout(InstalledLayout? layout) {
        if(layout is null) {
            return;
        }

        Remember(layout);
        if(currentObservedLayout is null) {
            currentObservedLayout = layout;
            return;
        }

        if(currentObservedLayout.Handle == layout.Handle) {
            return;
        }

        previousObservedLayout = currentObservedLayout;
        currentObservedLayout = layout;
    }

    internal bool ActivateLayout(
        uint threadId,
        nint targetWindow,
        nint currentHandle,
        nint targetHandle) =>
        currentHandle == targetHandle ||
        profileActivator.ActivateKeyboardLayout(threadId, targetWindow, targetHandle);

    bool ActivateAndRemember(
        uint threadId,
        nint targetWindow,
        InstalledLayout? currentLayout,
        nint currentHandle,
        InstalledLayout targetLayout) {
        var activated = ActivateLayout(threadId, targetWindow, currentHandle, targetLayout.Handle);
        if(activated) {
            ObserveCurrentLayout(currentLayout);
            ObserveCurrentLayout(targetLayout);
        }

        return activated;
    }

    public void Dispose() => profileActivator.Dispose();

    static bool TryGetForegroundTarget(
        out nint targetWindow,
        out uint threadId) {
        targetWindow = NativeMethods.GetForegroundWindow();
        threadId = targetWindow == 0 ? 0 : NativeMethods.GetWindowThreadProcessId(targetWindow, out _);

        var info = new NativeMethods.GuiThreadInfo {
            Size = checked((uint)Marshal.SizeOf<NativeMethods.GuiThreadInfo>())
        };

        if(NativeMethods.GetGUIThreadInfo(0, ref info) &&
            info.FocusWindow != 0) {
            var focusThreadId = NativeMethods.GetWindowThreadProcessId(info.FocusWindow, out _);
            if(focusThreadId != 0) {
                targetWindow = info.FocusWindow;
                threadId = focusThreadId;
            }
        }

        return targetWindow != 0 && threadId != 0;
    }
}
