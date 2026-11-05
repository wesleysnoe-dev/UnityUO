using System.Text.Json;
using UOClient.IO;

namespace UOClient.Cli;

/// <summary>The game's settings file (UserSettings/unity-uo-settings.json) for the command-line tools.</summary>
internal static class SettingsFile
{
    private static readonly JsonSerializerOptions Options = new() { IncludeFields = true, PropertyNameCaseInsensitive = true };

    /// <summary>The project's settings with launch arguments (-server, -uodata, -overrides, also with --) applied.</summary>
    public static ClientSettings Load(IEnumerable<string> args)
    {
        ClientSettings settings = new();
        string path = Path.Combine(ProjectRoot(), "UserSettings", ClientSettings.FileName);
        try
        {
            if (File.Exists(path))
                settings = JsonSerializer.Deserialize<ClientSettings>(File.ReadAllText(path), Options) ?? settings;
        }
        catch (Exception e)
        {
            Console.Error.WriteLine($"{path} is unreadable ({e.Message}); using defaults.");
        }
        return settings.ApplyArguments(args.ToArray());
    }

    public static string ProjectRoot()
    {
        string? dir = AppContext.BaseDirectory;
        while (dir != null && !Directory.Exists(Path.Combine(dir, "Assets")))
            dir = Path.GetDirectoryName(dir);
        return dir ?? Directory.GetCurrentDirectory();
    }
}
