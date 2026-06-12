using System.Windows.Forms;

namespace SmartLang;

public sealed class LanguageCatalog {
    public IReadOnlyList<InstalledLayout> GetInstalledLayouts() {
        var layouts = new List<InstalledLayout>();

        foreach(InputLanguage inputLanguage in InputLanguage.InstalledInputLanguages) {
            layouts.Add(
                new InstalledLayout(
                    inputLanguage.Handle,
                    inputLanguage.Culture.Name,
                    inputLanguage.Culture.DisplayName,
                    inputLanguage.LayoutName));
        }

        return layouts;
    }

    public IReadOnlyList<LanguageOption> GetLanguageOptions(
        IReadOnlyList<InstalledLayout>? layouts = null) {
        layouts ??= GetInstalledLayouts();

        return layouts
            .GroupBy(layout => layout.LanguageTag, StringComparer.OrdinalIgnoreCase)
            .Select(group => new LanguageOption(group.Key, $"{group.First().LanguageDisplayName} [{group.Key}]"))
            .OrderBy(language => language.DisplayName, StringComparer.CurrentCultureIgnoreCase)
            .ToArray();
    }
}
