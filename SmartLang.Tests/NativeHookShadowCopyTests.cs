namespace SmartLang.Tests;

public sealed class NativeHookShadowCopyTests: IDisposable {
    private readonly string _sourcePath = Path.Combine(
        Path.GetTempPath(),
        "SmartLang.Tests",
        $"{Guid.NewGuid():N}.dll");

    public NativeHookShadowCopyTests() {
        Directory.CreateDirectory(Path.GetDirectoryName(_sourcePath)!);
        File.WriteAllText(_sourcePath, "test");
    }

    [Fact]
    public void CreateCopiesLibraryToUniqueDirectory() {
        var (directory, libraryPath) = NativeHookShadowCopy.Create(_sourcePath);

        try {
            Assert.True(Directory.Exists(directory));
            Assert.True(File.Exists(libraryPath));
            Assert.Equal(Path.GetFileName(_sourcePath), Path.GetFileName(libraryPath));
            Assert.NotEqual(Path.GetDirectoryName(_sourcePath), directory);
        } finally {
            NativeHookShadowCopy.TryDelete(directory);
        }
    }

    [Fact]
    public void CleanupStaleCopiesLeavesLockedDirectoriesForNextRun() {
        var (directory, libraryPath) = NativeHookShadowCopy.Create(_sourcePath);

        using var stream = File.Open(
            libraryPath,
            FileMode.Open,
            FileAccess.Read,
            FileShare.None);

        NativeHookShadowCopy.CleanupStaleCopies();

        Assert.True(Directory.Exists(directory));
    }

    public void Dispose() {
        try {
            File.Delete(_sourcePath);
        } catch(IOException) {
        } catch(UnauthorizedAccessException) {
        }
    }
}
