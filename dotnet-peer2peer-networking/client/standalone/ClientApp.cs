using System.Reflection;
using NetworkingDemo.Client.Settings;

namespace NetworkingDemo.Client;

#if DEBUG
public static class ClientApp
{
    private static ClientCore _clientCore;

    public static async Task<int> Main(string[] args)
    {
        await StartAsync();
        await ApplicationLogic();
        await StopAsync();
        return 0;
    }

    private static async Task ApplicationLogic()
    {
        await _clientCore.ShouldExit.Task;
    }

    private static async Task StartAsync()
    {
        
        _clientCore = new ClientCore(await LoadUserSettings());
        await _clientCore.InitializeAsync(GetProjectRoot());
    }

    private static async Task StopAsync()
    {
        await _clientCore.ShutdownAsync();
    }
    
    private static string GetProjectRoot()
    {
        string assemblyPath = Assembly.GetExecutingAssembly().Location;
        string assemblyDir = Path.GetDirectoryName(assemblyPath)!;

        return Path.GetFullPath(Path.Combine(assemblyDir, ".."));
    }

    private static async Task<IClientSettings> LoadUserSettings()
    {
        string assemblyPath = Assembly.GetExecutingAssembly().Location;
        string assemblyDir = Path.GetDirectoryName(assemblyPath)!;
        
        string settingsFile = Path.Combine(assemblyDir, "..", "standalone", "settings.json");
        if (await StandaloneSettingsLoader.TryLoadAsync(settingsFile))
        {
            return StandaloneSettingsLoader.ClientSettings;
        }
        return null!;
    }
}
#endif

