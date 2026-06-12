namespace SmartLang.Tests;

public sealed class NativeHookShadowCopyTests: IDisposable {
    readonly string sourcePath = Path.Combine(Path.GetTempPath(), "SmartLang.Tests", $"{Guid.NewGuid():N}.dll");

    public NativeHookShadowCopyTests() {
        Directory.CreateDirectory(Path.GetDirectoryName(sourcePath)!);
        File.WriteAllText(sourcePath, "test");
    }

    [Fact]
    public void CreateCopiesLibraryToUniqueDirectory() {
        var (directory, libraryPath) = NativeHookShadowCopy.Create(sourcePath);

        try {
            Assert.True(Directory.Exists(directory));
            Assert.True(File.Exists(libraryPath));
            Assert.Equal(Path.GetFileName(sourcePath), Path.GetFileName(libraryPath));
            Assert.NotEqual(Path.GetDirectoryName(sourcePath), directory);
        } finally {
            NativeHookShadowCopy.TryDelete(directory);
        }
    }

    [Fact]
    public void CleanupStaleCopiesLeavesLockedDirectoriesForNextRun() {
        var (directory, libraryPath) = NativeHookShadowCopy.Create(sourcePath);

        using var stream = File.Open(libraryPath, FileMode.Open, FileAccess.Read, FileShare.None);

        NativeHookShadowCopy.CleanupStaleCopies();

        Assert.True(Directory.Exists(directory));
    }

    public void Dispose() {
        try {
            File.Delete(sourcePath);
        } catch(IOException) {
        } catch(UnauthorizedAccessException) {
        }
    }
}
