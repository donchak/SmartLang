namespace SmartLang;

public sealed class AppSettings {
    public const int CurrentVersion = 2;

    public int Version { get; set; } = CurrentVersion;

    public string PrimaryLanguageTag { get; set; } = string.Empty;

    public string SecondaryLanguageTag { get; set; } = string.Empty;

    public ShortcutKind PrimaryShortcut { get; set; } = ShortcutKind.CtrlShift;

    public ShortcutKind AllLayoutsShortcut { get; set; } = ShortcutKind.WinSpace;

    public bool StartWithWindows { get; set; } = true;

    public bool AdministratorAppSupport { get; set; } = true;

    public AppSettings Copy() => new() {
        Version = Version,
        PrimaryLanguageTag = PrimaryLanguageTag,
        SecondaryLanguageTag = SecondaryLanguageTag,
        PrimaryShortcut = PrimaryShortcut,
        AllLayoutsShortcut = AllLayoutsShortcut,
        StartWithWindows = StartWithWindows,
        AdministratorAppSupport = AdministratorAppSupport
    };
}
