namespace NetworkingDemo.Networking.Core;

public readonly struct QueuedPacket
{
    public QueuedPacket(ReadOnlyMemory<byte> packet, CustomUdpPeerConnection connection)
    {
        Packet = packet;
        Connection = connection;
    }

    public ReadOnlyMemory<byte> Packet { get; }
    public CustomUdpPeerConnection Connection { get; }
}
