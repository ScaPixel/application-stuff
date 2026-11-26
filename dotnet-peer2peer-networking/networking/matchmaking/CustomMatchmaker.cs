using System.Net;
using System.Text;
using NetworkingDemo.Networking.Core;
using NetworkingDemo.Shared.Configuration;
using NetworkingDemo.Shared.Logging;
using NetworkingDemo.Shared.Settings;

namespace NetworkingDemo.Networking;

public class CustomMatchmaker : IMatchmaker
{
    private bool _disposed;
    private CancellationTokenSource? _matchmakingToken = new();
    private ITransportLayer _transportLayer;
    private readonly string _appId = Config.Core!.AppId;
    private NetSession _session = NetSession.Default;
    private readonly List<NetPeer> _failedPeerConnections = new();
    private bool _serverRunning;
    private bool _sessionCreator;
    private TaskCompletionSource<LobbyInfo> _lobbyToken = new();
    private TaskCompletionSource<string[]> _lobbyListToken = new();
    private TaskCompletionSource<bool> _basicBoolToken = new();
    private ReadOnlyMemory<byte> _encodedRelayAddr = ReadOnlyMemory<byte>.Empty;
    private ReadOnlyMemory<byte> _heartbeatMsg;
    private readonly ClientListCollector _clientListCollector = new();

    #region CONST_HEADERS
    // ---------- CONSTS ----------
    // Server Headers
    
    // ReSharper disable InconsistentNaming
    private static ReadOnlySpan<byte> REGISTER_SESSION_ACK => "rsa;"u8;
    private static ReadOnlySpan<byte> SELF_JOIN_SESSION_ACK => "cca;"u8;
    private static ReadOnlySpan<byte> PEER_JOIN_SESSION_ACK => "cc;"u8;
    private static ReadOnlySpan<byte> LIST_SESSIONS_ACK => "lsa;"u8;
    private static ReadOnlySpan<byte> SESSION_STATE_ACK => "ssa;"u8;
    private static ReadOnlySpan<byte> UPDATE_CLIENT_NUM_ACK => "cna;"u8;
    private static ReadOnlySpan<byte> SET_RELAY_ACK => "sra;"u8;
    private static ReadOnlySpan<byte> SET_HOST_ACK => "sha;"u8;
    private static ReadOnlySpan<byte> RELAY_CHANGED_ACK => "rca;"u8;
    private static ReadOnlySpan<byte> SESSION_START => "sta;"u8;
    
    // Server Fail Headers
    private static ReadOnlySpan<byte> SESSIONS_STATE_FAIL => "ssf;"u8;
    private static ReadOnlySpan<byte> SET_RELAY_FAIL => "srf;"u8;
    private static ReadOnlySpan<byte> UPDATE_CLIENT_NUM_FAIL => "cnf;"u8;
    
    // Server Notifications
    private static ReadOnlySpan<byte> NOTIFY_SESSION_READY => "nsr;"u8;
    private static ReadOnlySpan<byte> NOTIFY_SESSION_READY_RELAY => "nsrr;"u8;
    private static ReadOnlySpan<byte> CLIENT_LIST_PART => "clp;"u8;
    private static ReadOnlySpan<byte> CLIENT_LIST_FIN => "cla;"u8;
    
    // Client Communication Headers
    private static string CLIENT_READY => "ready;";
    private static string CANCEL_CLIENT_READY => "unready;";
    // ReSharper restore InconsistentNaming
    
    #endregion //CONST_HEADERS
    
    private TransportHandle _serverHandle = new(Guid.NewGuid(), TransportType.CustomUdp,
        TransportHandleEncoder.EncodeCustomUdp(IPEndPoint.Parse(Config.Net!.SignalingServerAddr)), TransportFlags.ServerContact, ReadOnlyMemory<byte>.Empty);

    private TransportHandle _relayHandle;

    private MatchmakingSettings _settings;

    public CustomMatchmaker(MatchmakingSettings settings)
    {
        _transportLayer = settings.TransportLayer;
        _settings = settings;
        _transportLayer.SetHandler(OnMessageReceived);
    }

    // ---------- EVENTS ----------
    public event Action<LobbyInfo>? LobbyUpdate;
    public event Action<NetSession>? SessionStarted;
    public event Action<NetworkType>? NetworkToUse;

    public void UpdateProfileInfo(UserProfileDto profile)
    {
        _settings.ProfileInfo = profile;
        }

