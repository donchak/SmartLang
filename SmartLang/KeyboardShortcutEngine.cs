namespace SmartLang;

public readonly record struct SyntheticKeyEvent(int VirtualKey, bool IsKeyDown);

public sealed record ShortcutProcessingResult(
    bool Suppress,
    ShortcutKind? TriggeredShortcut = null,
    IReadOnlyList<SyntheticKeyEvent>? ReplayEvents = null)
{
    public static readonly ShortcutProcessingResult Pass = new(false);
}

public sealed class KeyboardShortcutEngine
{
    public const int VkLShift = 0xA0;
    public const int VkRShift = 0xA1;
    public const int VkLControl = 0xA2;
    public const int VkRControl = 0xA3;
    public const int VkLWin = 0x5B;
    public const int VkRWin = 0x5C;
    public const int VkSpace = 0x20;

    private readonly HashSet<int> _physicallyDown = [];
    private readonly List<int> _bufferedModifiers = [];
    private readonly HashSet<int> _replayedModifiers = [];
    private readonly HashSet<int> _consumedKeys = [];

    public ShortcutProcessingResult Process(int virtualKey, bool isKeyDown, bool isInjected = false)
    {
        if (isInjected)
        {
            return ShortcutProcessingResult.Pass;
        }

        var wasDown = _physicallyDown.Contains(virtualKey);
        if (isKeyDown)
        {
            _physicallyDown.Add(virtualKey);
        }
        else
        {
            _physicallyDown.Remove(virtualKey);
        }

        if (_consumedKeys.Contains(virtualKey))
        {
            if (!isKeyDown)
            {
                _consumedKeys.Remove(virtualKey);
            }

            return new ShortcutProcessingResult(true);
        }

        if (isKeyDown && wasDown)
        {
            return _bufferedModifiers.Contains(virtualKey)
                ? new ShortcutProcessingResult(true)
                : ShortcutProcessingResult.Pass;
        }

        if (IsPotentialModifier(virtualKey))
        {
            return isKeyDown
                ? ProcessModifierDown(virtualKey)
                : ProcessModifierUp(virtualKey);
        }

        if (isKeyDown &&
            virtualKey == VkSpace &&
            IsWinOnlyBuffer())
        {
            foreach (var key in _bufferedModifiers)
            {
                _consumedKeys.Add(key);
            }

            _consumedKeys.Add(VkSpace);
            _bufferedModifiers.Clear();
            return new ShortcutProcessingResult(true, ShortcutKind.WinSpace);
        }

        if (_bufferedModifiers.Count > 0 && isKeyDown)
        {
            return ReplayBufferedModifiers(suppressCurrentEvent: false);
        }

        return ShortcutProcessingResult.Pass;
    }

    private ShortcutProcessingResult ProcessModifierDown(int virtualKey)
    {
        if (_replayedModifiers.Contains(virtualKey))
        {
            return ShortcutProcessingResult.Pass;
        }

        if (_bufferedModifiers.Count == 0 &&
            _physicallyDown.Any(key => key != virtualKey && !IsPotentialModifier(key)))
        {
            return ShortcutProcessingResult.Pass;
        }

        SeedHeldConsumedModifiers();
        _bufferedModifiers.Add(virtualKey);

        if (BufferCanStillBecomeShortcut())
        {
            return new ShortcutProcessingResult(true);
        }

        return ReplayBufferedModifiers(suppressCurrentEvent: true);
    }

    private ShortcutProcessingResult ProcessModifierUp(int virtualKey)
    {
        if (_replayedModifiers.Remove(virtualKey))
        {
            return ShortcutProcessingResult.Pass;
        }

        if (!_bufferedModifiers.Contains(virtualKey))
        {
            return ShortcutProcessingResult.Pass;
        }

        if (IsCtrlShiftBuffer())
        {
            foreach (var key in _bufferedModifiers)
            {
                if (key != virtualKey && _physicallyDown.Contains(key))
                {
                    _consumedKeys.Add(key);
                }
            }

            _bufferedModifiers.Clear();
            return new ShortcutProcessingResult(true, ShortcutKind.CtrlShift);
        }

        var replay = _bufferedModifiers
            .Select(key => new SyntheticKeyEvent(key, true))
            .ToList();
        replay.Add(new SyntheticKeyEvent(virtualKey, false));

        foreach (var key in _bufferedModifiers)
        {
            if (key != virtualKey && _physicallyDown.Contains(key))
            {
                _replayedModifiers.Add(key);
            }
        }

        _bufferedModifiers.Clear();
        return new ShortcutProcessingResult(true, ReplayEvents: replay);
    }

    private ShortcutProcessingResult ReplayBufferedModifiers(bool suppressCurrentEvent)
    {
        var replay = _bufferedModifiers
            .Select(key => new SyntheticKeyEvent(key, true))
            .ToArray();

        foreach (var key in _bufferedModifiers)
        {
            if (_physicallyDown.Contains(key))
            {
                _replayedModifiers.Add(key);
            }
        }

        _bufferedModifiers.Clear();
        return new ShortcutProcessingResult(suppressCurrentEvent, ReplayEvents: replay);
    }

    private void SeedHeldConsumedModifiers()
    {
        foreach (var key in _consumedKeys)
        {
            if (_physicallyDown.Contains(key) &&
                IsPotentialModifier(key) &&
                !_bufferedModifiers.Contains(key))
            {
                _bufferedModifiers.Add(key);
            }
        }
    }

    private bool BufferCanStillBecomeShortcut()
    {
        var hasWin = _bufferedModifiers.Any(IsWin);
        var hasCtrl = _bufferedModifiers.Any(IsControl);
        var hasShift = _bufferedModifiers.Any(IsShift);

        return hasWin
            ? !hasCtrl && !hasShift
            : hasCtrl || hasShift;
    }

    private bool IsWinOnlyBuffer() =>
        _bufferedModifiers.Count > 0 &&
        _bufferedModifiers.All(IsWin);

    private bool IsCtrlShiftBuffer() =>
        _bufferedModifiers.Any(IsControl) &&
        _bufferedModifiers.Any(IsShift) &&
        !_bufferedModifiers.Any(IsWin);

    private static bool IsPotentialModifier(int key) =>
        IsControl(key) || IsShift(key) || IsWin(key);

    private static bool IsControl(int key) => key is VkLControl or VkRControl;

    private static bool IsShift(int key) => key is VkLShift or VkRShift;

    private static bool IsWin(int key) => key is VkLWin or VkRWin;
}
