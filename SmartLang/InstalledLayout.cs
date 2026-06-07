namespace SmartLang;

public sealed record InstalledLayout(
    nint Handle,
    string LanguageTag,
    string LanguageDisplayName,
    string LayoutName);

public sealed record LanguageOption(string LanguageTag, string DisplayName);
