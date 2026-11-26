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
    public event Action<INetwork>? SessionNetworkReady;
    public UserProfileDto UserProfile { get; private set; } = userProfileDto;
    public IMatchmaker? Matchmaker => _selectedStack?.Matchmaker;
    public INetwork? Network => _selectedStack?.Network;
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
        CustomNetStack netStack = new CustomNetStack();
        
        if (!await netStack.Init(UserProfile))
        {
            return;
        }

        netStack.SessionNetworkReady += OnSessionNetworkReady;
        _availableNetworksType |= NetworkType.CustomUdp;
        _netStacks.Add(netStack);
    }

    // TODO implement the other stacks when needed
    // private async Task InitSteam() {...}
    // private async Task InitEos() {...}

    private void SelectStack()
    {
        // TODO make this actually switch stacks when more are implemented
        _selectedStack = _netStacks[0];
    }

    private void OnSessionNetworkReady(INetwork Network)
    {
        SessionNetworkReady?.Invoke(Network);
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
