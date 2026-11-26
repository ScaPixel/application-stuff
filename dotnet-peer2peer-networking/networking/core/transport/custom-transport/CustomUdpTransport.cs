using System.Net;
using System.Text;
using NetworkingDemo.Shared.Configuration;
using NetworkingDemo.Shared.Logging;
using NetworkingDemo.Shared.Types;

namespace NetworkingDemo.Networking.Core;

public class CustomUdpTransport : ITransportLayer
{
    private bool _disposed = false;
    private bool _running = false;
    public bool IsRunning => _udpSocket.Running && _running;
    public ReadOnlyMemory<byte> HeartbeatMsg { get; set; }
    public ReadOnlyMemory<byte> HeartbeatResp { get; set; }
    private readonly UdpSocket _udpSocket = new();
    private readonly Dictionary<Guid, CustomUdpPeerConnection> _peerConnections = new();
    private readonly Dictionary<IPEndPoint, CustomUdpPeerConnection> _registeredEndpoints = new();
    private IPEndPoint? _relayEndPoint { get; set; }
    private Tick _currentTick;
    private ushort _queueAmount = 0;
    private readonly QueuedPacket[] _queue;

    private Action<TransportHandle, ReadOnlyMemory<byte>>? _messageReceivedHandler;
    public event Action<TransportHandle>? ClientConnectionLost;

    public void SetHandler(Action<TransportHandle, ReadOnlyMemory<byte>> handler)
    {
        _messageReceivedHandler = handler;
    }

    public void EnqueueMessage(ReadOnlyMemory<byte> packet, TransportHandle targetHandle)
    {
        if (targetHandle.TransportType != TransportType.CustomUdp)
        {
            return;
        }

        if (!_peerConnections.TryGetValue(targetHandle.PeerId, out var connection))
        {
            return;
        }
        
        _queue[_queueAmount++] = new QueuedPacket(packet, connection);
    }

    public void EnqueueBroadcastMessage(ReadOnlyMemory<byte> packet, TransportHandle[] targets)
    {
        foreach (var target in targets)
        {
            EnqueueMessage(packet, target);
        }
    }

    public async Task SendMessageImmediate(ReadOnlyMemory<byte> packet, TransportHandle targetHandle)
    {
        if (targetHandle.TransportType != TransportType.CustomUdp)
        {
            return;
        }

        if (!_peerConnections.TryGetValue(targetHandle.PeerId, out var connection))
        {
            return;
        }
        
        await connection.UdpSocket.SendPacket(connection.EndPoint, packet);
    }

    public async Task BroadcastMessageImmediate(ReadOnlyMemory<byte> packet, TransportHandle[] targets)
    {
        foreach (var target in targets)
        {
            await SendMessageImmediate(packet, target);
        }
    }

    public async Task<TransportHandle> Connect(TransportHandle transportHandle)
    {
        if ((transportHandle.Flags & TransportFlags.ServerContact) == TransportFlags.ServerContact)
        {
            return ConnectServer(transportHandle);
        }
        return await ConnectClient(transportHandle);
    }

    private TransportHandle ConnectServer(TransportHandle transportHandle)
    {
        if (!TransportHandleEncoder.TryDecode(transportHandle.PeerId, transportHandle.Payload, out DecodedEndpoint decodedEndpoint))
        {
            return TransportHandle.Failed(transportHandle.PeerId);
        }

        if (_registeredEndpoints.ContainsKey(decodedEndpoint.Udp.ToIPEndPoint()))
        {
            return _registeredEndpoints[decodedEndpoint.Udp.ToIPEndPoint()].Handle;
        }
        
        CustomUdpPeerConnection connection = 
            new CustomUdpPeerConnection(decodedEndpoint.Udp.ToIPEndPoint(), _udpSocket, CustomUdpConnectionType.Simple, transportHandle);
        
        _peerConnections.Add(transportHandle.PeerId, connection);
        _registeredEndpoints.Add(connection.EndPoint, connection);

        return connection.Handle;
    }

    private async Task<TransportHandle> ConnectClient(TransportHandle transportHandle)
    {
        if (!TransportHandleEncoder.TryDecode(transportHandle.PeerId, transportHandle.Payload, out DecodedEndpoint decodedEndpoint))
        {
            return CustomUdpPeerConnection.Failed(transportHandle.PeerId, decodedEndpoint.Udp.ToIPEndPoint(), _udpSocket).Handle;
        }
        
        // Check if already connected.
        if (_registeredEndpoints.TryGetValue(decodedEndpoint.Udp.ToIPEndPoint(), out var connection))
        {
            return connection.Handle;
        }
        
        // Setup needed variables
        TaskCompletionSource<CustomUdpPeerConnection> tcs = new();
        CancellationTokenSource cts = new();
        CustomUdpPeerConnection conn = CustomUdpPeerConnection.Failed(transportHandle.PeerId, decodedEndpoint.Udp.ToIPEndPoint(), _udpSocket);

        // Run all strategies
        try
        {
            _ = Task.Run(() => _attemptHolePunch(_udpSocket, transportHandle, tcs), cts.Token);
            _ = Task.Run(() => _attemptRelay(_udpSocket, transportHandle, tcs), cts.Token);
            cts.CancelAfter(TimeSpan.FromSeconds(90));

            conn = await tcs.Task;
        }
        catch (OperationCanceledException)
        {
            // Log exception
        }
        catch (Exception e)
        {
            // Log exception
        }
        finally // Cleanup
        {
            if (!cts.IsCancellationRequested)
            {
                await cts.CancelAsync();
            }

            // Add connection to list if successful
            if ((conn.Handle.Flags & TransportFlags.ConnectionFailed) == 0)
            {
                _peerConnections.Add(transportHandle.PeerId, conn);
                _registeredEndpoints.Add(conn.EndPoint, conn);
                if (conn.ConnectionType == CustomUdpConnectionType.Symmetric && !conn.UdpSocket.Equals(_udpSocket))
                {
                    conn.UdpSocket.PacketReceived += OnSocketReceiveMessage;
                }
            }
            
            // Any other cleanup goes here
        }
        
        return conn.Handle;
    }

