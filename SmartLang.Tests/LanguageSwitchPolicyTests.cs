namespace SmartLang.Tests;

public sealed class LanguageSwitchPolicyTests
{
    [Theory]
    [InlineData("en-US", "fr-FR")]
    [InlineData("de-DE", "en-US")]
    [InlineData(null, "en-US")]
    public void PrimaryTargetFollowsConfiguredToggleRule(
        string? current,
        string expected)
    {
        var target = LanguageSwitchPolicy.GetPrimaryTargetLanguage(
            current,
            "en-US",
            "fr-FR");

        Assert.Equal(expected, target);
    }

    [Theory]
    [InlineData(-1, 3, 0)]
    [InlineData(0, 3, 1)]
    [InlineData(2, 3, 0)]
    [InlineData(0, 0, -1)]
    public void NextLayoutIndexWraps(
        int currentIndex,
        int count,
        int expected)
    {
        Assert.Equal(
            expected,
            LanguageSwitchPolicy.GetNextLayoutIndex(currentIndex, count));
    }
}
