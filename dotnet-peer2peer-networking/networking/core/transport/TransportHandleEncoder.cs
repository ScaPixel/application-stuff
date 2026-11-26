using System.Net;

namespace NetworkingDemo.Networking.Core;

public static class TransportHandleEncoder
{
    public static bool TryDecode(Guid peerId, ReadOnlyMemory<byte> payload, out DecodedEndpoint endPoint)
    {
        byte typeByte = payload.Span[0];
        var body = payload[1..];
        
        var type = (TransportType)typeByte;
        switch (type)
        {
            case TransportType.CustomUdp:
                if (TryDecodeCustomUdp(body, out var udpEndPoint))
                {
                    endPoint = DecodedEndpoint.PeerCustomUdp(peerId, udpEndPoint);
                    return true;
                }
                
                endPoint = default;
                return false;
            case TransportType.Steam:
                if (TryDecodeSteam(body, out var steamHandle))
                {
                    endPoint = DecodedEndpoint.PeerSteam(peerId, steamHandle);
                    return true;
                }
                
                endPoint = default;
                return false;
            case TransportType.Eos:
                if (TryDecodeEos(body, out var eosHandle))
                {
                    endPoint = DecodedEndpoint.PeerEos(peerId, eosHandle);
                    return true;
                }
                
                endPoint = default;
                return false;
            default:
                endPoint = default;
                return false;
        }
    }
    
    public static ReadOnlyMemory<byte> EncodeCustomUdp(IPEndPoint endPoint)
    {
        var addrBytes = endPoint.Address.GetAddressBytes();
        var addrLen = addrBytes.Length;
        var port = (ushort)endPoint.Port;
        byte[] buf = new byte[1 + 1 + addrBytes.Length + 2];

        int i = 0;
        buf[i++] = (byte)TransportType.CustomUdp;
        buf[i++] = (byte)addrLen;
        Buffer.BlockCopy(addrBytes, 0, buf, i, addrLen); i += addrLen;
        buf[i++] = (byte)(port >> 8);
        buf[i] = (byte)(port & 0xFF);
        
        return buf;
    }
    
    public static bool TryDecodeCustomUdp(ReadOnlyMemory<byte> span, out CompactUdpEndpoint endPoint)
    {
        return CompactUdpEndpoint.TryParse(span.Span, out endPoint);
    }
    
    // TODO: Implement these (if steam/eos/other added)
    public static ReadOnlyMemory<byte> EncodeSteam(ulong steamHandle)
    {
        throw new NotImplementedException();
    }
    
    public static bool TryDecodeSteam(ReadOnlyMemory<byte> span, out ulong steamHandle)
    {
        throw new NotImplementedException();
    }
    
    public static ReadOnlyMemory<byte> EncodeEos(ulong eosHandle)
    {
        throw new NotImplementedException();
    }
    
    public static bool TryDecodeEos(ReadOnlyMemory<byte> span, out ulong eosHandle)
    {
        throw new NotImplementedException();
    }
}
