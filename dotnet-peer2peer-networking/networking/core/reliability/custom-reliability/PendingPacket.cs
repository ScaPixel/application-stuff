namespace NetworkingDemo.Networking.Core;

public class PendingPacket(uint packetId, ReadOnlyMemory<byte> payload, TransportHandle transportHandle)
{
    public uint PacketId { get; } = packetId;
    public ReadOnlyMemory<byte> Payload { get; } = payload;
    public TransportHandle TransportHandle { get; } = transportHandle;
    public bool Acknowledged { get; set; }
}
