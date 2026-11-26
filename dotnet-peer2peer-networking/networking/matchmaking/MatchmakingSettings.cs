using NetworkingDemo.Networking.Core;
using NetworkingDemo.Shared.Settings;

namespace NetworkingDemo.Networking;

public struct MatchmakingSettings
{
    public UserProfileDto ProfileInfo { get; set; }
    public bool SymNat { get; init; } // Symmetric nat
    public ITransportLayer TransportLayer { get; init; }
}