    private readonly Func<UdpSocket, TransportHandle, TaskCompletionSource<CustomUdpPeerConnection>, Task> _attemptHolePunch = async (sock, handle, tcs) =>
    {
        CustomUdpHolepuncher holepuncher = new CustomUdpHolepuncher(sock, handle);
        var peerConnection = await holepuncher.Connect();
        if ((peerConnection.Handle.Flags & TransportFlags.ConnectionFailed) == TransportFlags.ConnectionFailed)
        {
            return;
        }

        tcs.TrySetResult(peerConnection);
    };

    private readonly Func<UdpSocket, TransportHandle, TaskCompletionSource<CustomUdpPeerConnection>, Task> _attemptRelay = async (sock, handle, tcs) =>
    {
        if ((handle.Flags & TransportFlags.HasRelay) != TransportFlags.HasRelay)
        {
            return;
        }

        // TODO implement relay connection
        CustomUdpRelayConnector connector = new CustomUdpRelayConnector(sock, handle);
        var connection = await connector.Connect();
        if ((connection.Handle.Flags & TransportFlags.ConnectionFailed) == TransportFlags.ConnectionFailed)
        {
            return;
        }
        
        tcs.TrySetResult(connection);
    };

    public void Disconnect(TransportHandle transportHandle)
    {
        if (!_peerConnections.TryGetValue(transportHandle.PeerId, out CustomUdpPeerConnection? connection))
        {
            return;
        }
        
        if (connection.ConnectionType == CustomUdpConnectionType.Symmetric && !connection.UdpSocket.Equals(_udpSocket))
        {
            connection.UdpSocket.PacketReceived -= OnSocketReceiveMessage;
        }

        _registeredEndpoints.Remove(connection.EndPoint);
        _peerConnections.Remove(transportHandle.PeerId);
    }

    public void Start()
    {
        if (_running)
        {
            return;
        }
        _running = true;
        _udpSocket.PacketReceived += OnSocketReceiveMessage;
        _udpSocket.Start();
    }

    public void Stop()
    {
        if (!_running)
        {
            return;
        }
        _running = false;
        _udpSocket.PacketReceived -= OnSocketReceiveMessage;
        _udpSocket.Stop();
    }

    public void Tick(Tick currentTick)
    {
        _currentTick = currentTick;
        foreach (var connection in _peerConnections.Values)
        {
            if (_currentTick - connection.LatestSendPing >= 5 * Config.Sim!.TickRate) // TODO Add system for tick rate slowdown (in case sim takes too long)
            {
                connection.LatestSendPing = currentTick;
                _ = connection.UdpSocket.SendPacket(connection.EndPoint, HeartbeatMsg.ToArray());
            }

            if (_currentTick - connection.LatestReceiveTick >= Config.Net!.ClientConnectionLostTimeoutSeconds * Config.Sim.TickRate)
            {
                ClientConnectionLost?.Invoke(connection.Handle);
            }
        }

        for (ushort i = 0; i < _queueAmount; i++)
        {
            _queue[i].Connection.LatestSendPing = _currentTick;
            _ = _queue[i].Connection.UdpSocket.SendPacket(_queue[i].Connection.EndPoint, _queue[i].Packet);
        }
        _queueAmount = 0;
    }

    private void OnSocketReceiveMessage(IPEndPoint endPoint, IPEndPoint localEndPoint, ReadOnlyMemory<byte> buffer)
    {
        // Map to message format and emit MessageReceived(TransportHandle, ReadOnlyMemory<byte>) event
        if (!_registeredEndpoints.TryGetValue(endPoint, out CustomUdpPeerConnection? connection))
        {
            return;
        }
        
        connection.LatestReceiveTick = _currentTick;
        if (buffer.Span.SequenceEqual(HeartbeatMsg.Span))
        {
            _ = connection.UdpSocket.SendPacket(endPoint, HeartbeatResp.ToArray());
            return;
        }

        if (buffer.Span.SequenceEqual(HeartbeatResp.Span)) { return; }
        
        _messageReceivedHandler?.Invoke(connection.Handle, buffer);
    }

    public CustomUdpTransport(bool start = true)
    {
        if (Config.Net == null)
        { 
            // Log an error 
        }

        _queue = new QueuedPacket[Config.Net!.MaxPeers * 5];
        
        if (start)
        {
            Start();
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        
        foreach (var connection in _peerConnections.Values)
        {
            connection.UdpSocket.PacketReceived -= OnSocketReceiveMessage;
            connection.UdpSocket.Dispose();
        }
        GC.SuppressFinalize(this);
        
        _disposed = true;
    }
}
