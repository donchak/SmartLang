namespace SmartLang.Tests;

public sealed class StartupManagerTests
{
    [Fact]
    public void StartupCommandQuotesExecutablePath()
    {
        Assert.Equal(
            "\"C:\\Program Files\\SmartLang\\SmartLang.exe\"",
            StartupManager.BuildCommand(
                "C:\\Program Files\\SmartLang\\SmartLang.exe"));
    }
}
