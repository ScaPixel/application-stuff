using NetworkingDemo.Client.Settings;
using NetworkingDemo.Networking;
using NetworkingDemo.Shared.Commands;
using NetworkingDemo.Shared.Configuration;
using NetworkingDemo.Shared.Logging;
using NetworkingDemo.Shared.Settings;
using NetworkingDemo.Simulation;

namespace NetworkingDemo.Client;

public class ClientCore(IClientSettings settings)
{
    NetworkClient? _networkClient;
    private ClientSession? _session;
    public SimState? SimState => _session?.State;
    public IMatchmaker? Matchmaker => _networkClient?.Matchmaker;
    public readonly TaskCompletionSource ShouldExit = new();
    public event Action? SimReady;
    
    public async Task InitializeAsync(string rootPath)
    {
        InitConsoleLogger();
        if (!await LoadConfig(Path.Combine(rootPath, "config")))
        {
            return;
        }
        
        // TODO Initialize client logic
        
        _networkClient = new NetworkClient(BuildDto());
        await _networkClient.Init();
        _networkClient.SessionNetworkReady += OnNetSessionReady; // TODO move to better place
    }

    public async Task ShutdownAsync()
    {
        // TODO make shutdown logic
        
        _networkClient!.SessionNetworkReady -= OnNetSessionReady; // TODO move to better place
        _networkClient.Dispose();
    }

    public void EnqueueCommand(ICommand command)
    {
        switch (command)
        {
            case QuitCommand:
                HandleQuit();
                break;
            case Command cmd:
                EnqueueCommand(cmd);
                break;
        }
    }
    
    private void HandleQuit()
    {
        // If no session is active. Stop program
        if (_session is not { Running: true })
        {
            ShouldExit.TrySetResult();
        }
        else
        {
            _session!.Stop();
        }
    }

    private void EnqueueCommand(ICommand command)
    {
        if (_session == null)
        {
            return;
        }
        
        _session!.EnqueueCommand(command);
    }

    private void OnNetSessionReady(ISessionNetwork network)
    {
        SimReady?.Invoke();
        _session = new ClientSession(network);
    }

    public void StartSession()
    {
        _session?.Start();
    }
    
    private void InitConsoleLogger()
    {
        ConsoleLogger logger = new ConsoleLogger();
        logger.SetSeverity(LogLevel.All);
        Log.RegisterLogger(logger, LoggerType.SystemConsole);
        
        Log.Print("ClientCore: InitConsoleLogger | Console logger initialized", LogLevel.Verbose);
    }

    private async Task<bool> LoadConfig(string pathToSettingsFolder)
    {
        string filePath = Path.Combine(pathToSettingsFolder, File.Exists(Path.Combine(pathToSettingsFolder, "config.local.json")) ? "config.local.json" : "config.json");
        return await Config.TryLoad(filePath);
    }

    private UserProfileDto BuildDto()
    {
        return new UserProfileDto()
        {
            DisplayName = settings.DisplayName,
        };
    }
}
