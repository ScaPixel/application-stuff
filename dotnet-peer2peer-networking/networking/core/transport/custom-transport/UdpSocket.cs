using System.Diagnostics.CodeAnalysis;
using System.Net;
using System.Net.Sockets;
using System.Text;
using NetworkingDemo.Shared.Logging;

namespace NetworkingDemo.Networking.Core;

public class UdpSocket : IDisposable
{
    private UdpClient _client;
    private bool _running = false;
    public bool Running => _running;
    private bool _disposed = false;

    public IPEndPoint LocalEndPoint => (IPEndPoint)_client.Client.LocalEndPoint;

    /// <summary>
    /// Packet received, SourceEndpoint, Local Endpoint, packet
    /// </summary>
    public event Action<IPEndPoint, IPEndPoint, ReadOnlyMemory<byte>>? PacketReceived;

    public void Start()
    {
        _running = true;
        _ = ReceiveLoop();
    }

    public void Stop()
    {
        _running = false;
    }

    public async Task SendPacket(IPEndPoint endPoint, ReadOnlyMemory<byte> data)
    {
        try
        {
            await _client.SendAsync(data, endPoint);
        }
        catch (ObjectDisposedException e)
        {
            throw;
        }
    }

    private async Task ReceiveLoop()
    {
        while (_running)
        {
            try
            {
                UdpReceiveResult result = await _client.ReceiveAsync();
                PacketReceived?.Invoke(result.RemoteEndPoint, LocalEndPoint, result.Buffer);
            }
            catch (ObjectDisposedException e)
            {
                // Socket closed
                break;
            }
            catch (Exception ex)
            {
                // Log error
            }
        }
    }

    public UdpSocket(int port = 0, bool start = false)
    {
        _client = new UdpClient(port);
        _client.Client.ReceiveBufferSize = 1024 * 1024;
        _client.Client.SendBufferSize = 1024 * 1024;

        if (start)
        {
            Start();
        }
    }

    public override bool Equals([NotNullWhen(true)] object? obj) => obj is UdpSocket socket && Equals(socket);
    private bool Equals(UdpSocket other) => LocalEndPoint.Equals(other.LocalEndPoint) && Running == other.Running && LocalEndPoint.Port == other.LocalEndPoint.Port;

    public void Dispose()
    {
        if (_disposed) return;
        
        Dispose(true);
        GC.SuppressFinalize(this);
        
        _disposed = true;
    }
    
    ~UdpSocket()
    {
        Dispose(false);
    }
    
    private void Dispose(bool disposing)
    {
        // Place to release unmanaged resources (if needed)
        
        if (disposing)
        {
            _client?.Close();
            _client?.Dispose();
        }
    }
}
