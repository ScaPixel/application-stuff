using System.Net;
using NetworkingDemo.Shared.Types;

namespace NetworkingDemo.Networking.Core;

public enum CustomUdpConnectionType
{
    Simple,
    SimpleSymmetric,
    SymmetricSimple,
    Symmetric,
}

public class CustomUdpPeerConnection(
    IPEndPoint endPoint,
    UdpSocket udpSocket,
    CustomUdpConnectionType connectionType,
    TransportHandle handle)
{
    public IPEndPoint EndPoint { get; } = endPoint;
    public UdpSocket UdpSocket { get; } = udpSocket;
    public CustomUdpConnectionType ConnectionType { get; } = connectionType;
    public TransportHandle Handle { get; } = handle;
    // public TransportHandle Handle => new TransportHandle(_handle.PeerId, _handle.TransportType, _handle.Payload, _handle.Flags, );
    public Tick LatestSendPing { get; set; } = new Tick(0);
    public Tick LatestReceiveTick { get; set; } = new Tick(0);
    
    public static CustomUdpPeerConnection Failed(Guid id, IPEndPoint ep, UdpSocket socket) =>
        new CustomUdpPeerConnection(ep, socket, CustomUdpConnectionType.Simple, TransportHandle.Failed(id));
}
