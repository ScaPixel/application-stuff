using System.Net;
using System.Text;
using NetworkingDemo.Shared.Configuration;
using NetworkingDemo.Shared.Logging;
// ReSharper disable StringLiteralTypo

namespace NetworkingDemo.Networking.Core;

public class CustomUdpHolepuncher
{
    private readonly Dictionary<int, UdpSocket> _sockets = new();
    // ReSharper disable InconsistentNaming
    private static ReadOnlySpan<byte> PEER_GREET => "greet"u8;
    private static ReadOnlySpan<byte> PEER_CONFIRM => "confirm"u8;
    private static ReadOnlySpan<byte> PEER_ACTIVE => "active"u8;
    // ReSharper restore InconsistentNaming

    private readonly TaskCompletionSource<CustomUdpPeerConnection> _taskToken = new();
    private readonly CancellationTokenSource _cancelToken = new();
    
    private TransportHandle _handle;
    private DecodedEndpoint _decodedEndpoint;
    private CustomUdpConnectionType _type = CustomUdpConnectionType.Simple;
    private CustomUdpPeerConnection? _connectionObj;
    private IPAddress? _ipAddress;
    private int _greetedCount;
    private int _confirmedCount;
    private int _activeCount;
    private const int MAX_SAME_PACKETS = 3;
    private readonly int _defaultPort;

    public async Task<CustomUdpPeerConnection> Connect()
    {
        // Decode the handler into endpoint and ip address
        if (!TransportHandleEncoder.TryDecode(_handle.PeerId, _handle.Payload,
                out DecodedEndpoint decodedEndpoint))
        {
            return CustomUdpPeerConnection.Failed(_handle.PeerId, _decodedEndpoint.Udp.ToIPEndPoint(), _sockets[_defaultPort]);
        }
        
        _decodedEndpoint = decodedEndpoint;
        IPEndPoint endPoint = decodedEndpoint.Udp.ToIPEndPoint();
        _ipAddress = endPoint.Address;
        
        // Connect depending on type of connection
        if ((_handle.Flags & TransportFlags.SelfSymmetricNat) == TransportFlags.SelfSymmetricNat
            && (_handle.Flags & TransportFlags.PeerSymmetricNat) == TransportFlags.PeerSymmetricNat)
        {
            _type = CustomUdpConnectionType.Symmetric;
            CreateUdpSockets(Config.Net!.SymmetricPunchtroughSocketsLimit);
            return await ConnectSymmetricPeer();
        }
        
        if ((_handle.Flags & TransportFlags.PeerSymmetricNat) == TransportFlags.PeerSymmetricNat)
        {
            _type = CustomUdpConnectionType.SimpleSymmetric;
            return await ConnectSymmetricPeer();
        }
        
        if ((_handle.Flags & TransportFlags.SelfSymmetricNat) == TransportFlags.SelfSymmetricNat)
        {
            _type = CustomUdpConnectionType.SymmetricSimple;
            CreateUdpSockets(Math.Clamp(Config.Net!.SymmetricPunchtroughSocketsLimit / 100, 4, 25));
            return await ConnectSymmetricPeer();
        }
        
        return await ConnectSimplePeer();
    }

    private async Task<CustomUdpPeerConnection> ConnectSymmetricPeer()
    {
        try
        {
            foreach (var socket in _sockets.Values)
            {
                _ = Task.Run(() => _probePeer(socket, _ipAddress!, _decodedEndpoint.Udp.Port - 15, _cancelToken.Token),
                    _cancelToken.Token);
            }

            _cancelToken.CancelAfter(TimeSpan.FromSeconds(90));
            _connectionObj = await _taskToken.Task;
        }
        catch (OperationCanceledException)
        {
            _connectionObj = CustomUdpPeerConnection.Failed(_handle.PeerId, _decodedEndpoint.Udp.ToIPEndPoint(), _sockets[_defaultPort]);
        }
        finally
        {
            if (!_cancelToken.IsCancellationRequested)
            {
                await _cancelToken.CancelAsync();
            }
            DestroyUdpSockets(_connectionObj!.UdpSocket.LocalEndPoint.Port);
        }
        
        return  _connectionObj;
    }

