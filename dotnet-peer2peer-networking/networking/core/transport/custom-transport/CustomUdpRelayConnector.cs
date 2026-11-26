using System.Net;

namespace NetworkingDemo.Networking.Core;

public class CustomUdpRelayConnector(UdpSocket udpSocket, TransportHandle handle)
{
    private UdpSocket _udpSocket = udpSocket;
    private TransportHandle _handle = handle;
    private IPEndPoint _endPoint;
    private IPEndPoint _relayEndPoint;

    public async Task<CustomUdpPeerConnection> Connect()
    {
        if (!(TransportHandleEncoder.TryDecode(handle.PeerId, handle.Payload, out DecodedEndpoint decodedEndpoint) 
            && TransportHandleEncoder.TryDecode(handle.PeerId, handle.AdditionalInfo, out DecodedEndpoint decodedRelayEndpoint)))
        {
            return CustomUdpPeerConnection.Failed(_handle.PeerId, decodedEndpoint.Udp.ToIPEndPoint(), _udpSocket);
        }
    
        _endPoint = decodedEndpoint.Udp.ToIPEndPoint();
        _relayEndPoint = decodedRelayEndpoint.Udp.ToIPEndPoint();
        
        // TODO figure out how relay logic should work
        return CustomUdpPeerConnection.Failed(_handle.PeerId, _endPoint, _udpSocket); // TODO implement actual
    }
    
    
}
