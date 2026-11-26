using NetworkingDemo.Shared;

namespace NetworkingDemo.Networking;

public readonly struct LobbyInfo(string name, NetPeerDto[] peers, bool success)
{
    public string Name { get; } = name;
    public NetPeerDto[] Peers { get; } = peers;
    public bool Success { get; } = success;

    public static LobbyInfo Failed => new LobbyInfo("Failed", [], false);
}
