using System.Runtime.InteropServices;

namespace SmartLang.Tests;

public sealed class NativeMethodsTests
{
    [Fact]
    public void InputStructureMatchesWindowsAbi()
    {
        var expectedSize = Environment.Is64BitProcess ? 40 : 28;

        Assert.Equal(expectedSize, Marshal.SizeOf<NativeMethods.Input>());
    }

    [Fact]
    public void MouseHookDataStructureMatchesWindowsAbi()
    {
        var expectedSize = Environment.Is64BitProcess ? 32 : 24;

        Assert.Equal(expectedSize, Marshal.SizeOf<NativeMethods.MouseHookData>());
    }
}
