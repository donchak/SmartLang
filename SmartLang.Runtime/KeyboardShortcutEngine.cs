namespace SmartLang;

public readonly record struct SyntheticKeyEvent(int VirtualKey, bool IsKeyDown);

public sealed record ShortcutProcessingResult(
    bool Suppress,
    ShortcutKind? TriggeredShortcut = null,
    IReadOnlyList<SyntheticKeyEvent>? ReplayEvents = null,
    int ShortcutPressCount = 0) {
    public static readonly ShortcutProcessingResult Pass = new(false);
}

public sealed class KeyboardShortcutEngine {
    public const int VkLShift = 0xA0;
    public const int VkRShift = 0xA1;
    public const int VkLControl = 0xA2;
    public const int VkRControl = 0xA3;
    public const int VkLWin = 0x5B;
    public const int VkRWin = 0x5C;
    public const int VkSpace = 0x20;

    readonly HashSet<int> physicallyDown = [];
    readonly List<int> bufferedModifiers = [];
    readonly HashSet<int> replayedModifiers = [];
    readonly HashSet<int> consumedKeys = [];
    readonly bool ctrlShiftEnabled;
    readonly bool winSpaceEnabled;
    ShortcutKind? activeShortcutSession;
    int activeShortcutPressCount;

    public KeyboardShortcutEngine(IEnumerable<ShortcutKind>? enabledShortcuts = null) {
        var shortcuts = enabledShortcuts?.ToHashSet() ??
            [ShortcutKind.CtrlShift, ShortcutKind.WinSpace];
        ctrlShiftEnabled = shortcuts.Contains(ShortcutKind.CtrlShift);
        winSpaceEnabled = shortcuts.Contains(ShortcutKind.WinSpace);
    }

    public void Reset() {
        physicallyDown.Clear();
        bufferedModifiers.Clear();
        replayedModifiers.Clear();
        consumedKeys.Clear();
        activeShortcutSession = null;
        activeShortcutPressCount = 0;
    }

    public ShortcutProcessingResult Process(int virtualKey, bool isKeyDown, bool isInjected = false) {
        if(isInjected) {
            return ShortcutProcessingResult.Pass;
        }

        var wasDown = physicallyDown.Contains(virtualKey);
        if(isKeyDown) {
            physicallyDown.Add(virtualKey);
        } else {
            physicallyDown.Remove(virtualKey);
        }

        if(consumedKeys.Contains(virtualKey)) {
            if(!isKeyDown) {
                consumedKeys.Remove(virtualKey);
                ResetShortcutSessionIfNoModifierIsHeld();
            }

            return new ShortcutProcessingResult(true);
        }

        if(isKeyDown && wasDown) {
            return bufferedModifiers.Contains(virtualKey) ? new ShortcutProcessingResult(true) : ShortcutProcessingResult.Pass;
        }

        if(IsWatchedModifier(virtualKey)) {
            return isKeyDown ? ProcessModifierDown(virtualKey) : ProcessModifierUp(virtualKey);
        }

        if(isKeyDown) {
            SeedHeldConsumedModifiers();
        }

        if(isKeyDown &&
            winSpaceEnabled &&
            virtualKey == VkSpace &&
            IsWinOnlyBuffer()) {
            foreach(var key in bufferedModifiers) {
                consumedKeys.Add(key);
            }

            consumedKeys.Add(VkSpace);
            bufferedModifiers.Clear();
            return TriggerShortcut(ShortcutKind.WinSpace);
        }

        if(bufferedModifiers.Count > 0 && isKeyDown) {
            return ReplayBufferedModifiers(suppressCurrentEvent: false);
        }

        return ShortcutProcessingResult.Pass;
    }

    public ShortcutProcessingResult ProcessPointerInput(bool isInjected = false) {
        if(isInjected) {
            return ShortcutProcessingResult.Pass;
        }

        SeedHeldConsumedModifiers();
        return bufferedModifiers.Count > 0
            ? ReplayBufferedModifiers(suppressCurrentEvent: false)
            : ShortcutProcessingResult.Pass;
    }

