using NetworkingDemo.Networking.Core;
using NetworkingDemo.Shared.Settings;

namespace NetworkingDemo.Networking;

public interface INetStack : IDisposable
{
    public event Action<INetwork> SessionNetworkReady;
    ITransportLayer? TransportLayer { get; }
    IReliabilityLayer? ReliabilityLayer { get; }
    IMatchmaker? Matchmaker { get; }
    INetwork? Network { get; }
    NetworkType Type { get; }
    
    bool Available { get; }

    /// <summary>
    /// Initialize and check that this stack will be functional
    /// </summary>
    /// <param name="router">The PacketRouter to use for packets received through this stack</param>
    /// <param name="profileDto">The profile info</param>
    /// <returns>True if stack connection is available</returns>
    Task<bool> Init(UserProfileDto profileDto);
    
    // INetwork GetNetwork(NetSession session);
}
