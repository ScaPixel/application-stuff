using System;
using System.Buffers;
using System.Buffers.Binary;
using System.Collections.Concurrent;
using System.Text;
using NetworkingDemo.Shared.Configuration;
using NetworkingDemo.Shared.Logging;
using NetworkingDemo.Shared.Types;

namespace NetworkingDemo.Networking.Core;

public class CustomUdpReliability : IReliabilityLayer
{
    private bool _disposed;
    private ConcurrentDictionary<Guid, PeerPending> _packetSlots = new();
    private const int HEADER_SIZE = (sizeof(uint) * 2) + 1;
    
    public ITransportLayer Transport { get; private set; }
    private Action<ReadOnlyMemory<byte>, TransportHandle>? _messageReceivedHandler;

    public void SetHandler(Action<ReadOnlyMemory<byte>, TransportHandle> handler)
    {
        _messageReceivedHandler = handler;
    }

    public void EnqueueSendReliable(ReadOnlyMemory<byte> packet, TransportHandle transportHandle, PeerAvailabilityToken peerAvailabilityToken)
    {
        // Get the peer pending object, or create if none exists for this peer
        if (!_packetSlots.TryGetValue(transportHandle.PeerId, out PeerPending? peerPending))
        {
            peerPending = new PeerPending(peerAvailabilityToken);
            _packetSlots[transportHandle.PeerId] = peerPending;
        }
        
        byte[] buffer = ArrayPool<byte>.Shared.Rent(HEADER_SIZE + packet.Length);

        try
        {
            Span<byte> span = buffer.AsSpan(0, buffer.Length);

            // Add header info
            (uint id, uint bits) ack = peerPending.GetAckInfo();
            span[0] = 0b0000_0001;
            BinaryPrimitives.WriteUInt32BigEndian(span.Slice(1, sizeof(uint)), ack.id);
            BinaryPrimitives.WriteUInt32BigEndian(span.Slice(1 + sizeof(uint), sizeof(uint)), ack.bits);
            
            // Add packet info
            packet.Span.CopyTo(span.Slice(HEADER_SIZE));
            
            Transport.EnqueueMessage(buffer, transportHandle);

            PendingPacket pendingPacket = new PendingPacket(peerPending.LatestPacketId, buffer, transportHandle);
            peerPending.AddPacket(pendingPacket);
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }
    }

    public void EnqueueBroadcastReliable(ReadOnlyMemory<byte> packet, TransportHandle[] transportHandles,
        PeerAvailabilityToken[] peerAvailabilityTokens)
    {
        for (int i = 0; i < peerAvailabilityTokens.Length; i++)
        {
            EnqueueSendReliable(packet, transportHandles[i], peerAvailabilityTokens[i]);
        }
    }

    public void EnqueueSendUnreliable(ReadOnlyMemory<byte> packet, TransportHandle transportHandle)
    {
        byte[] buffer = ArrayPool<byte>.Shared.Rent(HEADER_SIZE + packet.Length);

        try
        {
            Span<byte> span = buffer.AsSpan(0, buffer.Length);

            // Add header info
            span[0] = 0b0000_0000;
            
            // Add packet info
            packet.Span.CopyTo(span.Slice(HEADER_SIZE));
            
            Transport.EnqueueMessage(buffer, transportHandle);
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }
    }

    public void EnqueueBroadcastUnreliable(ReadOnlyMemory<byte> packet, TransportHandle[] transportHandles)
    {
        foreach (var handle in transportHandles)
        {
            EnqueueSendUnreliable(packet, handle);
        }
    }

    public void Tick(Tick currentTick)
    {
        Transport.Tick(currentTick);
        
        // TODO finish logic for the tick.
        foreach (var peerPending in _packetSlots.Values)
        {
            if (peerPending.PeerAvailabilityToken.IsValid)
            {
                peerPending.ReQueueUnAcked(Transport);
            }
        }
    }

    private void PacketReceived(TransportHandle transportHandle, ReadOnlyMemory<byte> packet)
    {
        switch (packet.Span[0])
        {
            case 0b0000_0000:
                HandleUnreliable(transportHandle, packet);
                break;
            case 0b0000_0001:
                HandleReliable(transportHandle, packet);
                break;
            default:
                break;
        }
    }

    private void HandleUnreliable(TransportHandle transportHandle, ReadOnlyMemory<byte> packet)
    {
        _messageReceivedHandler?.Invoke(packet[HEADER_SIZE..], transportHandle);
    }

    private void HandleReliable(TransportHandle transportHandle, ReadOnlyMemory<byte> packet)
    {
        ReadOnlyMemory<byte> header = packet[..HEADER_SIZE];
        // TODO Handle Ack and header info.
        uint packetId = BinaryPrimitives.ReadUInt32BigEndian(header.Span.Slice(1, sizeof(uint)));
        uint bitfield = BinaryPrimitives.ReadUInt32BigEndian(header.Span.Slice(1 + sizeof(uint), sizeof(uint)));
        if (_packetSlots.TryGetValue(transportHandle.PeerId, out var peerPending))
        {
            peerPending.ProcessAck(packetId, bitfield);
        }
        
        _messageReceivedHandler?.Invoke(packet[HEADER_SIZE..], transportHandle);
    }

    public void SetTransport(ITransportLayer transportLayer)
    {
        Transport = transportLayer;
        Transport.SetHandler(PacketReceived);
    }

    public CustomUdpReliability(ITransportLayer transportLayer)
    {
        Transport = transportLayer;
        transportLayer.SetHandler(PacketReceived);
    }

    public void Dispose()
    {
        if (_disposed) return;
        
        Transport.Dispose();
        GC.SuppressFinalize(this);
        
        _disposed = true;
    }
}
