namespace SmartLang;

public static class LanguageSwitchPolicy
{
    public static string GetPrimaryTargetLanguage(
        string? currentLanguageTag,
        string primaryLanguageTag,
        string secondaryLanguageTag)
    {
        return string.Equals(
            currentLanguageTag,
            primaryLanguageTag,
            StringComparison.OrdinalIgnoreCase)
            ? secondaryLanguageTag
            : primaryLanguageTag;
    }

    public static int GetNextLayoutIndex(int currentIndex, int layoutCount)
    {
        if (layoutCount <= 0)
        {
            return -1;
        }

        return currentIndex < 0 || currentIndex >= layoutCount - 1
            ? 0
            : currentIndex + 1;
    }
}
