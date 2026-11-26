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
    private ClientGameSession? _gameSession;
    public SimState? SimState => _gameSession?.State;
    public IMatchmaker? Matchmaker => _networkClient?.Matchmaker;
    public readonly TaskCompletionSource ShouldExit = new();
    public event Action? SimReady;
    
    public async Task InitializeAsync(string rootPath)
    {
        Log.Print("ClientCore: InitializeAsync | Starting initialization", LogLevel.Verbose);
        InitConsoleLogger();
        if (!await LoadConfig(Path.Combine(rootPath, "config")))
        {
            Log.Print("ClientCore: Initialize config failed", LogLevel.Error);
            return;
        }
        
        // TODO Initialize client logic
        
        // TODO Initialize networking
        Log.Print("ClientCore: InitializeAsync | Starting to create network client", LogLevel.Verbose);
        _networkClient = new NetworkClient(BuildDto());
        Log.Print("ClientCore: InitializeAsync | Initialize network client", LogLevel.Verbose);
        await _networkClient.Init();
        Log.Print("ClientCore: InitializeAsync | Network client initialized", LogLevel.Verbose);
        _networkClient.SessionNetworkReady += OnNetSessionReady; // TODO move to better place
        Log.Print("ClientCore: InitializeAsync | Network client OnNetSessionReady connected", LogLevel.Verbose);
        Log.Print("ClientCore: InitializeAsync | Initialization completed");
    }

    public async Task ShutdownAsync()
    {
        // TODO make shutdown logic
        
        _networkClient!.SessionNetworkReady -= OnNetSessionReady; // TODO move to better place
        _networkClient.Dispose();
        Log.Print("ClientCore: ShutdownAsync | Shutdown complete");
    }

    public void EnqueueCommand(ICommand command)
    {
        switch (command)
        {
            case QuitCommand:
                HandleQuit();
                break;
            case IGameCommand gameCommand:
                EnqueueGameCommand(gameCommand);
                break;
        }
    }
    
    private void HandleQuit()
    {
        // If no session is active. Stop program
        if (_gameSession is not { Running: true })
        {
            Log.Print("ClientCore: HandleQuit | Called without Running Game Session. Stopping Client");
            ShouldExit.TrySetResult();
        }
        else
        {
            Log.Print("ClientCore: HandleQuit | Stopping game session");
            _gameSession!.Stop();
        }
    }

    private void EnqueueGameCommand(IGameCommand command)
    {
        if (_gameSession == null)
        {
            Log.Print("ClientCore: EnqueueGameCommand | Can't enqueue a command when no session is running",
                LogLevel.Warning);
            return;
        }
        
        Log.Print($"ClientCore: EnqueueGameCommand | Enqueue command: {command}", LogLevel.Verbose);
        _gameSession!.EnqueueCommand(command);
    }

    // private void HandleClientCommand(IClientCommand command)
    // {
    //     switch (command)
    //     {
    //         case CreateSessionCommand cmd:
    //             _networkClient.Matchmaker?.CreateLobby(cmd.Id, cmd.MaxPeers, cmd.Public);
    //             break;
    //         case JoinSessionCommand cmd:
    //             _networkClient.Matchmaker?.JoinLobby(cmd.Id);
    //             break;
    //         case StartSessionCommand:
    //             _networkClient.Matchmaker?.StartOrReady();
    //             break;
    //         case CancelSessionStartCommand:
    //             _networkClient.Matchmaker?.CancelStartOrReady();
    //             break;
    //         case UpdateSessionCommand cmd:
    //             _networkClient.Matchmaker?.UpdateLobby(cmd.MaxPeers);
    //             break;
    //     }
    // }

    private void OnNetSessionReady(IGameNetwork gameNetwork)
    {
        // Create the game network for this session
        Log.Print("ClientCore: OnNetSessionReady | Net session ready", LogLevel.Verbose);
        SimReady?.Invoke();
        _gameSession = new ClientGameSession(gameNetwork);
        // _gameSession.Start();
    }

    public void StartGameSession()
    {
        Log.Print("ClientCore: StartGameSession | Starting game session", LogLevel.Verbose);
        _gameSession?.Start();
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