    // ReSharper disable RedundantTypeArgumentsOfMethod
    public async Task<LobbyInfo> CreateLobby(string id, int numPlayers, bool isPublic)
    {
        ResetCompletionToken<LobbyInfo>(ref _lobbyToken);
        if (!await TryCreateSession(id)) return LobbyInfo.Failed;
        
        // rs;{session_id};{app_id};{platform};{max_clients};{public};{hard_nat};{display_name}
        await _transportLayer.SendMessageImmediate(
            Encoding.UTF8.GetBytes($"rs;{id};{_appId};{(int)NetworkType.CustomUdp};{numPlayers};{isPublic};{_settings.SymNat};{_settings.ProfileInfo.DisplayName}"),
            _serverHandle);
        
        _sessionCreator = true;
        return await _lobbyToken.Task;
    }

    public async Task<LobbyInfo> UpdateLobby(int numPlayers)
    {
        ResetCompletionToken<LobbyInfo>(ref _lobbyToken);
        _transportLayer.SetHandler(OnMessageReceived);
        // cn;{session_id};{app_id};{number_of_clients}
        await _transportLayer.SendMessageImmediate(Encoding.UTF8.GetBytes($"cn;{_session.Name};{_appId};{numPlayers}"), _serverHandle);
        
        return await _lobbyToken.Task;
    }

    public async Task<LobbyInfo> JoinLobby(string id)
    {
        ResetCompletionToken<LobbyInfo>(ref _lobbyToken);
        if (!await TryCreateSession(id)) return LobbyInfo.Failed;
        
        // cc;{session_id};{app_id};{platform};{hard_nat};{display_name}
        await _transportLayer.SendMessageImmediate(
            Encoding.UTF8.GetBytes($"cc;{id};{_appId};{(int)NetworkType.CustomUdp};{_settings.SymNat};{_settings.ProfileInfo.DisplayName}"),
            _serverHandle);
        
        _sessionCreator = false;
        return await _lobbyToken.Task;
    }

    public void StartOrReady()
    {
        _ = MessageAllPeers(Encoding.UTF8.GetBytes(CLIENT_READY));
        _session.SelfReady = true;
        _ = StartIfReady();
    }

    public void CancelStartOrReady()
    {
        _ = MessageAllPeers(Encoding.UTF8.GetBytes(CANCEL_CLIENT_READY));
        _session.SelfReady = false;
    }

    public void LeaveLobby()
    {
        if (_session.IsHost && _serverRunning)
        {
            ShutdownServer();
        }
        _session = NetSession.Default;
        _transportLayer.Disconnect(_serverHandle);
        CancelSession();
    }

    private async Task<bool> SetHostAddress()
    {
        ResetCompletionToken<bool>(ref _basicBoolToken);
        if (!_session.IsHost) { return false; }
        
        string oldHost = _session.ServerEndpoint != null ? _session.ServerEndpoint.ToString() : string.Empty;

        
        // TODO BEGIN | Replace the following with actual implementation once server exists:
        if (TransportHandleEncoder.TryDecode(Guid.Empty, _encodedRelayAddr, out var decodedEndpoint))
        {
            _session.ServerEndpoint = decodedEndpoint.Udp.ToIPEndPoint();
        }
        else
        {
            _session.ServerEndpoint = new IPEndPoint(IPAddress.Loopback, 9990);
        }
        // TODO END
        
        // sh;{session_id};{old_host_addr};{app_id};{host_addr}
        await _transportLayer.SendMessageImmediate(
            Encoding.UTF8.GetBytes($"sh;{_session.Name};{oldHost};{_appId};{_session.ServerEndpoint}"),
            _serverHandle);

        return await _basicBoolToken.Task;
    }

    public async Task<string[]> GetLobbyList()
    {
        ResetCompletionToken<string[]>(ref _lobbyListToken);
        _transportLayer.SetHandler(OnMessageReceived);
        
        var resHandle = await _transportLayer.Connect(_serverHandle);
        if ((resHandle.Flags & TransportFlags.ConnectionFailed) == TransportFlags.ConnectionFailed)
        {
            return [];
        }
        _serverHandle = resHandle;
        // ls;{app_id}
        await _transportLayer.SendMessageImmediate(Encoding.UTF8.GetBytes($"ls;{_appId}"), _serverHandle);

        return await _lobbyListToken.Task;
    }

    private async Task RequestSessionState()
    {
        // ss;
        // {session_id(str)};{app_id(str)}
        await _transportLayer.SendMessageImmediate(Encoding.UTF8.GetBytes($"ss;{_session.Name};{_appId}"), _serverHandle);
    }
    // ReSharper restore RedundantTypeArgumentsOfMethod

    public void SetTransportLayer(ITransportLayer transportLayer)
    {
        _transportLayer = transportLayer;
        _transportLayer.SetHandler(OnMessageReceived);
    }

    private void OnMessageReceived(TransportHandle handle, ReadOnlyMemory<byte> data)
    {
        _ = (handle.Flags & TransportFlags.ServerContact) == TransportFlags.ServerContact 
            ? HandleServerMessage(data) 
            : HandleClientMessage(handle, data);
    }

