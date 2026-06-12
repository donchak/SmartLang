namespace SmartLang.Tests;

public sealed class ProcessSecurityTests {
    [Fact]
    public void ProgramFilesSubdirectoryIsProtectedInstallation() {
        var programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
        var path = Path.Combine(programFiles, "SmartLang");

        Assert.True(ProcessSecurity.IsProtectedInstallation(path));
    }

    [Fact]
    public void UserWritableTemporaryDirectoryIsNotProtectedInstallation() {
        var path = Path.Combine(Path.GetTempPath(), "SmartLang");

        Assert.False(ProcessSecurity.IsProtectedInstallation(path));
    }

    [Fact]
    public void SimilarProgramFilesPrefixIsNotAccepted() {
        var programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
        var path = programFiles + "-Untrusted";

        Assert.False(ProcessSecurity.IsProtectedInstallation(path));
    }
}
