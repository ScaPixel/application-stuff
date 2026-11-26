using System.ComponentModel;
using System.Diagnostics.CodeAnalysis;

namespace NetworkingDemo.Networking.Core;

public enum TransportType : byte
{
    Unknown = 0x00,
    CustomUdp = 0x01,
    Steam = 0x02,
    Eos = 0x03,
    // reserved 0x04-0x0F
}

[Flags]
public enum TransportFlags : byte
{
    None = 0,
    ConnectionFailed = 1 << 0,
    PeerSymmetricNat = 1 << 1,
    SelfSymmetricNat = 1 << 2,
    ServerContact = 1 << 3,
    HasRelay = 1 << 4,
    // Reserved next few bits for other information that can be useful later
}

public readonly struct TransportHandle
{
    public Guid PeerId { get; }
    public TransportType TransportType { get; }
    public ReadOnlyMemory<byte> Payload { get; }
    public TransportFlags Flags { get; }
    public ReadOnlyMemory<byte> AdditionalInfo { get; }

    public TransportHandle(Guid peerId, TransportType transportType, ReadOnlyMemory<byte> payload, TransportFlags flags,
        ReadOnlyMemory<byte> additionalInfo)
    {
        if (!Enum.IsDefined(typeof(TransportType), transportType))
            throw new InvalidEnumArgumentException(nameof(transportType), (int)transportType, typeof(TransportType));
        PeerId = peerId;
        TransportType = transportType;
        Payload = payload;
        Flags = flags;
        AdditionalInfo = additionalInfo;
    }
    
    public static TransportHandle Failed(Guid id) => new TransportHandle(id, TransportType.Unknown,
        ReadOnlyMemory<byte>.Empty, TransportFlags.ConnectionFailed, ReadOnlyMemory<byte>.Empty);

    public override bool Equals([NotNullWhen(true)] object? obj) => obj is TransportHandle o && Equals(o);

    private bool Equals(TransportHandle other) => PeerId == other.PeerId && TransportType == other.TransportType && Flags == other.Flags;
}