    private async Task HandleServerMessage(ReadOnlyMemory<byte> data)
    {
        bool shouldRetryFailed = false;
        
        try
        {
            ReadOnlyMemory<byte> header = ExtractServerHeader(data);
            if (header.IsEmpty)
            {
                return;
            }
            
            // -------- SUCCESS -------- 
            if (header.Span.SequenceEqual(REGISTER_SESSION_ACK))
            {
                await SessionCreated(data[REGISTER_SESSION_ACK.Length..]);
            }
            if (header.Span.SequenceEqual(SELF_JOIN_SESSION_ACK))
            {
                await SelfJoinedSession(data[SELF_JOIN_SESSION_ACK.Length..]);
            }
            if (header.Span.SequenceEqual(PEER_JOIN_SESSION_ACK))
            {
                PeerJoinedSession(data[PEER_JOIN_SESSION_ACK.Length..]);
                shouldRetryFailed = true;
            }
            if (header.Span.SequenceEqual(UPDATE_CLIENT_NUM_ACK))
            {
                ClientNumberUpdated(data[UPDATE_CLIENT_NUM_ACK.Length..]);
                shouldRetryFailed = true;
            }
            if (header.Span.SequenceEqual(LIST_SESSIONS_ACK))
            {
                SessionList(data[LIST_SESSIONS_ACK.Length..]);
            }
            if (header.Span.SequenceEqual(SESSION_STATE_ACK))
            {
                await SessionState(data[SESSION_STATE_ACK.Length..]);
                shouldRetryFailed = true;
            }
            if (header.Span.SequenceEqual(SET_RELAY_ACK))
            {
                RelaySet(data[SET_RELAY_ACK.Length..]);
            }
            if (header.Span.SequenceEqual(SET_HOST_ACK))
            {
                HostSet(data[SET_HOST_ACK.Length..]);
            }
            if (header.Span.SequenceEqual(RELAY_CHANGED_ACK))
            {
                RelayChanged(data[RELAY_CHANGED_ACK.Length..]);
                shouldRetryFailed = true;
            }
            if (header.Span.SequenceEqual(SESSION_START))
            {
                StartSession(data[SESSION_START.Length..]);
                shouldRetryFailed = true;
            }
            
            // -------- FAIL --------
            if (header.Span.SequenceEqual(SESSIONS_STATE_FAIL))
            {
                // TODO handle session state failure
                shouldRetryFailed = true;
            }
            if (header.Span.SequenceEqual(SET_RELAY_FAIL))
            {
                // TODO handle relay setting failure
                shouldRetryFailed = true;
            }
            if (header.Span.SequenceEqual(UPDATE_CLIENT_NUM_FAIL))
            {
                // TODO handle update client number failure
                shouldRetryFailed = true;
            }
            
            // -------- NOTIFICATION --------
            if (header.Span.SequenceEqual(NOTIFY_SESSION_READY_RELAY) || header.Span.SequenceEqual(NOTIFY_SESSION_READY))
            {
                await SessionReady(data);
                shouldRetryFailed = true;
            }
            if (header.Span.SequenceEqual(CLIENT_LIST_PART))
            {
                ClientPartReceived(data[CLIENT_LIST_PART.Length..]);
            }
            if (header.Span.SequenceEqual(CLIENT_LIST_FIN))
            {
                ClientPartFinished(data[CLIENT_LIST_FIN.Length..]);
            }
        }
        catch (Exception e)
        {
            throw;
        }
        finally
        {
            // Finally retry connecting to failed clients
            if (shouldRetryFailed)
            {
                RetryFailedClientConnections();
            }
        }
    }
    
    private ReadOnlyMemory<byte> ExtractServerHeader(ReadOnlyMemory<byte> data)
    {
        var span = data.Span;
        int index = span.IndexOf((byte)';');

        return index >= 0 ? data[..(index+1)] : ReadOnlyMemory<byte>.Empty;
    }

    private async Task HandleClientMessage(TransportHandle handle, ReadOnlyMemory<byte> data)
    {
        // Check if client is part of session/matchmaking
        // All clients should be known and connected before messages are accepted from it
        NetPeer? peer = _session.GetPeer(handle.PeerId);
        if (peer == null)
        {
            return;
        }
        
        // Change peer readiness
        if (data.Span[..CLIENT_READY.Length].SequenceEqual(Encoding.UTF8.GetBytes(CLIENT_READY)))
        {
            peer.Ready = true;
            await StartIfReady();
        }
        if (data.Span[..CANCEL_CLIENT_READY.Length].SequenceEqual(Encoding.UTF8.GetBytes(CANCEL_CLIENT_READY)))
        {
            peer.Ready = false;
        }
    }
    
