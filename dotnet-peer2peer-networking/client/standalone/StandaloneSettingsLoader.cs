using System.Text.Json;
using NetworkingDemo.Shared.Logging;

namespace NetworkingDemo.Client.Settings;

#if DEBUG
public static class StandaloneSettingsLoader
{
    public static IClientSettings ClientSettings { get; private set; }

    public static async Task<bool> TryLoadAsync(string path)
    {
        if (!File.Exists(path))
        {
            return false;
        }

        string json = await File.ReadAllTextAsync(path);
        StandaloneSettings settings = JsonSerializer.Deserialize<StandaloneSettings>(json)!;

        ClientSettings = settings;
        return true;
    }
}
#endif