    private async Task<CustomUdpPeerConnection> ConnectSimplePeer()
    {
        _sockets[_defaultPort].PacketReceived += ReceivePacket;

        try
        {
            _ = _sockets[_defaultPort].SendPacket(_decodedEndpoint.Udp.ToIPEndPoint(), PEER_GREET.ToArray());
            _connectionObj = await _taskToken.Task;
        }
        finally
        {
            _sockets[_defaultPort].PacketReceived -= ReceivePacket;
        }

        return _connectionObj;
    }

    private void ReceivePacket(IPEndPoint endPoint, IPEndPoint localEndPoint, ReadOnlyMemory<byte> packet)
    {
        if (!_sockets.TryGetValue(localEndPoint.Port, out UdpSocket? socket))
        {
            return;
        }

        if (packet.Span.SequenceEqual(PEER_ACTIVE) && endPoint.Address.Equals(_ipAddress) 
                                                   && _confirmedCount >= 1 && _activeCount <= MAX_SAME_PACKETS)
        {
            _activeCount++;
            _ = socket.SendPacket(endPoint, PEER_ACTIVE.ToArray()); // Send another just in case it wasn't received.
            return;
        }
        
        if (packet.Span.SequenceEqual(PEER_CONFIRM) && endPoint.Address.Equals(_ipAddress) 
                                                    && _greetedCount >= 1 && _confirmedCount <= MAX_SAME_PACKETS)
        {
            _handle = new TransportHandle(_handle.PeerId, _handle.TransportType,
                TransportHandleEncoder.EncodeCustomUdp(endPoint), _handle.Flags, ReadOnlyMemory<byte>.Empty);
            
            CustomUdpPeerConnection peerConnection = new CustomUdpPeerConnection(endPoint, socket, _type, _handle);
            _ = socket.SendPacket(endPoint, PEER_CONFIRM.ToArray());
            _ = socket.SendPacket(endPoint, PEER_ACTIVE.ToArray());
            _taskToken.TrySetResult(peerConnection);
            _confirmedCount++;
            return;
        }

        if (packet.Span.SequenceEqual(PEER_GREET) && endPoint.Address.Equals(_ipAddress) 
                                                  && _greetedCount <= MAX_SAME_PACKETS)
        {
            _ = socket.SendPacket(endPoint, PEER_GREET.ToArray());
            _ = socket.SendPacket(endPoint, PEER_CONFIRM.ToArray());
            _greetedCount++;
            return;
        }

        // Repeat of previous packet, don't do anything
        if (packet.Span.SequenceEqual(PEER_GREET) || packet.Span.SequenceEqual(PEER_CONFIRM) ||
            packet.Span.SequenceEqual(PEER_ACTIVE)) { return; }
    }

    private void CreateUdpSockets(uint count)
    {
        for (uint i = 1; i < count; i++)
        {
            UdpSocket udpSocket = new UdpSocket(start: true);
            _sockets.Add(udpSocket.LocalEndPoint.Port, udpSocket);
            udpSocket.PacketReceived += ReceivePacket;
        }
    }

    private void DestroyUdpSockets(int exceptSocketAtPort)
    {
        foreach (UdpSocket socket in _sockets.Values)
        {
            socket.PacketReceived -= ReceivePacket;
            
            if (exceptSocketAtPort == socket.LocalEndPoint.Port) continue;
            _sockets.Remove(socket.LocalEndPoint.Port);
            
            if (_sockets[_defaultPort].Equals(socket)) continue;
            socket.Stop();
            socket.Dispose();
        }
    }

    private readonly Func<UdpSocket, IPAddress, int, CancellationToken, Task> _probePeer = async (sock, addr, port, cts) =>
    {
        int currentPort = port;
        while (currentPort <= 65535 && !cts.IsCancellationRequested /*&& stopwatch.Elapsed < TimeSpan.FromSeconds(90)*/)
        {
            try
            {
                _ = sock.SendPacket(new IPEndPoint(addr, currentPort++), PEER_GREET.ToArray());
                await Task.Delay(Config.Net!.SocketProbeLimitMilliseconds);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception e)
            {
                break;
            }
        }
    };

    public CustomUdpHolepuncher(UdpSocket defaultUdpSocket, TransportHandle transportHandle)
    {
        _defaultPort = defaultUdpSocket.LocalEndPoint.Port;
        _sockets.Add(_defaultPort, defaultUdpSocket);
        _handle = transportHandle;
    }
}
