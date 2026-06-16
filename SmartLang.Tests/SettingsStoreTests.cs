namespace SmartLang.Tests;

public sealed class SettingsStoreTests: IDisposable {
    readonly string directory = Path.Combine(Path.GetTempPath(), $"SmartLang.Tests.{Guid.NewGuid():N}");

    [Fact]
    public void SettingsRoundTrip() {
        var path = Path.Combine(directory, "settings.json");
        var store = new SettingsStore(path);
        var expected = new AppSettings {
            PrimaryLanguageTag = "en-US",
            SecondaryLanguageTag = "fr-FR",
            PrimaryShortcut = ShortcutKind.WinSpace,
            AllLayoutsShortcut = ShortcutKind.CtrlShift,
            SwitchingMode = SwitchingMode.RecentLanguages,
            StartWithWindows = true,
            AdministratorAppSupport = false
        };

        store.Save(expected);
        var actual = store.Load();

        Assert.Equal(expected.Version, actual.Version);
        Assert.Equal(expected.PrimaryLanguageTag, actual.PrimaryLanguageTag);
        Assert.Equal(expected.SecondaryLanguageTag, actual.SecondaryLanguageTag);
        Assert.Equal(expected.PrimaryShortcut, actual.PrimaryShortcut);
        Assert.Equal(expected.AllLayoutsShortcut, actual.AllLayoutsShortcut);
        Assert.Equal(expected.SwitchingMode, actual.SwitchingMode);
        Assert.Equal(expected.StartWithWindows, actual.StartWithWindows);
        Assert.Equal(expected.AdministratorAppSupport, actual.AdministratorAppSupport);
    }

    [Fact]
    public void InvalidJsonReturnsDefaults() {
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, "settings.json");
        File.WriteAllText(path, "{not-json");

        var settings = new SettingsStore(path).Load();

        Assert.Equal(AppSettings.CurrentVersion, settings.Version);
        Assert.Empty(settings.PrimaryLanguageTag);
        Assert.Empty(settings.SecondaryLanguageTag);
    }

    [Fact]
    public void NoneAllLayoutsShortcutRoundTrips() {
        var path = Path.Combine(directory, "settings.json");
        var store = new SettingsStore(path);
        var expected = new AppSettings {
            PrimaryLanguageTag = "en-US",
            SecondaryLanguageTag = "fr-FR",
            AllLayoutsShortcut = ShortcutKind.None
        };

        store.Save(expected);
        var actual = store.Load();

        Assert.Equal(ShortcutKind.None, actual.AllLayoutsShortcut);
    }

    [Fact]
    public void UnknownOlderSchemaVersionReturnsDefaults() {
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, "settings.json");
        File.WriteAllText(path, """
            {
              "Version": 0,
              "PrimaryLanguageTag": "en-US",
              "SecondaryLanguageTag": "fr-FR",
              "PrimaryShortcut": "CtrlShift",
              "AllLayoutsShortcut": "WinSpace",
              "StartWithWindows": true
            }
            """);

        var settings = new SettingsStore(path).Load();

        Assert.Equal(AppSettings.CurrentVersion, settings.Version);
        Assert.Empty(settings.PrimaryLanguageTag);
        Assert.Empty(settings.SecondaryLanguageTag);
        Assert.True(settings.StartWithWindows);
        Assert.True(settings.AdministratorAppSupport);
    }

    [Fact]
    public void VersionOneSettingsAreMigratedWithAdministratorSupportEnabled() {
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, "settings.json");
        File.WriteAllText(path, """
            {
              "Version": 1,
              "PrimaryLanguageTag": "en-US",
              "SecondaryLanguageTag": "fr-FR",
              "PrimaryShortcut": "CtrlShift",
              "AllLayoutsShortcut": "WinSpace",
              "StartWithWindows": false
            }
            """);

        var settings = new SettingsStore(path).Load();

        Assert.Equal(AppSettings.CurrentVersion, settings.Version);
        Assert.Equal("en-US", settings.PrimaryLanguageTag);
        Assert.Equal("fr-FR", settings.SecondaryLanguageTag);
        Assert.False(settings.StartWithWindows);
        Assert.True(settings.AdministratorAppSupport);
        Assert.Equal(SwitchingMode.PrimaryLanguages, settings.SwitchingMode);
    }

    [Fact]
    public void VersionTwoSettingsAreMigratedWithPrimarySwitchingMode() {
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, "settings.json");
        File.WriteAllText(path, """
            {
              "Version": 2,
              "PrimaryLanguageTag": "en-US",
              "SecondaryLanguageTag": "fr-FR",
              "PrimaryShortcut": "CtrlShift",
              "AllLayoutsShortcut": "WinSpace",
              "StartWithWindows": false,
              "AdministratorAppSupport": false
            }
            """);

        var settings = new SettingsStore(path).Load();

        Assert.Equal(AppSettings.CurrentVersion, settings.Version);
        Assert.Equal("en-US", settings.PrimaryLanguageTag);
        Assert.Equal("fr-FR", settings.SecondaryLanguageTag);
        Assert.False(settings.AdministratorAppSupport);
        Assert.Equal(SwitchingMode.PrimaryLanguages, settings.SwitchingMode);
    }

    [Fact]
    public void NewerSchemaVersionReturnsDefaults() {
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, "settings.json");
        File.WriteAllText(path, """
            {"Version": 999, "PrimaryLanguageTag": "en-US"}
            """);

        var settings = new SettingsStore(path).Load();

        Assert.Equal(AppSettings.CurrentVersion, settings.Version);
        Assert.Empty(settings.PrimaryLanguageTag);
    }

    [Fact]
    public void SaveCleansUpTemporaryFileWhenMoveTargetIsLocked() {
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, "settings.json");
        var tempPath = path + ".tmp";

        using(var lockStream = new FileStream(path, FileMode.Create, FileAccess.ReadWrite, FileShare.None)) {
            Assert.ThrowsAny<Exception>(() => new SettingsStore(path).Save(new AppSettings()));
        }

        Assert.False(File.Exists(tempPath));
    }

    public void Dispose() {
        if(Directory.Exists(directory)) {
            Directory.Delete(directory, recursive: true);
        }
    }
}