    private async Task SessionCreated(ReadOnlyMemory<byte> data)
    {
        // {session_id(str)};{platform(u32)};{creator_addr(cli)};{relay_addr(ipp)}
        string sessionData = Encoding.UTF8.GetString(data.Span);
        string[] parts = sessionData.Split(';');
        
        // Validate everything correct
        if (parts.Length != 4)
        {
            return;
        }
        if (parts[0] != _session.Name)
        {
            _lobbyToken.TrySetResult(LobbyInfo.Failed);
            return;
        }

        _session.IsHost = !string.IsNullOrWhiteSpace(parts[3]);
        if (_session.IsHost)
        {
            _encodedRelayAddr = TransportHandleEncoder.EncodeCustomUdp(IPEndPoint.Parse(parts[3]));
        }
        
        // Set platform and emit success signal
        _session.Platform = (SessionPlatform)Enum.Parse(typeof(SessionPlatform), parts[1]);
        bool result = await InitIfHost(); // TODO use result for something?
        _lobbyToken.TrySetResult(new LobbyInfo(_session.Name, [], true));
    }

    private async Task SelfJoinedSession(ReadOnlyMemory<byte> data)
    {
        // cca; 
        // {client_list_parts_integer};{client_list_id};{platform};{relay_addr};{a_client_is_relay}
        
        // Extract data
        string sessionData = Encoding.UTF8.GetString(data.Span);
        string[] parts = sessionData.Split(';');
        if (parts.Length != 5) return;
        
        // Handle main sections, session wide info
        // string[] clients = mainParts[0].Split(',');
        uint expectedClientCount = uint.Parse(parts[0]);
        uint clientListId = uint.Parse(parts[1]);
        _session.Platform = (SessionPlatform)Enum.Parse(typeof(SessionPlatform), parts[2]);
        string relayStr = parts[3];
        bool clientAsRelay = parts[4].Equals("1") || parts[4].Equals("true", StringComparison.OrdinalIgnoreCase);
        
        _clientListCollector.NewPendingList(TimeSpan.FromSeconds(5), clientListId, expectedClientCount, 0);
        List<PendingClientInfo> clients = await GetClientInfos(clientListId);
        
        // Relay info
        if (!string.IsNullOrWhiteSpace(relayStr))
        {
            // Check if another client (not your own)
            IPEndPoint relayEndpoint = IPEndPoint.Parse(relayStr);
            bool found = clients.Any(c => c.EndPoint.Equals(relayEndpoint));
        
            // Assign self as host depending on relay info. If no other peers are host but a client is host, I must be host
            _session.IsHost = found switch
            {
                true when clientAsRelay => false,
                false when clientAsRelay => true,
                _ => _session.IsHost
            };
            _encodedRelayAddr = TransportHandleEncoder.EncodeCustomUdp(relayEndpoint);
        }
        
        // Handle each client in the list
        foreach (PendingClientInfo client in clients)
        {
            ConnectClientFromPendingData(client);
        }
        
        bool result = await InitIfHost();
        _lobbyToken.TrySetResult(new LobbyInfo(_session.Name, _session.GetAllPeerDtos(), true));
    }

    private void PeerJoinedSession(ReadOnlyMemory<byte> data)
    {
        // cc;
        // {{client_addr}:{hard_nat}:{display_name}};{platform};{relay_addr};{a_client_is_relay}
        string dataString = Encoding.UTF8.GetString(data.Span);
        string[] parts = dataString.Split(';');
        if (parts.Length != 4) return;
        
        // Session wide info
        _session.Platform = (SessionPlatform)Enum.Parse(typeof(SessionPlatform), parts[1]);
        bool clientAsRelay = parts[3].Equals("1") || parts[3].Equals("true", StringComparison.OrdinalIgnoreCase);
        if (!string.IsNullOrWhiteSpace(parts[2]))
        {
            // Relay stuff
            ReadOnlyMemory<byte> relay = TransportHandleEncoder.EncodeCustomUdp(IPEndPoint.Parse(parts[2]));
            if (!_encodedRelayAddr.Span.SequenceEqual(relay.Span))
            {
                // If I am host and a new client is chosen as relay, I am no longer host
                if (_session.IsHost && clientAsRelay)
                {
                    _session.IsHost = false;
                }
                
                _encodedRelayAddr = _encodedRelayAddr.Span.SequenceEqual(relay.Span) ? relay : _encodedRelayAddr;
            }
        }
        
        // Client connection
        ConnectClientFromServerData(Encoding.UTF8.GetBytes(parts[0]));
        LobbyUpdate?.Invoke(new LobbyInfo(_session.Name, _session.GetAllPeerDtos(), true));
    }

    private void SessionList(ReadOnlyMemory<byte> data)
    {
        // lsa;
        // {session_id_list}
        string dataString = Encoding.UTF8.GetString(data.Span);
        string[] parts = dataString.Split(',');
        _lobbyListToken.TrySetResult(parts);
    }

