namespace SmartLang;

internal static class AppLog {
    private const long MaximumLength = 1_000_000;
    private static readonly object Sync = new();

    internal static readonly string FilePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "SmartLang",
        "SmartLang.log");

    internal static void Write(string message) {
        try {
            lock(Sync) {
                var directory = Path.GetDirectoryName(FilePath)!;
                Directory.CreateDirectory(directory);

                if(File.Exists(FilePath) &&
                    new FileInfo(FilePath).Length >= MaximumLength) {
                    File.Move(FilePath, FilePath + ".old", overwrite: true);
                }

                File.AppendAllText(
                    FilePath,
                    $"{DateTimeOffset.Now:O} [P{Environment.ProcessId}:T{Environment.CurrentManagedThreadId}] {message}{Environment.NewLine}");
            }
        } catch(Exception exception) when(
              exception is IOException or UnauthorizedAccessException) {
        }
    }
}
