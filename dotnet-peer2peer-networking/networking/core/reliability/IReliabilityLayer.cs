using NetworkingDemo.Shared.Types;

namespace NetworkingDemo.Networking.Core;

public interface IReliabilityLayer : IDisposable
{
    public ITransportLayer Transport { get; }
    void SetHandler(Action<ReadOnlyMemory<byte>, TransportHandle> handler);
    void SetTransport(ITransportLayer transportLayer);
    void EnqueueSendReliable(ReadOnlyMemory<byte> packet, TransportHandle transportHandle, PeerAvailabilityToken peerAvailabilityToken);
    void EnqueueBroadcastReliable(ReadOnlyMemory<byte> packet, TransportHandle[] transportHandles, PeerAvailabilityToken[] packetHandles);
    void EnqueueSendUnreliable(ReadOnlyMemory<byte> packet, TransportHandle transportHandle);
    void EnqueueBroadcastUnreliable(ReadOnlyMemory<byte> packet, TransportHandle[] transportHandles);
    // Task SendImmediateReliable(ReadOnlyMemory<byte> packet, TransportHandle transportHandle, PeerAvailabilityToken peerAvailabilityToken);
    // Task BroadcastImmediateReliable(ReadOnlyMemory<byte> packet, TransportHandle[] transportHandles, PeerAvailabilityToken peerAvailabilityToken);
    // Task SendImmediateUnreliable(ReadOnlyMemory<byte> packet, TransportHandle transportHandle);
    // Task BroadcastImmediateUnreliable(ReadOnlyMemory<byte> packet, TransportHandle[] transportHandles);
    void Tick(Tick currentTick);
}
