namespace SmartLang.Tests;

public sealed class KeyboardShortcutEngineTests
{
    [Theory]
    [InlineData(KeyboardShortcutEngine.VkLControl, KeyboardShortcutEngine.VkLShift)]
    [InlineData(KeyboardShortcutEngine.VkRControl, KeyboardShortcutEngine.VkRShift)]
    [InlineData(KeyboardShortcutEngine.VkLControl, KeyboardShortcutEngine.VkRShift)]
    public void CtrlShiftTriggersOnceWhenModifierIsReleased(int control, int shift)
    {
        var engine = new KeyboardShortcutEngine();

        Assert.True(engine.Process(control, isKeyDown: true).Suppress);
        Assert.True(engine.Process(shift, isKeyDown: true).Suppress);

        var trigger = engine.Process(shift, isKeyDown: false);
        Assert.True(trigger.Suppress);
        Assert.Equal(ShortcutKind.CtrlShift, trigger.TriggeredShortcut);

        var finalRelease = engine.Process(control, isKeyDown: false);
        Assert.True(finalRelease.Suppress);
        Assert.Null(finalRelease.TriggeredShortcut);
    }

    [Theory]
    [InlineData(KeyboardShortcutEngine.VkLControl, KeyboardShortcutEngine.VkLShift)]
    [InlineData(KeyboardShortcutEngine.VkRControl, KeyboardShortcutEngine.VkRShift)]
    public void ShiftCanTriggerRepeatedlyWhileControlRemainsHeld(int control, int shift)
    {
        var engine = new KeyboardShortcutEngine();

        engine.Process(control, isKeyDown: true);
        engine.Process(shift, isKeyDown: true);
        Assert.Equal(
            ShortcutKind.CtrlShift,
            engine.Process(shift, isKeyDown: false).TriggeredShortcut);

        Assert.True(engine.Process(shift, isKeyDown: true).Suppress);
        var secondTrigger = engine.Process(shift, isKeyDown: false);

        Assert.True(secondTrigger.Suppress);
        Assert.Equal(ShortcutKind.CtrlShift, secondTrigger.TriggeredShortcut);
        Assert.True(engine.Process(control, isKeyDown: false).Suppress);
    }

    [Fact]
    public void ControlCanTriggerRepeatedlyWhileShiftRemainsHeld()
    {
        var engine = new KeyboardShortcutEngine();

        engine.Process(KeyboardShortcutEngine.VkLShift, isKeyDown: true);
        engine.Process(KeyboardShortcutEngine.VkLControl, isKeyDown: true);
        Assert.Equal(
            ShortcutKind.CtrlShift,
            engine.Process(
                KeyboardShortcutEngine.VkLControl,
                isKeyDown: false).TriggeredShortcut);

        Assert.True(engine.Process(
            KeyboardShortcutEngine.VkLControl,
            isKeyDown: true).Suppress);
        var secondTrigger = engine.Process(
            KeyboardShortcutEngine.VkLControl,
            isKeyDown: false);

        Assert.True(secondTrigger.Suppress);
        Assert.Equal(ShortcutKind.CtrlShift, secondTrigger.TriggeredShortcut);
        Assert.True(engine.Process(
            KeyboardShortcutEngine.VkLShift,
            isKeyDown: false).Suppress);
    }

    [Fact]
    public void CtrlShiftEscapeReplaysModifiersAndDoesNotTrigger()
    {
        const int vkEscape = 0x1B;
        var engine = new KeyboardShortcutEngine();

        engine.Process(KeyboardShortcutEngine.VkLControl, isKeyDown: true);
        engine.Process(KeyboardShortcutEngine.VkLShift, isKeyDown: true);
        var escape = engine.Process(vkEscape, isKeyDown: true);

        Assert.False(escape.Suppress);
        Assert.Null(escape.TriggeredShortcut);
        Assert.Collection(
            escape.ReplayEvents!,
            item => Assert.Equal(
                new SyntheticKeyEvent(KeyboardShortcutEngine.VkLControl, true),
                item),
            item => Assert.Equal(
                new SyntheticKeyEvent(KeyboardShortcutEngine.VkLShift, true),
                item));

        Assert.False(engine.Process(KeyboardShortcutEngine.VkLShift, false).Suppress);
        Assert.False(engine.Process(KeyboardShortcutEngine.VkLControl, false).Suppress);
    }

