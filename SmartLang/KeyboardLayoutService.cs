using System.Runtime.InteropServices;

namespace SmartLang;

public sealed class KeyboardLayoutService : IDisposable
{
    private readonly LanguageCatalog _catalog;
    private readonly IInputProfileActivator _profileActivator;
    private readonly Dictionary<string, nint> _lastLayouts =
        new(StringComparer.OrdinalIgnoreCase);

    public KeyboardLayoutService(LanguageCatalog catalog)
        : this(catalog, new NativeInputProfileActivator())
    {
    }

    internal KeyboardLayoutService(
        LanguageCatalog catalog,
        IInputProfileActivator profileActivator)
    {
        _catalog = catalog;
        _profileActivator = profileActivator;
    }

    internal IReadOnlyDictionary<string, nint> RememberedLayouts => _lastLayouts;

    public bool TogglePrimaryLanguages(AppSettings settings)
    {
        var layouts = _catalog.GetInstalledLayouts();
        if (!TryGetForegroundTarget(
            out var targetWindow,
            out var threadId))
        {
            AppLog.Write("Primary switch failed: no foreground input thread.");
            return false;
        }

        var currentHandle = NativeMethods.GetKeyboardLayout(threadId);
        var currentLayout = layouts.FirstOrDefault(layout => layout.Handle == currentHandle);
        Remember(currentLayout);

        var targetLanguage = LanguageSwitchPolicy.GetPrimaryTargetLanguage(
            currentLayout?.LanguageTag,
            settings.PrimaryLanguageTag,
            settings.SecondaryLanguageTag);

        var targetLayout = ResolveLayout(targetLanguage, layouts);
        if (targetLayout is null)
        {
            AppLog.Write($"Primary switch failed: no layout for {targetLanguage}.");
            return false;
        }

        AppLog.Write(
            $"Primary switch: thread={threadId}, current=0x{currentHandle:X}, " +
            $"target=0x{targetLayout.Handle:X} ({targetLanguage}).");
        return ActivateLayout(
            threadId,
            targetWindow,
            currentHandle,
            targetLayout.Handle);
    }

    public bool CycleAllLayouts()
    {
        var layouts = _catalog.GetInstalledLayouts();
        if (layouts.Count == 0 ||
            !TryGetForegroundTarget(
                out var targetWindow,
                out var threadId))
        {
            AppLog.Write("Cycle switch failed: no layouts or foreground input thread.");
            return false;
        }

        var currentHandle = NativeMethods.GetKeyboardLayout(threadId);
        var currentIndex = -1;
        for (var index = 0; index < layouts.Count; index++)
        {
            if (layouts[index].Handle == currentHandle)
            {
                currentIndex = index;
                Remember(layouts[index]);
                break;
            }
        }

        var targetIndex = LanguageSwitchPolicy.GetNextLayoutIndex(currentIndex, layouts.Count);
        if (targetIndex >= 0)
        {
            AppLog.Write(
                $"Cycle switch: thread={threadId}, current=0x{currentHandle:X}, " +
                $"target=0x{layouts[targetIndex].Handle:X}.");
        }

        return targetIndex >= 0 &&
            ActivateLayout(
                threadId,
                targetWindow,
                currentHandle,
                layouts[targetIndex].Handle);
    }

    internal InstalledLayout? ResolveLayout(
        string languageTag,
        IReadOnlyList<InstalledLayout> layouts)
    {
        var matchingLayouts = layouts
            .Where(layout => string.Equals(
                layout.LanguageTag,
                languageTag,
                StringComparison.OrdinalIgnoreCase))
            .ToArray();

        if (matchingLayouts.Length == 0)
        {
            return null;
        }

        if (_lastLayouts.TryGetValue(languageTag, out var lastHandle))
        {
            return matchingLayouts.FirstOrDefault(layout => layout.Handle == lastHandle)
                ?? matchingLayouts[0];
        }

        return matchingLayouts[0];
    }

    internal void Remember(InstalledLayout? layout)
    {
        if (layout is not null)
        {
            _lastLayouts[layout.LanguageTag] = layout.Handle;
        }
    }

    internal bool ActivateLayout(
        uint threadId,
        nint targetWindow,
        nint currentHandle,
        nint targetHandle) =>
        currentHandle == targetHandle ||
        _profileActivator.ActivateKeyboardLayout(
            threadId,
            targetWindow,
            targetHandle);

    public void Dispose() => _profileActivator.Dispose();

    private static bool TryGetForegroundTarget(
        out nint targetWindow,
        out uint threadId)
    {
        targetWindow = NativeMethods.GetForegroundWindow();
        threadId = targetWindow == 0
            ? 0
            : NativeMethods.GetWindowThreadProcessId(targetWindow, out _);

        var info = new NativeMethods.GuiThreadInfo
        {
            Size = checked((uint)Marshal.SizeOf<NativeMethods.GuiThreadInfo>())
        };

        if (NativeMethods.GetGUIThreadInfo(0, ref info) &&
            info.FocusWindow != 0)
        {
            var focusThreadId = NativeMethods.GetWindowThreadProcessId(
                info.FocusWindow,
                out _);
            if (focusThreadId != 0)
            {
                targetWindow = info.FocusWindow;
                threadId = focusThreadId;
            }
        }

        return targetWindow != 0 && threadId != 0;
    }
}
