using System.Net;
using System.Text;
using NetworkingDemo.Shared.Configuration;
using NetworkingDemo.Networking.Core;
using NetworkingDemo.Shared.Logging;
using NetworkingDemo.Shared.Settings;

namespace NetworkingDemo.Networking;

public class CustomNetStack : INetStack
{
    public event Action<INetwork>? SessionNetworkReady;
    
    private bool _disposed = false;
    public ITransportLayer? TransportLayer { get; private set; }
    public IReliabilityLayer? ReliabilityLayer { get; private set; }
    public IMatchmaker? Matchmaker { get; private set; }
    public INetwork? Network { get; private set; }
    public NetworkType Type { get; private set; } = NetworkType.CustomUdp;

    private TaskCompletionSource<bool>? _connectionAvailableTcs;
    private IPEndPoint? _localEndPoint { get; set; }

    private static ReadOnlySpan<byte> MY_IP_MSG => Encoding.UTF8.GetBytes($"myip;{Config.Core!.AppId}");
    private static ReadOnlySpan<byte> MY_IP_RESP_HEADER => "ip;"u8;
    private TransportHandle _ipHandle { get; set; }
    private TransportHandle _signalHandle { get; set; }
    private bool _hardNat;
    public bool Available { get; private set; }
    
    public async Task<bool> Init(UserProfileDto profile)
    {
        _connectionAvailableTcs = new TaskCompletionSource<bool>();
        CustomUdpTransport transportLayer = new();
        transportLayer.SetHandler(OnMessageReceived);

        CreateServerHandles();
        await transportLayer.Connect(_signalHandle);
        await transportLayer.Connect(_ipHandle);
        
        var result = await CheckConnection(transportLayer);
        
        // // Init other parts if connection is available
        if (!result)
        {
            return false;
        }

        transportLayer.Disconnect(_ipHandle);
        
        TransportLayer = transportLayer;
        MatchmakingSettings matchmakerSettings = new() { ProfileInfo = profile, SymNat = _hardNat, TransportLayer = TransportLayer };
        Matchmaker = new CustomMatchmaker(matchmakerSettings);
        Matchmaker.SessionStarted += OnMatchmakerSessionStarted;

        Available = true;
        return true;
    }

    private void CreateServerHandles()
    {
        IPEndPoint ipEndPoint = IPEndPoint.Parse(Config.Net!.IpServerAddr);
        IPEndPoint signalEndPoint = IPEndPoint.Parse(Config.Net.SignalingServerAddr);
        _ipHandle =
            new TransportHandle(Guid.NewGuid(), TransportType.CustomUdp, TransportHandleEncoder.EncodeCustomUdp(ipEndPoint),
                TransportFlags.ServerContact, ReadOnlyMemory<byte>.Empty);
        _signalHandle =
            new TransportHandle(Guid.NewGuid(), TransportType.CustomUdp,
                TransportHandleEncoder.EncodeCustomUdp(signalEndPoint), TransportFlags.ServerContact,
                ReadOnlyMemory<byte>.Empty);
    }

    private void OnMatchmakerSessionStarted(NetSession session)
    {
        ReliabilityLayer = new CustomUdpReliability(TransportLayer!);
        Network = new CustomUdpNetwork(ReliabilityLayer, session);
        SessionNetworkReady?.Invoke(Network);
    }

    private async Task<bool> CheckConnection(ITransportLayer transportLayer)
    {
        // Wait for the result of the net check, or a timeout
        bool result;
        try
        {
            await transportLayer.SendMessageImmediate(MY_IP_MSG.ToArray(), _signalHandle);
            await transportLayer.SendMessageImmediate(MY_IP_MSG.ToArray(), _ipHandle);
            result = await WaitWithTimeout(_connectionAvailableTcs!, 5);
        }
        catch (TimeoutException)
        {
            // Retry the task
            try
            {
                await transportLayer.SendMessageImmediate(MY_IP_MSG.ToArray(), _signalHandle);
                await transportLayer.SendMessageImmediate(MY_IP_MSG.ToArray(), _ipHandle);
                result = await WaitWithTimeout(_connectionAvailableTcs!, 10);
            }
            catch (TimeoutException)
            {
                // Third attempt
                try
                {
                    await transportLayer.SendMessageImmediate(MY_IP_MSG.ToArray(), _signalHandle);
                    await transportLayer.SendMessageImmediate(MY_IP_MSG.ToArray(), _ipHandle);
                    result = await WaitWithTimeout(_connectionAvailableTcs!, 15);
                }
                catch (TimeoutException)
                {
                    result = false;
                }
            }
        }

        return result;
    }

    private async Task<T> WaitWithTimeout<T>(TaskCompletionSource<T> task, int timeoutSeconds)
    {
        var completed = await Task.WhenAny(task.Task, Task.Delay(timeoutSeconds * 1000));

        if (completed == task.Task) return await task.Task;
        
        throw new TimeoutException();
    }

    void OnMessageReceived(TransportHandle handle, ReadOnlyMemory<byte> packet)
    {
        ReadOnlySpan<byte> msgSpan = packet.Span[MY_IP_RESP_HEADER.Length..];
        if (!packet.Span[..MY_IP_RESP_HEADER.Length].SequenceEqual(MY_IP_RESP_HEADER))
        {
            return;
        }
        
        // Parse message into parts
        string msgString = Encoding.UTF8.GetString(msgSpan);
        string[] parts = msgString.Split(';');
        if (parts.Length != 2)
        {
            return;
        }
        
        string ipPort = parts[0];
        string appId = parts[1];

        if (!appId.Equals(Config.Core?.AppId))
        {
            return;
        }
        
        // Parse IP and Port from IP part of the message
        string[] ipParts = ipPort.Split(':');
        if (ipParts.Length != 2)
        {
            return;
        }

        if (!IPAddress.TryParse(ipParts[0], out var ipAddr))
        {
            return;
        }

        if (!int.TryParse(ipParts[1], out var port))
        {
            return;
        }
        
        // Compare addresses to figure out if we are behind a hard NAT or not 
        if (_localEndPoint == null)
        {
            _localEndPoint = new IPEndPoint(ipAddr, port);
        }
        else
        {
            IPEndPoint otherEndPoint = new IPEndPoint(ipAddr, port);
            if (!_localEndPoint.Equals(otherEndPoint))
            {
                _hardNat = true;
            }
            _connectionAvailableTcs?.TrySetResult(true);
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        
        // Network.Dispose(); // Not needed, no signals etc
        Matchmaker!.SessionStarted -= OnMatchmakerSessionStarted;
        Matchmaker!.Dispose(); // Needed to disconnect signals etc
        ReliabilityLayer!.Dispose(); // Needed to disconnect signals etc
        TransportLayer!.Dispose(); // Close all sockets and disconnect signals
        GC.SuppressFinalize(this);

        _disposed = true;
    }
}