    [Fact]
    public void NormalControlCombinationReplaysControlBeforeKey()
    {
        const int vkC = 0x43;
        var engine = new KeyboardShortcutEngine();

        Assert.True(engine.Process(KeyboardShortcutEngine.VkLControl, true).Suppress);
        var keyDown = engine.Process(vkC, true);

        Assert.False(keyDown.Suppress);
        Assert.Equal(
            [new SyntheticKeyEvent(KeyboardShortcutEngine.VkLControl, true)],
            keyDown.ReplayEvents);
        Assert.False(engine.Process(KeyboardShortcutEngine.VkLControl, false).Suppress);
    }

    [Theory]
    [InlineData(KeyboardShortcutEngine.VkLWin)]
    [InlineData(KeyboardShortcutEngine.VkRWin)]
    public void WinSpaceIsConsumedAndTriggersOnce(int windowsKey)
    {
        var engine = new KeyboardShortcutEngine();

        Assert.True(engine.Process(windowsKey, true).Suppress);
        var trigger = engine.Process(KeyboardShortcutEngine.VkSpace, true);

        Assert.True(trigger.Suppress);
        Assert.Equal(ShortcutKind.WinSpace, trigger.TriggeredShortcut);
        Assert.True(engine.Process(KeyboardShortcutEngine.VkSpace, true).Suppress);
        Assert.True(engine.Process(KeyboardShortcutEngine.VkSpace, false).Suppress);
        Assert.True(engine.Process(windowsKey, false).Suppress);
    }

    [Theory]
    [InlineData(KeyboardShortcutEngine.VkLWin)]
    [InlineData(KeyboardShortcutEngine.VkRWin)]
    public void SpaceCanTriggerRepeatedlyWhileWindowsKeyRemainsHeld(int windowsKey)
    {
        var engine = new KeyboardShortcutEngine();

        engine.Process(windowsKey, isKeyDown: true);
        Assert.Equal(
            ShortcutKind.WinSpace,
            engine.Process(
                KeyboardShortcutEngine.VkSpace,
                isKeyDown: true).TriggeredShortcut);
        Assert.True(engine.Process(
            KeyboardShortcutEngine.VkSpace,
            isKeyDown: false).Suppress);

        var secondTrigger = engine.Process(
            KeyboardShortcutEngine.VkSpace,
            isKeyDown: true);

        Assert.True(secondTrigger.Suppress);
        Assert.Equal(ShortcutKind.WinSpace, secondTrigger.TriggeredShortcut);
        Assert.True(engine.Process(
            KeyboardShortcutEngine.VkSpace,
            isKeyDown: false).Suppress);
        Assert.True(engine.Process(windowsKey, isKeyDown: false).Suppress);
    }

    [Fact]
    public void HeldConsumedModifierIsReplayedBeforeAnotherKey()
    {
        const int vkE = 0x45;
        var engine = new KeyboardShortcutEngine();

        engine.Process(KeyboardShortcutEngine.VkLWin, isKeyDown: true);
        engine.Process(KeyboardShortcutEngine.VkSpace, isKeyDown: true);
        engine.Process(KeyboardShortcutEngine.VkSpace, isKeyDown: false);

        var keyDown = engine.Process(vkE, isKeyDown: true);

        Assert.False(keyDown.Suppress);
        Assert.Equal(
            [new SyntheticKeyEvent(KeyboardShortcutEngine.VkLWin, true)],
            keyDown.ReplayEvents);
        Assert.False(engine.Process(
            KeyboardShortcutEngine.VkLWin,
            isKeyDown: false).Suppress);
    }

    [Fact]
    public void ModifierTapIsReplayedAsDownAndUp()
    {
        var engine = new KeyboardShortcutEngine();

        engine.Process(KeyboardShortcutEngine.VkLShift, true);
        var release = engine.Process(KeyboardShortcutEngine.VkLShift, false);

        Assert.True(release.Suppress);
        Assert.Equal(
            [
                new SyntheticKeyEvent(KeyboardShortcutEngine.VkLShift, true),
                new SyntheticKeyEvent(KeyboardShortcutEngine.VkLShift, false)
            ],
            release.ReplayEvents);
    }
}