    private async Task SessionState(ReadOnlyMemory<byte> data)
    {
        // ssa;
        // {session_platform};{clients_list_size};{client_list_id};{relay_address};{a_client_is_relay};{session_full}
        var dataString = Encoding.UTF8.GetString(data.Span);
        string[] parts = dataString.Split(';');
        if (parts.Length != 6) { return; }
        _session.Platform = (SessionPlatform)Enum.Parse(typeof(SessionPlatform), parts[0]);
        uint expectedClientCount = uint.Parse(parts[1]);
        uint clientListId = uint.Parse(parts[2]);
        if (!string.IsNullOrWhiteSpace(parts[3]))
        {
            _encodedRelayAddr = TransportHandleEncoder.EncodeCustomUdp(IPEndPoint.Parse(parts[2]));
        }
        // Unused variables so far
        bool clientAsRelay = parts[4].Equals("true", StringComparison.OrdinalIgnoreCase) || parts[4].Equals("1");
        bool sessionFull = parts[5].Equals("true", StringComparison.OrdinalIgnoreCase) || parts[5].Equals("1");

        // If session is full the ready signal will handle the client list
        if (sessionFull) { return; }
        
        _clientListCollector.NewPendingList(TimeSpan.FromSeconds(5), clientListId, expectedClientCount, 0);
        List<PendingClientInfo> clients = await GetClientInfos(clientListId);
        // Check if we are missing a connection
        CheckClientConnectionsByEndpoint(clients);
        LobbyUpdate?.Invoke(new  LobbyInfo(_session.Name, _session.GetAllPeerDtos(), true));
    }

    private void RelaySet(ReadOnlyMemory<byte> data)
    {
        // sra;
        // {session_id};{relay_addr}
        var dataString = Encoding.UTF8.GetString(data.Span);
        string[] parts = dataString.Split(';');
        if (parts.Length != 2 || !_session.Name.Equals(parts[0]))
        {
            return;
        }
        _encodedRelayAddr = TransportHandleEncoder.EncodeCustomUdp(IPEndPoint.Parse(parts[1]));
    }

    private void HostSet(ReadOnlyMemory<byte> data)
    {
        // sha;
        // {session_id};{host_addr}
        var dataString = Encoding.UTF8.GetString(data.Span);
        string[] parts = dataString.Split(';');
        if (parts.Length != 2 || !_session.Name.Equals(parts[0]))
        {
            return;
        }
        _session.ServerEndpoint = IPEndPoint.Parse(parts[1]);
        _basicBoolToken.TrySetResult(true);
    }

    private void RelayChanged(ReadOnlyMemory<byte> data)
    {
        // rca;
        // {session_id};{relay_addr}
        // Extract data
        var dataString = Encoding.UTF8.GetString(data.Span);
        string[] parts = dataString.Split(';');
        if (parts.Length != 2 || !_session.Name.Equals(parts[0]))
        {
            return;
        }
        ReadOnlyMemory<byte> relay = TransportHandleEncoder.EncodeCustomUdp(IPEndPoint.Parse(parts[1]));
        
        // Update if not same
        if (relay.Span.SequenceEqual(_encodedRelayAddr.Span)) return;
        if (_session.IsHost)
        {
            _session.IsHost = false;
        }
        _encodedRelayAddr = relay;
    }

    private void ClientNumberUpdated(ReadOnlyMemory<byte> data)
    {
        // cna;
        // {number_of_clients}
    }

    private void ClientPartReceived(ReadOnlyMemory<byte> data)
    {
        // clp;
        // {list_id};{part_number};{client_info}
        // client_info = ConnectClientFromServerData format
        
        var dataString = Encoding.UTF8.GetString(data.Span);
        string[] parts = dataString.Split(';');
        if (parts.Length != 3) { return; }
        
        uint id = uint.Parse(parts[0]);
        int partNumber = int.Parse(parts[1]); //Unused
        string clientInfo = parts[2];
        
        string[] clientParts = clientInfo.Split(':');
        if (clientParts.Length != 4) { return; }
        // IP:Port:{symmetricInt}:{DisplayName}
        PendingClientInfo info = new PendingClientInfo
        {
            DisplayName = clientParts[3],
            EndPoint = IPEndPoint.Parse($"{clientParts[0]}:{clientParts[1]}"),
            IsSymmetricNat = clientParts[2].Equals("1") ||
                             clientParts[2].Equals("true", StringComparison.OrdinalIgnoreCase),
        };
        
        _clientListCollector.AddClient(info, id);
    }

    private void ClientPartFinished(ReadOnlyMemory<byte> data)
    {
        // cla;
        // complete;{list_id};{number_of_parts}
        string dataStr = Encoding.UTF8.GetString(data.Span);
        string[] parts = dataStr.Split(';');
        if (parts.Length != 3) { return; }
        uint id = uint.Parse(parts[1]);
        uint expectedCount = uint.Parse(parts[2]);
        _clientListCollector.ConfirmPacketReceived(id, expectedCount);
    }

