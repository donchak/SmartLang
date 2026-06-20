namespace SmartLang.Tests;

public sealed class KeyboardShortcutEngineTests {
    [Theory]
    [InlineData(KeyboardShortcutEngine.VkLControl, KeyboardShortcutEngine.VkLShift)]
    [InlineData(KeyboardShortcutEngine.VkRControl, KeyboardShortcutEngine.VkRShift)]
    [InlineData(KeyboardShortcutEngine.VkLControl, KeyboardShortcutEngine.VkRShift)]
    public void CtrlShiftTriggersOnceWhenModifierIsReleased(int control, int shift) {
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
    public void ShiftCanTriggerRepeatedlyWhileControlRemainsHeld(int control, int shift) {
        var engine = new KeyboardShortcutEngine();

        engine.Process(control, isKeyDown: true);
        engine.Process(shift, isKeyDown: true);
        Assert.Equal(ShortcutKind.CtrlShift, engine.Process(shift, isKeyDown: false).TriggeredShortcut);

        Assert.True(engine.Process(shift, isKeyDown: true).Suppress);
        var secondTrigger = engine.Process(shift, isKeyDown: false);

        Assert.True(secondTrigger.Suppress);
        Assert.Equal(ShortcutKind.CtrlShift, secondTrigger.TriggeredShortcut);
        Assert.Equal(2, secondTrigger.ShortcutPressCount);
        Assert.True(engine.Process(control, isKeyDown: false).Suppress);
    }

    [Fact]
    public void ControlCanTriggerRepeatedlyWhileShiftRemainsHeld() {
        var engine = new KeyboardShortcutEngine();

        engine.Process(KeyboardShortcutEngine.VkLShift, isKeyDown: true);
        engine.Process(KeyboardShortcutEngine.VkLControl, isKeyDown: true);
        Assert.Equal(
            ShortcutKind.CtrlShift,
            engine.Process(KeyboardShortcutEngine.VkLControl, isKeyDown: false).TriggeredShortcut);

        Assert.True(engine.Process(KeyboardShortcutEngine.VkLControl, isKeyDown: true).Suppress);
        var secondTrigger = engine.Process(KeyboardShortcutEngine.VkLControl, isKeyDown: false);

        Assert.True(secondTrigger.Suppress);
        Assert.Equal(ShortcutKind.CtrlShift, secondTrigger.TriggeredShortcut);
        Assert.True(engine.Process(KeyboardShortcutEngine.VkLShift, isKeyDown: false).Suppress);
    }

    [Theory]
    [InlineData(KeyboardShortcutEngine.VkLAlt, KeyboardShortcutEngine.VkLShift)]
    [InlineData(KeyboardShortcutEngine.VkRAlt, KeyboardShortcutEngine.VkRShift)]
    [InlineData(KeyboardShortcutEngine.VkLAlt, KeyboardShortcutEngine.VkRShift)]
    public void AltShiftTriggersOnceWhenModifierIsReleased(int alt, int shift) {
        var engine = new KeyboardShortcutEngine();

        Assert.True(engine.Process(alt, isKeyDown: true).Suppress);
        Assert.True(engine.Process(shift, isKeyDown: true).Suppress);

        var trigger = engine.Process(shift, isKeyDown: false);
        Assert.True(trigger.Suppress);
        Assert.Equal(ShortcutKind.AltShift, trigger.TriggeredShortcut);

        var finalRelease = engine.Process(alt, isKeyDown: false);
        Assert.True(finalRelease.Suppress);
        Assert.Null(finalRelease.TriggeredShortcut);
    }

    [Theory]
    [InlineData(KeyboardShortcutEngine.VkLAlt, KeyboardShortcutEngine.VkLShift)]
    [InlineData(KeyboardShortcutEngine.VkRAlt, KeyboardShortcutEngine.VkRShift)]
    public void ShiftCanTriggerRepeatedlyWhileAltRemainsHeld(int alt, int shift) {
        var engine = new KeyboardShortcutEngine();

        engine.Process(alt, isKeyDown: true);
        engine.Process(shift, isKeyDown: true);
        Assert.Equal(ShortcutKind.AltShift, engine.Process(shift, isKeyDown: false).TriggeredShortcut);

        Assert.True(engine.Process(shift, isKeyDown: true).Suppress);
        var secondTrigger = engine.Process(shift, isKeyDown: false);

        Assert.True(secondTrigger.Suppress);
        Assert.Equal(ShortcutKind.AltShift, secondTrigger.TriggeredShortcut);
        Assert.Equal(2, secondTrigger.ShortcutPressCount);
        Assert.True(engine.Process(alt, isKeyDown: false).Suppress);
    }

    [Fact]
    public void CtrlShiftEscapeReplaysModifiersAndDoesNotTrigger() {
        const int vkEscape = 0x1B;
        var engine = new KeyboardShortcutEngine();

        engine.Process(KeyboardShortcutEngine.VkLControl, isKeyDown: true);
        engine.Process(KeyboardShortcutEngine.VkLShift, isKeyDown: true);
        var escape = engine.Process(vkEscape, isKeyDown: true);

        Assert.False(escape.Suppress);
        Assert.Null(escape.TriggeredShortcut);
        Assert.Collection(
            escape.ReplayEvents!,
            item => Assert.Equal(new SyntheticKeyEvent(KeyboardShortcutEngine.VkLControl, true), item),
            item => Assert.Equal(new SyntheticKeyEvent(KeyboardShortcutEngine.VkLShift, true), item));

        Assert.False(engine.Process(KeyboardShortcutEngine.VkLShift, false).Suppress);
        Assert.False(engine.Process(KeyboardShortcutEngine.VkLControl, false).Suppress);
    }

    [Fact]
    public void NormalControlCombinationReplaysControlBeforeKey() {
        const int vkC = 0x43;
        var engine = new KeyboardShortcutEngine();

        Assert.True(engine.Process(KeyboardShortcutEngine.VkLControl, true).Suppress);
        var keyDown = engine.Process(vkC, true);

        Assert.False(keyDown.Suppress);
        Assert.Equal([new SyntheticKeyEvent(KeyboardShortcutEngine.VkLControl, true)], keyDown.ReplayEvents);
        Assert.False(engine.Process(KeyboardShortcutEngine.VkLControl, false).Suppress);
    }

    [Theory]
    [InlineData(KeyboardShortcutEngine.VkLWin)]
    [InlineData(KeyboardShortcutEngine.VkRWin)]
    public void WinSpaceIsConsumedAndTriggersOnce(int windowsKey) {
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
    public void SpaceCanTriggerRepeatedlyWhileWindowsKeyRemainsHeld(int windowsKey) {
        var engine = new KeyboardShortcutEngine();

        engine.Process(windowsKey, isKeyDown: true);
        Assert.Equal(ShortcutKind.WinSpace, engine.Process(KeyboardShortcutEngine.VkSpace, isKeyDown: true).TriggeredShortcut);
        Assert.True(engine.Process(KeyboardShortcutEngine.VkSpace, isKeyDown: false).Suppress);

        var secondTrigger = engine.Process(KeyboardShortcutEngine.VkSpace, isKeyDown: true);

        Assert.True(secondTrigger.Suppress);
        Assert.Equal(ShortcutKind.WinSpace, secondTrigger.TriggeredShortcut);
        Assert.Equal(2, secondTrigger.ShortcutPressCount);
        Assert.True(engine.Process(KeyboardShortcutEngine.VkSpace, isKeyDown: false).Suppress);
        Assert.True(engine.Process(windowsKey, isKeyDown: false).Suppress);
    }

    [Fact]
    public void ShortcutPressCountResetsAfterModifierRelease() {
        var engine = new KeyboardShortcutEngine();

        engine.Process(KeyboardShortcutEngine.VkLControl, isKeyDown: true);
        engine.Process(KeyboardShortcutEngine.VkLShift, isKeyDown: true);
        Assert.Equal(1, engine.Process(KeyboardShortcutEngine.VkLShift, isKeyDown: false).ShortcutPressCount);
        engine.Process(KeyboardShortcutEngine.VkLControl, isKeyDown: false);

        engine.Process(KeyboardShortcutEngine.VkLControl, isKeyDown: true);
        engine.Process(KeyboardShortcutEngine.VkLShift, isKeyDown: true);
        Assert.Equal(1, engine.Process(KeyboardShortcutEngine.VkLShift, isKeyDown: false).ShortcutPressCount);
    }

    [Fact]
    public void HeldConsumedModifierIsReplayedBeforeAnotherKey() {
        const int vkE = 0x45;
        var engine = new KeyboardShortcutEngine();

        engine.Process(KeyboardShortcutEngine.VkLWin, isKeyDown: true);
        engine.Process(KeyboardShortcutEngine.VkSpace, isKeyDown: true);
        engine.Process(KeyboardShortcutEngine.VkSpace, isKeyDown: false);

        var keyDown = engine.Process(vkE, isKeyDown: true);

        Assert.False(keyDown.Suppress);
        Assert.Equal([new SyntheticKeyEvent(KeyboardShortcutEngine.VkLWin, true)], keyDown.ReplayEvents);
        Assert.False(engine.Process(KeyboardShortcutEngine.VkLWin, isKeyDown: false).Suppress);
    }

    [Fact]
    public void DisabledWinSpacePassesThroughUnchanged() {
        var engine = new KeyboardShortcutEngine([ShortcutKind.CtrlShift]);

        var windowsDown = engine.Process(KeyboardShortcutEngine.VkLWin, isKeyDown: true);
        var spaceDown = engine.Process(KeyboardShortcutEngine.VkSpace, isKeyDown: true);
        var spaceUp = engine.Process(KeyboardShortcutEngine.VkSpace, isKeyDown: false);
        var windowsUp = engine.Process(KeyboardShortcutEngine.VkLWin, isKeyDown: false);

        Assert.False(windowsDown.Suppress);
        Assert.False(spaceDown.Suppress);
        Assert.False(spaceUp.Suppress);
        Assert.False(windowsUp.Suppress);
        Assert.Null(spaceDown.TriggeredShortcut);
        Assert.Null(spaceDown.ReplayEvents);
    }

    [Fact]
    public void DisabledCtrlShiftPassesThroughUnchanged() {
        var engine = new KeyboardShortcutEngine([ShortcutKind.WinSpace]);

        Assert.False(engine.Process(KeyboardShortcutEngine.VkLControl, isKeyDown: true).Suppress);
        Assert.False(engine.Process(KeyboardShortcutEngine.VkLShift, isKeyDown: true).Suppress);
        Assert.False(engine.Process(KeyboardShortcutEngine.VkLShift, isKeyDown: false).Suppress);
        Assert.False(engine.Process(KeyboardShortcutEngine.VkLControl, isKeyDown: false).Suppress);
    }

    [Fact]
    public void DisabledAltShiftPassesThroughUnchanged() {
        var engine = new KeyboardShortcutEngine([ShortcutKind.CtrlShift, ShortcutKind.WinSpace]);

        Assert.False(engine.Process(KeyboardShortcutEngine.VkLAlt, isKeyDown: true).Suppress);
        Assert.False(engine.Process(KeyboardShortcutEngine.VkLShift, isKeyDown: true).Suppress);
        Assert.False(engine.Process(KeyboardShortcutEngine.VkLShift, isKeyDown: false).Suppress);
        Assert.False(engine.Process(KeyboardShortcutEngine.VkLAlt, isKeyDown: false).Suppress);
    }

    [Theory]
    [InlineData(KeyboardShortcutEngine.VkLShift)]
    [InlineData(KeyboardShortcutEngine.VkRShift)]
    [InlineData(KeyboardShortcutEngine.VkLControl)]
    [InlineData(KeyboardShortcutEngine.VkRControl)]
    [InlineData(KeyboardShortcutEngine.VkLAlt)]
    [InlineData(KeyboardShortcutEngine.VkRAlt)]
    [InlineData(KeyboardShortcutEngine.VkLWin)]
    [InlineData(KeyboardShortcutEngine.VkRWin)]
    public void SingleModifierTapIsReplayedAsDownAndUp(int modifier) {
        var engine = new KeyboardShortcutEngine();

        var press = engine.Process(modifier, true);
        var release = engine.Process(modifier, false);

        Assert.True(press.Suppress);
        Assert.True(release.Suppress);
        Assert.Null(release.TriggeredShortcut);
        Assert.Equal([new SyntheticKeyEvent(modifier, true), new SyntheticKeyEvent(modifier, false)], release.ReplayEvents);
    }

    [Fact]
    public void ResetClearsStuckModifierAfterMissedKeyUp() {
        const int vkA = 0x41;
        var engine = new KeyboardShortcutEngine();

        // Control is pressed and buffered, but its key-up is never delivered
        // (e.g. the low-level hook was evicted mid-chord by Windows).
        Assert.True(engine.Process(KeyboardShortcutEngine.VkLControl, isKeyDown: true).Suppress);

        // Reinstalling the hook resets the engine, so the stuck modifier is
        // forgotten rather than corrupting the next keystroke.
        engine.Reset();

        var press = engine.Process(vkA, isKeyDown: true);
        Assert.False(press.Suppress);
        Assert.Null(press.ReplayEvents);
        Assert.Null(press.TriggeredShortcut);

        var release = engine.Process(vkA, isKeyDown: false);
        Assert.False(release.Suppress);
        Assert.Null(release.ReplayEvents);
    }

    [Theory]
    [InlineData(KeyboardShortcutEngine.VkLShift)]
    [InlineData(KeyboardShortcutEngine.VkRShift)]
    [InlineData(KeyboardShortcutEngine.VkLControl)]
    [InlineData(KeyboardShortcutEngine.VkRControl)]
    [InlineData(KeyboardShortcutEngine.VkLAlt)]
    [InlineData(KeyboardShortcutEngine.VkRAlt)]
    [InlineData(KeyboardShortcutEngine.VkLWin)]
    [InlineData(KeyboardShortcutEngine.VkRWin)]
    public void PointerInputReplaysBufferedModifierBeforeClick(int modifier) {
        var engine = new KeyboardShortcutEngine();

        Assert.True(engine.Process(modifier, isKeyDown: true).Suppress);
        var pointerInput = engine.ProcessPointerInput();

        Assert.False(pointerInput.Suppress);
        Assert.Equal([new SyntheticKeyEvent(modifier, true)], pointerInput.ReplayEvents);
        Assert.False(engine.Process(modifier, isKeyDown: false).Suppress);
    }

    [Fact]
    public void InjectedPointerInputDoesNotReplayBufferedModifier() {
        var engine = new KeyboardShortcutEngine();
        engine.Process(KeyboardShortcutEngine.VkLShift, isKeyDown: true);

        var injectedPointerInput = engine.ProcessPointerInput(isInjected: true);
        var physicalPointerInput = engine.ProcessPointerInput();

        Assert.Null(injectedPointerInput.ReplayEvents);
        Assert.Equal([new SyntheticKeyEvent(KeyboardShortcutEngine.VkLShift, true)], physicalPointerInput.ReplayEvents);
    }

    [Fact]
    public void PointerInputReplaysConsumedModifierStillHeldAfterShortcut() {
        var engine = new KeyboardShortcutEngine();
        engine.Process(KeyboardShortcutEngine.VkLControl, isKeyDown: true);
        engine.Process(KeyboardShortcutEngine.VkLShift, isKeyDown: true);
        engine.Process(KeyboardShortcutEngine.VkLShift, isKeyDown: false);

        var pointerInput = engine.ProcessPointerInput();

        Assert.Equal([new SyntheticKeyEvent(KeyboardShortcutEngine.VkLControl, true)], pointerInput.ReplayEvents);
        Assert.False(engine.Process(KeyboardShortcutEngine.VkLControl, isKeyDown: false).Suppress);
    }

    [Fact]
    public void OrdinarySingleKeyPassesThroughUnchanged() {
        const int vkA = 0x41;
        var engine = new KeyboardShortcutEngine();

        var press = engine.Process(vkA, isKeyDown: true);
        var release = engine.Process(vkA, isKeyDown: false);

        Assert.False(press.Suppress);
        Assert.False(release.Suppress);
        Assert.Null(press.TriggeredShortcut);
        Assert.Null(press.ReplayEvents);
        Assert.Null(release.TriggeredShortcut);
        Assert.Null(release.ReplayEvents);
    }

    [Fact]
    public void EveryNonModifierVirtualKeyPassesThroughWhenPressedAlone() {
        HashSet<int> watchedModifiers =
        [
            KeyboardShortcutEngine.VkLShift,
            KeyboardShortcutEngine.VkRShift,
            KeyboardShortcutEngine.VkLControl,
            KeyboardShortcutEngine.VkRControl,
            KeyboardShortcutEngine.VkLAlt,
            KeyboardShortcutEngine.VkRAlt,
            KeyboardShortcutEngine.VkLWin,
            KeyboardShortcutEngine.VkRWin
        ];

        for(var virtualKey = 1; virtualKey <= byte.MaxValue; virtualKey++) {
            if(watchedModifiers.Contains(virtualKey)) {
                continue;
            }

            var engine = new KeyboardShortcutEngine();
            var press = engine.Process(virtualKey, isKeyDown: true);
            var release = engine.Process(virtualKey, isKeyDown: false);

            Assert.False(press.Suppress);
            Assert.False(release.Suppress);
            Assert.Null(press.TriggeredShortcut);
            Assert.Null(release.TriggeredShortcut);
            Assert.Null(press.ReplayEvents);
            Assert.Null(release.ReplayEvents);
        }
    }
}
