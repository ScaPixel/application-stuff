using NetworkingDemo.Networking.Core;
using NetworkingDemo.Shared.Settings;

namespace NetworkingDemo.Networking;

public interface IMatchmaker : IDisposable
{
    event Action<LobbyInfo> LobbyUpdate;
    event Action<NetSession> SessionStarted;
    event Action<NetworkType> NetworkToUse;
    
    Task<LobbyInfo> CreateLobby(string id, int numPlayers, bool isPublic);
    Task<LobbyInfo> UpdateLobby(int numPlayers);
    Task<LobbyInfo> JoinLobby(string id);
    void StartOrReady();
    void CancelStartOrReady();
    void LeaveLobby();
    Task<string[]> GetLobbyList();
    void SetTransportLayer(ITransportLayer transportLayer);
    void UpdateProfileInfo(UserProfileDto profile);
}