    ShortcutProcessingResult ProcessModifierDown(int virtualKey) {
        if(replayedModifiers.Contains(virtualKey)) {
            return ShortcutProcessingResult.Pass;
        }

        if(bufferedModifiers.Count == 0 &&
            physicallyDown.Any(key => key != virtualKey && !IsWatchedModifier(key))) {
            return ShortcutProcessingResult.Pass;
        }

        SeedHeldConsumedModifiers();
        bufferedModifiers.Add(virtualKey);

        if(BufferCanStillBecomeShortcut()) {
            return new ShortcutProcessingResult(true);
        }

        return ReplayBufferedModifiers(suppressCurrentEvent: true);
    }

    ShortcutProcessingResult ProcessModifierUp(int virtualKey) {
        if(replayedModifiers.Remove(virtualKey)) {
            return ShortcutProcessingResult.Pass;
        }

        if(!bufferedModifiers.Contains(virtualKey)) {
            return ShortcutProcessingResult.Pass;
        }

        if(ctrlShiftEnabled && IsCtrlShiftBuffer()) {
            foreach(var key in bufferedModifiers) {
                if(key != virtualKey && physicallyDown.Contains(key)) {
                    consumedKeys.Add(key);
                }
            }

            bufferedModifiers.Clear();
            return TriggerShortcut(ShortcutKind.CtrlShift);
        }

        var replay = bufferedModifiers
            .Select(key => new SyntheticKeyEvent(key, true))
            .ToList();
        replay.Add(new SyntheticKeyEvent(virtualKey, false));

        foreach(var key in bufferedModifiers) {
            if(key != virtualKey && physicallyDown.Contains(key)) {
                replayedModifiers.Add(key);
            }
        }

        bufferedModifiers.Clear();
        return new ShortcutProcessingResult(true, ReplayEvents: replay);
    }

    ShortcutProcessingResult TriggerShortcut(ShortcutKind shortcut) {
        if(activeShortcutSession == shortcut) {
            activeShortcutPressCount++;
        } else {
            activeShortcutSession = shortcut;
            activeShortcutPressCount = 1;
        }

        return new ShortcutProcessingResult(true, shortcut, ShortcutPressCount: activeShortcutPressCount);
    }

    ShortcutProcessingResult ReplayBufferedModifiers(bool suppressCurrentEvent) {
        var replay = bufferedModifiers
            .Select(key => new SyntheticKeyEvent(key, true))
            .ToArray();

        foreach(var key in bufferedModifiers) {
            if(physicallyDown.Contains(key)) {
                consumedKeys.Remove(key);
                replayedModifiers.Add(key);
            }
        }

        bufferedModifiers.Clear();
        activeShortcutSession = null;
        activeShortcutPressCount = 0;
        return new ShortcutProcessingResult(suppressCurrentEvent, ReplayEvents: replay);
    }

    void SeedHeldConsumedModifiers() {
        foreach(var key in consumedKeys) {
            if(physicallyDown.Contains(key) &&
                IsWatchedModifier(key) &&
                !bufferedModifiers.Contains(key)) {
                bufferedModifiers.Add(key);
            }
        }
    }

    bool BufferCanStillBecomeShortcut() {
        var hasWin = bufferedModifiers.Any(IsWin);
        var hasCtrl = bufferedModifiers.Any(IsControl);
        var hasShift = bufferedModifiers.Any(IsShift);

        return hasWin ? !hasCtrl && !hasShift : hasCtrl || hasShift;
    }

    bool IsWinOnlyBuffer() =>
        bufferedModifiers.Count > 0 &&
        bufferedModifiers.All(IsWin);

    bool IsCtrlShiftBuffer() =>
        bufferedModifiers.Any(IsControl) &&
        bufferedModifiers.Any(IsShift) &&
        !bufferedModifiers.Any(IsWin);

    void ResetShortcutSessionIfNoModifierIsHeld() {
        if(!physicallyDown.Any(IsWatchedModifier)) {
            activeShortcutSession = null;
            activeShortcutPressCount = 0;
        }
    }

    bool IsWatchedModifier(int key) =>
        (ctrlShiftEnabled && (IsControl(key) || IsShift(key))) ||
        (winSpaceEnabled && IsWin(key));

    static bool IsControl(int key) => key is VkLControl or VkRControl;

    static bool IsShift(int key) => key is VkLShift or VkRShift;

    static bool IsWin(int key) => key is VkLWin or VkRWin;
}
