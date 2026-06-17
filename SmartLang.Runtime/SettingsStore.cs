using System.Text.Json;
using System.Text.Json.Serialization;

namespace SmartLang;

public sealed class SettingsStore {
    static readonly JsonSerializerOptions JsonOptions = new() {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() }
    };

    public SettingsStore(string? filePath = null) {
        FilePath = filePath ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "SmartLang",
            "settings.json");
    }

    public string FilePath { get; }

    public AppSettings Load() {
        try {
            if(!File.Exists(FilePath)) {
                return new AppSettings();
            }

            var settings = JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(FilePath), JsonOptions);

            return settings?.Version switch {
                AppSettings.CurrentVersion => settings,
                2 => MigrateVersion2(settings),
                1 => MigrateVersion2(MigrateVersion1(settings)),
                _ => new AppSettings()
            };
        } catch(JsonException) {
            return new AppSettings();
        } catch(IOException) {
            return new AppSettings();
        } catch(UnauthorizedAccessException) {
            return new AppSettings();
        }
    }

    public void Save(AppSettings settings) {
        var directory = Path.GetDirectoryName(FilePath)
            ?? throw new InvalidOperationException("The settings path has no parent directory.");

        Directory.CreateDirectory(directory);
        var temporaryPath = FilePath + ".tmp";
        var moved = false;
        try {
            File.WriteAllText(temporaryPath, JsonSerializer.Serialize(settings, JsonOptions));
            File.Move(temporaryPath, FilePath, overwrite: true);
            moved = true;
        } finally {
            if(!moved) {
                try {
                    File.Delete(temporaryPath);
                } catch(IOException) {
                } catch(UnauthorizedAccessException) {
                }
            }
        }
    }

    static AppSettings MigrateVersion1(AppSettings settings) {
        settings.Version = 2;
        settings.AdministratorAppSupport = true;
        return settings;
    }

    static AppSettings MigrateVersion2(AppSettings settings) {
        settings.Version = AppSettings.CurrentVersion;
        settings.SwitchingMode = SwitchingMode.PrimaryLanguages;
        return settings;
    }
}
