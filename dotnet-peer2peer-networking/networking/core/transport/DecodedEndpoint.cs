using System.Net;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace NetworkingDemo.Networking.Core;

public enum DecodedEndpointType : byte
{
    Unknown = 0x00,
    CustomUdp = 0x01,
    Steam = 0x02,
    Eos = 0x03,
    // reserved 0x04-0x0F
}

public readonly struct DecodedEndpoint
{
    public Guid PeerId { get; }
    public DecodedEndpointType Type { get; }
    
    public readonly CompactUdpEndpoint Udp { get; }
    public readonly ulong SteamId { get; }
    public readonly ulong EosAccountId { get; }
    
    private DecodedEndpoint(Guid peerId, DecodedEndpointType type, CompactUdpEndpoint udp, ulong steamId, ulong eosId)
    {
        PeerId = peerId;
        Type = type;
        Udp = udp;
        SteamId = steamId;
        EosAccountId = eosId;
    }

    public static DecodedEndpoint PeerCustomUdp(Guid peerId, CompactUdpEndpoint udp)
        => new DecodedEndpoint(peerId, DecodedEndpointType.CustomUdp, udp, 0UL, 0UL);

    public static DecodedEndpoint PeerSteam(Guid peerId, ulong steamId)
        => new DecodedEndpoint(peerId, DecodedEndpointType.Steam, default, steamId, 0UL);

    public static DecodedEndpoint PeerEos(Guid peerId, ulong eosId)
        => new DecodedEndpoint(peerId, DecodedEndpointType.Eos, default, 0UL, eosId);
}

[StructLayout(LayoutKind.Sequential, Pack = 1)]
public readonly struct CompactUdpEndpoint(ulong a, ulong b, ushort port, byte len)
{
    private readonly ulong _a = a;
    private readonly ulong _b = b;
    public readonly ushort Port = port;
    public readonly byte Len = len;

    public static bool TryParse(ReadOnlySpan<byte> payload, out CompactUdpEndpoint result)
    {
        result = default;
        if (payload.Length < 1 + 2) return false;
        byte len = payload[0];
        if (len != 4 && len != 16) return false;
        int expected = 1 + len + 2;
        if (payload.Length != expected) return false;
        
        ReadOnlySpan<byte> addrSpan = payload.Slice(1, len);
        ushort port = (ushort)((payload[1 + len] << 8) | payload[2 + len]);
        
        Span<byte> temp = stackalloc byte[16];
        if (len == 4)
        {
            // IPv4
            temp.Clear();
            addrSpan.CopyTo(temp.Slice(12, 4));
        }
        else
        {
            // IPv6
            addrSpan.CopyTo(temp);
        }
        
        ulong a = MemoryMarshal.Read<ulong>(temp);
        ulong b = MemoryMarshal.Read<ulong>(temp.Slice(8));
        
        result = new CompactUdpEndpoint(a, b, port, len);
        return true;
    }

    public IPEndPoint ToIPEndPoint()
    {
        Span<byte> buf = stackalloc byte[16];
        MemoryMarshal.Write<ulong>(buf, ref Unsafe.AsRef(in _a));
        MemoryMarshal.Write<ulong>(buf.Slice(8), ref Unsafe.AsRef(in _b));

        if (Len == 4)
        {
            var ipv4 = new byte[4];
            buf.Slice(12, 4).CopyTo(ipv4);
            return new IPEndPoint(new IPAddress(ipv4), Port);
        }
        else 
        {
            var ipv6 = buf.ToArray();
            return new IPEndPoint(new IPAddress(ipv6), Port);
        }
    }
    
    public override int GetHashCode() => HashCode.Combine(_a, _b, Port, Len);
    public override bool Equals(object? obj) => obj is CompactUdpEndpoint o && Equals(o);
    private bool Equals(CompactUdpEndpoint other)
        => _a == other._a && _b == other._b && Port == other.Port && Len == other.Len;
}
