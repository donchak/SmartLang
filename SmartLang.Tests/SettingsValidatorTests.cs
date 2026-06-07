namespace SmartLang.Tests;

public sealed class SettingsValidatorTests
{
    private static readonly LanguageOption[] Languages =
    [
        new("en-US", "English"),
        new("fr-FR", "French")
    ];

    [Fact]
    public void ValidSettingsAreAccepted()
    {
        Assert.Null(SettingsValidator.Validate(CreateValidSettings(), Languages));
    }

    [Fact]
    public void SameLanguageIsRejected()
    {
        var settings = CreateValidSettings();
        settings.SecondaryLanguageTag = settings.PrimaryLanguageTag;

        Assert.Equal(
            "The two primary languages must be different.",
            SettingsValidator.Validate(settings, Languages));
    }

    [Fact]
    public void SameShortcutIsRejected()
    {
        var settings = CreateValidSettings();
        settings.AllLayoutsShortcut = settings.PrimaryShortcut;

        Assert.Equal(
            "The two commands must use different shortcuts.",
            SettingsValidator.Validate(settings, Languages));
    }

    [Fact]
    public void RemovedLanguageIsRejected()
    {
        var settings = CreateValidSettings();
        LanguageOption[] languagesAfterRemoval =
        [
            Languages[0],
            new("de-DE", "German")
        ];

        Assert.Equal(
            "One or more configured languages are no longer installed.",
            SettingsValidator.Validate(settings, languagesAfterRemoval));
    }

    private static AppSettings CreateValidSettings() => new()
    {
        PrimaryLanguageTag = "en-US",
        SecondaryLanguageTag = "fr-FR",
        PrimaryShortcut = ShortcutKind.CtrlShift,
        AllLayoutsShortcut = ShortcutKind.WinSpace
    };
}
