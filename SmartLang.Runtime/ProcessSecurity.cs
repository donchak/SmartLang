namespace SmartLang;

public static class ProcessSecurity {
    public static bool IsCurrentProcessElevated() {
        using var processHandle = NativeMethods.OpenProcess(
            NativeMethods.ProcessQueryLimitedInformation,
            false,
            (uint)Environment.ProcessId);
        return !processHandle.IsInvalid &&
            NativeMethods.IsProcessElevated(processHandle);
    }

    public static bool IsProtectedInstallation(string baseDirectory) {
        var fullPath = Path.GetFullPath(baseDirectory)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) +
            Path.DirectorySeparatorChar;
        var programFiles = Environment.GetFolderPath(
            Environment.SpecialFolder.ProgramFiles);
        var programFilesX86 = Environment.GetFolderPath(
            Environment.SpecialFolder.ProgramFilesX86);

        return IsUnder(fullPath, programFiles) ||
            IsUnder(fullPath, programFilesX86);
    }

    private static bool IsUnder(string fullPath, string parentDirectory) {
        if(string.IsNullOrWhiteSpace(parentDirectory)) {
            return false;
        }

        var parent = Path.GetFullPath(parentDirectory)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) +
            Path.DirectorySeparatorChar;
        return fullPath.StartsWith(parent, StringComparison.OrdinalIgnoreCase);
    }
}