    private async Task<List<PendingClientInfo>> GetClientInfos(uint listId)
    {
        while (_clientListCollector.IsDone(listId) == ClientListResult.Pending)
        {
            await Task.Delay(50);
        }
        
        return _clientListCollector.GetPendingClientList(listId);
    }

    private void ConnectClientFromServerData(ReadOnlyMemory<byte> data)
    {
        // ip:port:hard_nat_integer:display_name
        // Extract strings from data
        string clientData = Encoding.UTF8.GetString(data.Span);
        string[] parts = clientData.Split(':');
        if (parts.Length != 4)
        {
            return;
        }

        PendingClientInfo clientInfo = new PendingClientInfo
        {
            DisplayName = parts[3],
            EndPoint = IPEndPoint.Parse($"{parts[0]}:{parts[1]}"),
            IsSymmetricNat = parts[2].Equals("1") || parts[2].Equals("true", StringComparison.OrdinalIgnoreCase),
        };
        
        ConnectClientFromPendingData(clientInfo);
    }

    private void ConnectClientFromPendingData(PendingClientInfo clientInfo)
    {
        // Create peer object
        NetPeer peer = new NetPeer(Guid.NewGuid())
        {
            EndPoint = clientInfo.EndPoint,
            SymmetricNat = clientInfo.IsSymmetricNat,
            PeerName = clientInfo.DisplayName,
        };
        
        // Figure out if we are dealing with symmetric NAT or not
        TransportFlags transportFlags = TransportFlags.None;
        if (peer.SymmetricNat)
        {
            transportFlags |= TransportFlags.PeerSymmetricNat;
        }
        if (_settings.SymNat)
        {
            transportFlags |= TransportFlags.SelfSymmetricNat;
        }

        // Relay logic
        if (!_encodedRelayAddr.IsEmpty)
        {
            peer.AdditionalInfo = _encodedRelayAddr;
            transportFlags |= TransportFlags.HasRelay;
        }
        
        // Build transport handle and peer info
        peer.TransportFlags = transportFlags;
        TransportHandle handle = peer.GetHandle(TransportType.CustomUdp);
        _session.AddPeer(peer);

        _ = Task.Run(async () =>
        {
            // Connect and check result
            TransportHandle resultHandle = await _transportLayer.Connect(handle);
            if ((resultHandle.Flags & TransportFlags.ConnectionFailed) == TransportFlags.ConnectionFailed)
            {
                _failedPeerConnections.Add(peer);
            }
        });
    }

    private void RetryFailedClientConnections()
    {
        try
        {
            foreach (var peer in _failedPeerConnections)
            {
                // Ensure that relay details are included
                if (!_encodedRelayAddr.IsEmpty)
                {
                    peer.AdditionalInfo = _encodedRelayAddr;
                    peer.TransportFlags |= TransportFlags.HasRelay;
                }
            
                // Run connection task
                _ = Task.Run(async () =>
                {
                    TransportHandle res = await _transportLayer.Connect(peer.GetHandle(TransportType.CustomUdp));
                    if ((res.Flags & TransportFlags.ConnectionFailed) == TransportFlags.ConnectionFailed)
                    {
                        return;
                    }
                
                    _failedPeerConnections.Remove(peer);
                    // await _transportLayer.SendMessageImmediate(Encoding.UTF8.GetBytes($"{PEER_NAME}{settings.ProfileInfo.DisplayName}"),
                    //     peer.GetHandle(TransportType.CustomUdp));
                });
            }
        }
        catch (Exception e)
        {
            throw;
        }
    }

    private async Task SessionReady(ReadOnlyMemory<byte> data)
    {
        if (data.Span[NOTIFY_SESSION_READY.Length] == (byte)';')
        {
            await SessionReadyClient(data[NOTIFY_SESSION_READY.Length..]);
        }
        else
        {
            await SessionReadyRelay(data[NOTIFY_SESSION_READY_RELAY.Length..]);
        }
    }

