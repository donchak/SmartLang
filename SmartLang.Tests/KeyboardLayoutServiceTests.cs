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

    [Fact]
    public void ActivateLayoutUsesExactTargetHandle()
    {
        var activator = new RecordingInputProfileActivator();
        var service = new KeyboardLayoutService(new LanguageCatalog(), activator);

        var result = service.ActivateLayout(123, (nint)10, (nint)20);

        Assert.True(result);
        Assert.Equal(123u, activator.ThreadId);
        Assert.Equal((nint)20, activator.ActivatedHandle);
    }

    [Fact]
    public void ActivateLayoutDoesNothingWhenTargetIsAlreadyActive()
    {
        var activator = new RecordingInputProfileActivator();
        var service = new KeyboardLayoutService(new LanguageCatalog(), activator);

        var result = service.ActivateLayout(123, (nint)20, (nint)20);

        Assert.True(result);
        Assert.Null(activator.ActivatedHandle);
    }

    [Fact]
    public void ActivateLayoutReturnsActivatorFailure()
    {
        var activator = new RecordingInputProfileActivator
        {
            Result = false
        };
        var service = new KeyboardLayoutService(new LanguageCatalog(), activator);

        var result = service.ActivateLayout(123, (nint)10, (nint)20);

        Assert.False(result);
        Assert.Equal((nint)20, activator.ActivatedHandle);
    }

    private sealed class RecordingInputProfileActivator : IInputProfileActivator
    {
        public bool Result { get; init; } = true;

        public nint? ActivatedHandle { get; private set; }

        public uint? ThreadId { get; private set; }

        public bool ActivateKeyboardLayout(uint threadId, nint layoutHandle)
        {
            ThreadId = threadId;
            ActivatedHandle = layoutHandle;
            return Result;
        }

        public void Dispose()
        {
        }
    }
}
