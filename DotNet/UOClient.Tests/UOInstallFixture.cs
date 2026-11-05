using System.Text.Json;
using UOClient.IO;

namespace UOClient.Tests;

/// <summary>
/// Opens the UO data once per test run: UO_PATH, else the game's setting (UserSettings/unity-uo-settings.json),
/// which falls back to the UO Smallz client's own data.
/// </summary>
public sealed class UOInstallFixture : IDisposable
{
    public UOInstallFixture()
    {
        string path = Environment.GetEnvironmentVariable("UO_PATH") ?? Settings().ResolvedDataPath;
        if (!Directory.Exists(path))
            throw new InvalidOperationException($"UO data not found at '{path}'. Set UO_PATH, or uoDataPath in UserSettings/{ClientSettings.FileName}.");
        Data = new UOData(path);
    }

    public UOData Data { get; }

    private static ClientSettings Settings()
    {
        string? dir = AppContext.BaseDirectory;
        while (dir != null && !Directory.Exists(Path.Combine(dir, "Assets")))
            dir = Path.GetDirectoryName(dir);
        string file = Path.Combine(dir ?? ".", "UserSettings", ClientSettings.FileName);
        if (!File.Exists(file))
            return new ClientSettings();
        return JsonSerializer.Deserialize<ClientSettings>(File.ReadAllText(file), new JsonSerializerOptions { IncludeFields = true }) ?? new ClientSettings();
    }

    public void Dispose() => Data.Dispose();
}

[CollectionDefinition("UO install")]
public sealed class UOInstallCollection : ICollectionFixture<UOInstallFixture> { }
