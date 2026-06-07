namespace SmartLang.Tests;

public sealed class KeyboardLayoutServiceTests
{
    private static InstalledLayout Layout(nint handle, string tag, string name = "") =>
        new(handle, tag, tag, string.IsNullOrEmpty(name) ? tag : name);

    [Fact]
    public void ResolveLayoutReturnsNullWhenLanguageIsNotInstalled()
    {
        var service = new KeyboardLayoutService(new LanguageCatalog());
        var layouts = new[] { Layout(1, "en-US") };

        Assert.Null(service.ResolveLayout("fr-FR", layouts));
    }

    [Fact]
    public void ResolveLayoutReturnsFirstMatchWhenNothingRemembered()
    {
        var service = new KeyboardLayoutService(new LanguageCatalog());
        var layouts = new[]
        {
            Layout(10, "en-US", "US"),
            Layout(11, "en-US", "US-Dvorak"),
        };

        var resolved = service.ResolveLayout("en-US", layouts);

        Assert.NotNull(resolved);
        Assert.Equal((nint)10, resolved!.Handle);
    }

    [Fact]
    public void ResolveLayoutPrefersRememberedHandle()
    {
        var service = new KeyboardLayoutService(new LanguageCatalog());
        var layouts = new[]
        {
            Layout(10, "en-US", "US"),
            Layout(11, "en-US", "US-Dvorak"),
        };

        service.Remember(layouts[1]);

        var resolved = service.ResolveLayout("en-US", layouts);

        Assert.Equal((nint)11, resolved!.Handle);
    }

    [Fact]
    public void ResolveLayoutFallsBackToFirstMatchWhenRememberedHandleIsNoLongerInstalled()
    {
        var service = new KeyboardLayoutService(new LanguageCatalog());

        service.Remember(Layout(99, "en-US", "Phantom"));

        var layouts = new[]
        {
            Layout(10, "en-US", "US"),
            Layout(11, "en-US", "US-Dvorak"),
        };

        var resolved = service.ResolveLayout("en-US", layouts);

        Assert.Equal((nint)10, resolved!.Handle);
    }

    [Fact]
    public void RememberIgnoresNull()
    {
        var service = new KeyboardLayoutService(new LanguageCatalog());

        service.Remember(null);

        Assert.Empty(service.RememberedLayouts);
    }

    [Fact]
    public void RememberOverwritesPriorEntryForSameLanguage()
    {
        var service = new KeyboardLayoutService(new LanguageCatalog());

        service.Remember(Layout(10, "en-US"));
        service.Remember(Layout(11, "en-US"));

        Assert.Equal((nint)11, service.RememberedLayouts["en-US"]);
        Assert.Single(service.RememberedLayouts);
    }

    [Fact]
    public void RememberIsCaseInsensitiveOnLanguageTag()
    {
        var service = new KeyboardLayoutService(new LanguageCatalog());

        var layouts = new[]
        {
            Layout(20, "en-US", "Variant-A"),
            Layout(21, "en-US", "Variant-B"),
        };
        service.Remember(layouts[1]);

        var resolved = service.ResolveLayout("EN-us", layouts);

        Assert.Equal((nint)21, resolved!.Handle);
    }
}
