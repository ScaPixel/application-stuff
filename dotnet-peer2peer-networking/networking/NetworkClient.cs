using NetworkingDemo.Shared.Logging;
using NetworkingDemo.Shared.Settings;
using NetworkingDemo.Shared.Types;

namespace NetworkingDemo.Networking;

/* Manages the whole network, is the top-layer
 * Initializes and holds references to all lower systems (one layer down).
 * Has network session that was created by matchmaker
 * Exposes events and subsystems to other non-network systems
 */

/*
 Backend selection, Holds transport, translates into transport.
 Connection lifecycle, Messages, reliability etc.

 Expose Networking APIs:
 - Matchmaking API
 - Session API
 - Transport API
*/

[Flags]
public enum NetworkType
{
    None = 0,
    CustomUdp = 1 << 0,
    Steam = 1 << 1,
    Epic = 1 << 2,
    Other = 1 << 3,
}

public class NetworkClient(UserProfileDto userProfileDto) : IDisposable
{
    public event Action<IGameNetwork>? SessionNetworkReady;
    public UserProfileDto UserProfile { get; private set; } = userProfileDto;
    public IMatchmaker? Matchmaker => _selectedStack?.Matchmaker;
    public IGameNetwork? GameNetwork => _selectedStack?.GameNetwork;
    // private PacketRouter _packetRouter { get; set; }
    private readonly List<INetStack> _netStacks = new();
    private INetStack? _selectedStack;

    private NetworkType _availableNetworksType = NetworkType.None;

    public async Task Init()
    {
        await InitCustomUdp();
#if STEAM
        await InitSteam();
#endif
#if EOS
        await InitEos();
#endif
        SelectStack();
    }

    private async Task InitCustomUdp()
    {
        Log.Print("NetworkClient: InitCustomUdp | Starting to init custom network", LogLevel.Verbose);
        CustomNetStack netStack = new CustomNetStack();
        Log.Print("NetworkClient: InitCustomUdp | Created CustomNetStack", LogLevel.Verbose);

        if (!await netStack.Init(UserProfile))
        {
            Log.Print("NetworkClient: InitCustomUdp | Failed to initialize custom network stack", LogLevel.Error);
            return;
        }

        netStack.SessionNetworkReady += OnSessionNetworkReady;
        Log.Print("NetworkClient: InitCustomUdp | Connected OnSessionNetworkReady", LogLevel.Verbose);
        _availableNetworksType |= NetworkType.CustomUdp;
        _netStacks.Add(netStack);
        Log.Print("NetworkClient: InitCustomUdp | Added CustomNetStack to list", LogLevel.Verbose);
    }

    // TODO implement the other stacks when needed
    // private async Task InitSteam() {...}
    // private async Task InitEos() {...}

    private void SelectStack()
    {
        Log.Print("NetworkClient: SelectStack | Selecting stack", LogLevel.Verbose);

        // TODO make this actually switch stacks when more are implemented
        _selectedStack = _netStacks[0];
    }

    private void OnSessionNetworkReady(IGameNetwork gameNetwork)
    {
        SessionNetworkReady?.Invoke(gameNetwork);
    }

    public void Dispose()
    {
        foreach (var netStack in _netStacks)
        {
            netStack.SessionNetworkReady -= OnSessionNetworkReady;
            netStack.Dispose();
        }
    }
}
