namespace SmartLang;

public static class SettingsValidator
{
    public static string? Validate(AppSettings settings, IReadOnlyCollection<LanguageOption> languages)
    {
        if (languages.Count < 2)
        {
            return "At least two Windows input languages must be installed.";
        }

        if (string.IsNullOrWhiteSpace(settings.PrimaryLanguageTag) ||
            string.IsNullOrWhiteSpace(settings.SecondaryLanguageTag))
        {
            return "Select both primary languages.";
        }

        if (string.Equals(
            settings.PrimaryLanguageTag,
            settings.SecondaryLanguageTag,
            StringComparison.OrdinalIgnoreCase))
        {
            return "The two primary languages must be different.";
        }

        if (settings.PrimaryShortcut == ShortcutKind.None)
        {
            return "Select a shortcut for switching primary languages.";
        }

        if (settings.AllLayoutsShortcut != ShortcutKind.None &&
            settings.PrimaryShortcut == settings.AllLayoutsShortcut)
        {
            return "The two commands must use different shortcuts.";
        }

        var availableTags = languages
            .Select(language => language.LanguageTag)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        if (!availableTags.Contains(settings.PrimaryLanguageTag) ||
            !availableTags.Contains(settings.SecondaryLanguageTag))
        {
            return "One or more configured languages are no longer installed.";
        }

        return null;
    }
}
