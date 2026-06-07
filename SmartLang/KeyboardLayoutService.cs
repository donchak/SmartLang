using System.Runtime.InteropServices;

namespace SmartLang;

public sealed class KeyboardLayoutService
{
    private readonly LanguageCatalog _catalog;
    private readonly Dictionary<string, nint> _lastLayouts =
        new(StringComparer.OrdinalIgnoreCase);

    public KeyboardLayoutService(LanguageCatalog catalog)
    {
        _catalog = catalog;
    }

    public bool TogglePrimaryLanguages(AppSettings settings)
    {
        var layouts = _catalog.GetInstalledLayouts();
        if (!TryGetTargetWindow(out var targetWindow, out var threadId))
        {
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
        return targetLayout is not null && RequestLayout(targetWindow, targetLayout.Handle);
    }

    public bool CycleAllLayouts()
    {
        var layouts = _catalog.GetInstalledLayouts();
        if (layouts.Count == 0 ||
            !TryGetTargetWindow(out var targetWindow, out var threadId))
        {
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
        return targetIndex >= 0 && RequestLayout(targetWindow, layouts[targetIndex].Handle);
    }

    private InstalledLayout? ResolveLayout(
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

    private void Remember(InstalledLayout? layout)
    {
        if (layout is not null)
        {
            _lastLayouts[layout.LanguageTag] = layout.Handle;
        }
    }

    private static bool TryGetTargetWindow(out nint targetWindow, out uint threadId)
    {
        targetWindow = NativeMethods.GetForegroundWindow();
        threadId = targetWindow == 0
            ? 0
            : NativeMethods.GetWindowThreadProcessId(targetWindow, out _);

        if (threadId == 0)
        {
            return false;
        }

        var info = new NativeMethods.GuiThreadInfo
        {
            Size = checked((uint)Marshal.SizeOf<NativeMethods.GuiThreadInfo>())
        };

        if (NativeMethods.GetGUIThreadInfo(threadId, ref info))
        {
            targetWindow = info.FocusWindow != 0
                ? info.FocusWindow
                : info.ActiveWindow != 0
                    ? info.ActiveWindow
                    : targetWindow;
        }

        return targetWindow != 0;
    }

    private static bool RequestLayout(nint targetWindow, nint layoutHandle) =>
        NativeMethods.PostMessage(
            targetWindow,
            NativeMethods.WmInputLangChangeRequest,
            0,
            layoutHandle);
}
