using System;
using NetworkingDemo.Shared.Configuration;

namespace NetworkingDemo.Networking.Core;

public class PeerPending(PeerAvailabilityToken peerAvailabilityToken)
{
    public PeerAvailabilityToken PeerAvailabilityToken { get; } = peerAvailabilityToken;
    public uint LatestPacketId => _latestPacketId++;
    private uint _latestPacketId = 0;
    
    // My sent packets
    private readonly PendingPacket[] _packets = new PendingPacket[Config.Net!.MaxPacketsStoredPerClient];
    private uint _rangeStart;
    private uint _rangeEnd;
    private uint _remoteLatestAck;
    
    // Received from peer
    private uint _bitfield = 0;
    private uint _highestReceivedPacketId;
    

    /// <summary>
    /// Add packet to the pending packets
    /// </summary>
    /// <param name="pendingPacket"></param>
    public void AddPacket(PendingPacket pendingPacket)
    {
        _rangeEnd = Math.Max(_rangeEnd, pendingPacket.PacketId) % (uint)_packets.Length;
        _packets[pendingPacket.PacketId % _packets.Length] = pendingPacket;
    }
    
    /// <summary>
    /// Resend any un-acked packets
    /// </summary>
    /// <param name="transportLayer">The transport to use for sending</param>
    public void ReQueueUnAcked(ITransportLayer transportLayer)
    {
        for (uint i = _rangeStart; i < _rangeEnd - 1; i++)
        {
            if (!_packets[i].Acknowledged)
            {
                transportLayer.EnqueueMessage(_packets[i].Payload, _packets[i].TransportHandle);
            }
        }
    }

    /// <summary>
    /// Get Ack info for packets received from this peer
    /// </summary>
    /// <returns>Highest received, bitfield of previous</returns>
    public (uint, uint) GetAckInfo()
    {
        return (_highestReceivedPacketId, _bitfield);
    }

    /// <summary>
    /// Process the ack info that the remote sent
    /// </summary>
    /// <param name="packetId">Id of the packet</param>
    /// <param name="remoteBitfield">Bitfield that came along with it</param>
    public void ProcessAck(uint packetId, uint remoteBitfield)
    {
        // Handle packet receive logic (register that it's received
        ReceivePacket(packetId);
        
        // Handle Ack
        _remoteLatestAck = Math.Max(_remoteLatestAck, packetId);
        uint newRangeStart = _remoteLatestAck % (uint)_packets.Length;
        
        for (int i = 0; i < 32; i++)
        {
            uint ackId = packetId - (uint)(i + 1);

            // Ack packets if included in bitfield, otherwise ensure we start resending up to the oldest packet
            if ((remoteBitfield & (1u << i)) != 0)
            {
                AckPacket(ackId);
            }
            else
            {
                newRangeStart = ackId % (uint)_packets.Length;
            }

            // Don't process packets further than oldest ack
            if (ackId % _packets.Length == _rangeStart) break;
        }
        
        _rangeStart = newRangeStart;
    }

    /// <summary>
    /// Ack the packet
    /// </summary>
    /// <param name="packetId"></param>
    private void AckPacket(uint packetId)
    {
        int index = (int)(packetId % _packets.Length);

        if (_packets[index].PacketId != packetId)
        {
            return;
        }
        
        _packets[index].Acknowledged = true;
    }
    
    /// <summary>
    /// Handle logic for receipt of peers packet
    /// </summary>
    /// <param name="packetId"></param>
    private void ReceivePacket(uint packetId)
    {
        // If this is a later ID than the previous highest
        if ((int)(packetId - _highestReceivedPacketId) > 0)
        {
            uint shift = packetId - _highestReceivedPacketId;
            
            if (shift >= 32) // Reset if too high ID
            {
                _bitfield = 0;
            }
            else //Shift over as much as needed
            {
                _bitfield <<= (int)shift;
            }
            
            // Set bit for previous packet
            _bitfield |= (1u << (int)(shift - 1));
            
            _highestReceivedPacketId = packetId;
        }
        else
        {
            uint diff = _highestReceivedPacketId - packetId;

            // Don't handle duplicates of latest
            if (diff == 0)
            {
                return;
            }

            // Handle packets within the 32 bit latest
            if (diff <= 32)
            {
                _bitfield |= (1u << (int)diff - 1);
            }
        }
    }
}
