namespace SmartLang.Tests;

public sealed class KeyboardHookTests {
    [Theory]
    [InlineData(NativeMethods.WmLButtonDown)]
    [InlineData(NativeMethods.WmLButtonUp)]
    [InlineData(NativeMethods.WmRButtonDown)]
    [InlineData(NativeMethods.WmRButtonUp)]
    [InlineData(NativeMethods.WmMButtonDown)]
    [InlineData(NativeMethods.WmMButtonUp)]
    [InlineData(NativeMethods.WmMouseWheel)]
    [InlineData(NativeMethods.WmXButtonDown)]
    [InlineData(NativeMethods.WmXButtonUp)]
    [InlineData(NativeMethods.WmMouseHWheel)]
    public void MouseButtonsAndWheelsArePointerInteractions(int message) {
        Assert.True(KeyboardHook.IsPointerInteractionMessage(message));
    }

    [Theory]
    [InlineData(0x0200)]
    [InlineData(0x0203)]
    [InlineData(0x0206)]
    [InlineData(0x0209)]
    [InlineData(0x020D)]
    public void MouseMovementAndDoubleClickMessagesAreNotPointerInteractions(int message) {
        Assert.False(KeyboardHook.IsPointerInteractionMessage(message));
    }
}
