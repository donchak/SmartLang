namespace SmartLang.Tests;

public sealed class NativeInputProfileActivatorTests {
    [Theory]
    [InlineData("Windows.UI.Core.CoreWindow", true)]
    [InlineData("ApplicationFrameWindow", false)]
    [InlineData("HwndWrapper[DefaultDomain;;]", false)]
    [InlineData("", false)]
    public void CoreWindowDetectionIsExact(
        string className,
        bool expected) {
        Assert.Equal(
            expected,
            NativeInputProfileActivator.IsCoreWindowClass(className));
    }
}