    private async Task SessionReadyClient(ReadOnlyMemory<byte> data)
    {
        // Notify Session Ready:
        // nsr;
        // {platform(u32)};{client_list_size(u32)};{client_list_id(u32)};{relay_addr(ipp)};{host_addr(ipp)};{session_id(str)}
        // Extract data
        string dataString = Encoding.UTF8.GetString(data.Span);
        string[] parts = dataString.Split(';');
        if (parts.Length != 6) { return; }
        string sessionId = parts[5];
        if (!_session.Name.Equals(sessionId, StringComparison.OrdinalIgnoreCase)) { return; }
        _session.Platform = (SessionPlatform)Enum.Parse(typeof(SessionPlatform), parts[0]);
        uint size = uint.Parse(parts[1]);
        uint listId = uint.Parse(parts[2]);
        string relayStr = parts[3];
        string serverStr = parts[4];
        
        // Relay
        if (!string.IsNullOrWhiteSpace(relayStr))
        {
            IPEndPoint relayAddr = IPEndPoint.Parse(relayStr);
            _encodedRelayAddr = TransportHandleEncoder.EncodeCustomUdp(relayAddr);
        }
        else if (!_encodedRelayAddr.IsEmpty)
        {
            _encodedRelayAddr = ReadOnlyMemory<byte>.Empty;
        }

        if (!string.IsNullOrWhiteSpace(serverStr))
        {
            IPEndPoint hostAddr = IPEndPoint.Parse(serverStr);
            _session.ServerEndpoint = hostAddr;
        }
        else
        {
            await SessionReadyNoHost();
            return;
        }
        
        _clientListCollector.NewPendingList(TimeSpan.FromSeconds(5), listId, size, 0);
        List<PendingClientInfo> clients = await GetClientInfos(listId);
        // Ensure all clients are connected
        CheckClientConnectionsByEndpoint(clients);
    }

    private async Task SessionReadyRelay(ReadOnlyMemory<byte> data)
    {
        // Extract data parts
        // nsrr;
        // {hmac};{app_id(str)};{platform(u32)};{client_list_size(u32)};{client_list_id(u32)};{relay_addr(ipp)};{host_addr(ipp)};{session_id(str)};{expiration_timestamp(i64)};{nonce}
        string dataString = Encoding.UTF8.GetString(data.Span);
        string[] parts = dataString.Split(';');
        if (parts.Length != 10) return;
        string sessionId = parts[7];
        if (!_session.Name.Equals(sessionId, StringComparison.OrdinalIgnoreCase)) { return; }
        string hmac = parts[0];
        _session.Platform = (SessionPlatform)Enum.Parse(typeof(SessionPlatform), parts[2]);
        uint listSize = uint.Parse(parts[3]);
        uint listId = uint.Parse(parts[4]);
        string relayStr = parts[5];
        string hostStr = parts[6];
        DateTime expTimestamp = DateTime.Parse(parts[8]);
        string nonce = parts[9];

        if (!string.IsNullOrWhiteSpace(relayStr))
        {
            IPEndPoint relayAddr = IPEndPoint.Parse(relayStr);
            _encodedRelayAddr = TransportHandleEncoder.EncodeCustomUdp(relayAddr);
        }

        if (!string.IsNullOrWhiteSpace(hostStr))
        {
            IPEndPoint hostAddr = IPEndPoint.Parse(hostStr);
            _session.ServerEndpoint = hostAddr;
        }
        else 
        {
            // If no host is set, run host selection and initialize server, then wait for new ready message
            await SessionReadyNoHost();
            return;
        }
        
        _clientListCollector.NewPendingList(TimeSpan.FromSeconds(5), listId, listSize, 0);
        List<PendingClientInfo> clients = await GetClientInfos(listId);
        
        CheckClientConnectionsByEndpoint(clients);

        const TransportFlags transportFlags = TransportFlags.ServerContact;
        var handle = new TransportHandle(Guid.NewGuid(), TransportType.CustomUdp, _encodedRelayAddr, transportFlags, ReadOnlyMemory<byte>.Empty);
        _relayHandle = await _transportLayer.Connect(handle);
        
        // TODO Fix above, and run relay registration
        var msg = Encoding.UTF8.GetBytes($"cr;{dataString}");
        _ = _transportLayer.SendMessageImmediate(msg, _relayHandle);
    }

    private async Task SessionReadyNoHost()
    {
        // Run host selection, creator set to host, init if host
        uint waitTime = 5;
        if (_sessionCreator)
        {
            _session.IsHost = true;
            bool res = await InitIfHost();
            waitTime = 1;
        }
        
        // Run background task to re-request start signal, to get the updated stuff
        _ = Task.Run(async () =>
        {
            Task.Delay(TimeSpan.FromSeconds(waitTime)).Wait();
            await RequestSessionState();
        });
    }

    private void StartSession(ReadOnlyMemory<byte> data)
    {
        // sta;
        // {server_addr}
        string serverAddr = Encoding.UTF8.GetString(data.Span);
        IPEndPoint serverEndPoint = IPEndPoint.Parse(serverAddr);
        if (_session.ServerEndpoint == null || _session.ServerEndpoint?.Equals(serverEndPoint) == false)
        {
            _session.ServerEndpoint = serverEndPoint;
        }
        _session.ServerEndpoint = IPEndPoint.Parse(serverAddr);
        
        // TODO connect to server
        
        SessionStarted?.Invoke(_session);
        
        _session = NetSession.Default; // Don't need to remove the session reference, but no reason to keep it around either
        _clientListCollector.Cleanup();
    }

    // Check that the clients on the list of endpoints are connected. Connect to missing, disconnect extras
    private void CheckClientConnectionsByEndpoint(List<PendingClientInfo> clients)
    {
        var peers = _session.GetAllPeers().ToDictionary(peer => peer.EndPoint!, peer => peer);
        
        var desired = new HashSet<IPEndPoint>(clients.ToArray().Select(c => c.EndPoint));
        var existing = peers.Keys.ToHashSet();
        
        var toAdd = desired.Except(existing).ToArray();
        var toRemove = existing.Except(desired).ToArray();

        // Disconnect from clients that are no longer part of session
        if (toRemove.Length != 0)
        {
            foreach (var peerToRemove in toRemove)
            {
                _session.RemovePeer(peers[peerToRemove]);
                _transportLayer.Disconnect(peers[peerToRemove].GetHandle(TransportType.CustomUdp));
            }
        }

        foreach (var client in clients.Where(client => toAdd.Contains(client.EndPoint)))
        {
            ConnectClientFromPendingData(client);
        }
    }

    private async Task StartIfReady()
    {
        if (_session is { Ready: false, IsHost: false }) return;
        
        // Initialize the server if not already initialized
        bool result = await InitIfHost();

        if (_session.IsHost)
        {
            _ = _transportLayer.SendMessageImmediate(
                Encoding.UTF8.GetBytes($"st;{_session.Name};{_appId};{_session.ServerEndpoint}"), _serverHandle);
        }
    }

    private async Task<bool> InitIfHost()
    {
        if (!_session.IsHost) return false;
        if (_serverRunning /* && _server.Address.Equals(_session.ServerEndpoint)*/) // TODO replace bool with an actual server reference?
        {
            return false;
        }
        
        // TODO INIT SERVER
        bool result = await SetHostAddress();
        if (!result)
        {
            // Log error
            return result;
        }

        _serverRunning = true;
        return result;
    }

    private void ConnectToServer()
    {
        // TODO connect to the server
        // _session.ServerEndpoint
    }

    private void ShutdownServer()
    {
        _serverRunning = false;
    }
    
    private async Task MessageAllPeers(ReadOnlyMemory<byte> data)
    {
        foreach (var peer in _session.GetAllPeers())
        {
            await _transportLayer.SendMessageImmediate(data, peer.GetHandle(TransportType.CustomUdp));
        }
    }

    private void SetHeartbeat()
    {
        _heartbeatMsg = Encoding.UTF8.GetBytes($"hb;{_session.Name}");
        _transportLayer.HeartbeatMsg = _heartbeatMsg;
        _transportLayer.HeartbeatResp = Encoding.UTF8.GetBytes($"hba;{_session.Name}");
    }

    private void ResetCompletionToken<T>(ref TaskCompletionSource<T> token)
    {
        token = new TaskCompletionSource<T>();
    }

    private async Task<bool> TryCreateSession(string id)
    {
        ResetCompletionToken<LobbyInfo>(ref _lobbyToken);
        _transportLayer.SetHandler(OnMessageReceived);
        _session = new NetSession(id);
        var resHandle = await _transportLayer.Connect(_serverHandle);
        if ((resHandle.Flags & TransportFlags.ConnectionFailed) == TransportFlags.ConnectionFailed)
        {
            return false;
        }
        _serverHandle = resHandle;
        SetHeartbeat();
        StartMatchmakingKeepalivePings();
        return true;
    }

    private void CancelSession()
    {
        _matchmakingToken?.Cancel();
        _matchmakingToken?.Dispose();
        _matchmakingToken = null;
    }
    
    private void StartMatchmakingKeepalivePings()
    {
        _matchmakingToken = new();
        _ = Task.Run(() => MatchmakingKeepalive(_matchmakingToken!.Token), _matchmakingToken!.Token);
    }

    private async Task MatchmakingKeepalive(CancellationToken token)
    {
        try
        {
            using PeriodicTimer timer = new PeriodicTimer(TimeSpan.FromSeconds(5));
            
            while (await timer.WaitForNextTickAsync(token))
            {
                _ = _transportLayer.SendMessageImmediate(_heartbeatMsg, _serverHandle);
                foreach (var peer in _session.GetAllPeers()
                             .Where(p => (p.GetHandle(TransportType.CustomUdp).Flags & TransportFlags.ConnectionFailed) == 0))
                {
                    _ = _transportLayer.SendMessageImmediate(_heartbeatMsg,
                        peer.GetHandle(TransportType.CustomUdp));
                }
            }
        }
        catch (OperationCanceledException)
        {
            // Cancelation expected
        }
        catch (Exception e)
        {
            // Log exception
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        
        CancelSession();
        GC.SuppressFinalize(this);
        
        _disposed = true;
    }
}
