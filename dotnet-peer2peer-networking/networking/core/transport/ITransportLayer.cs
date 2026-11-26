using NetworkingDemo.Shared.Types;

namespace NetworkingDemo.Networking.Core;

/*
Just deal with the actual message transporting.
Send message to either one or all peers
Emit event when message arrived
Test if there is connection to system at all. (Signaling, peer etc.)
*/

public interface ITransportLayer : IDisposable
{
    bool IsRunning { get; }
    ReadOnlyMemory<byte> HeartbeatMsg { get; set; }
    ReadOnlyMemory<byte> HeartbeatResp { get; set; }
    
    void SetHandler(Action<TransportHandle, ReadOnlyMemory<byte>> handler);
    
    /// <summary>
    /// Client connection lost
    /// </summary>
    event Action<TransportHandle> ClientConnectionLost;

    /// <summary>
    /// Unreliable version of the EnqueueMessage method. Try to send to a specific peer. 
    /// Message is queued for sending on the next tick
    /// </summary>
    /// <param name="packet">message packet</param>
    /// <param name="targetHandle">where to send to</param>
    /// /// <returns>Whether sending was successful or not</returns>
    void EnqueueMessage(ReadOnlyMemory<byte> packet, TransportHandle targetHandle);
    
    /// <summary>
    /// Unreliable version of the SendMessageImmediate method. Try to send to a specific peer.
    /// Send right now. Don't care about ticks
    /// </summary>
    /// <param name="packet">message packet</param>
    /// <param name="targetHandle">where to send to</param>
    /// /// <returns>Whether sending was successful or not</returns>
    Task SendMessageImmediate(ReadOnlyMemory<byte> packet, TransportHandle targetHandle);

    /// <summary>
    /// Try to broadcast message unreliably to all targets. 
    /// Message is queued for sending on the next tick. 
    /// If any target has incorrect TransportType, that target is skipped.
    /// </summary>
    /// <param name="packet">The packet to send</param>
    /// <param name="targets">The TransportHandle for all targets</param>
    /// <returns>List of true/false per target of whether sending was successful</returns>
    void EnqueueBroadcastMessage(ReadOnlyMemory<byte> packet, TransportHandle[] targets);
    
    /// <summary>
    /// Try to broadcast message unreliably to all targets.
    /// Broadcast right now. Don't care about ticks
    /// If any target has incorrect TransportType, that target is skipped.
    /// </summary>
    /// <param name="packet">The packet to send</param>
    /// <param name="targets">The TransportHandle for all targets</param>
    /// <returns>List of true/false per target of whether sending was successful</returns>
    Task BroadcastMessageImmediate(ReadOnlyMemory<byte> packet, TransportHandle[] targets);

    /// <summary>
    /// Connect to a new client at the given address
    /// </summary>
    /// <param name="transportHandle">Address details about the connection</param>
    /// <returns>Details about the connection</returns>
    Task<TransportHandle> Connect(TransportHandle transportHandle);
    
    /// <summary>
    /// Disconnect from the client at the given address
    /// </summary>
    /// <param name="transportHandle">Details about the connection to terminate</param>
    void Disconnect(TransportHandle transportHandle);

    /// <summary>
    /// Start the transport
    /// </summary>
    void Start();
    
    /// <summary>
    /// Stop the transport
    /// </summary>
    void Stop();

    /// <summary>
    /// Run a network tick
    /// </summary>
    /// <param name="currentTick">id of current tick</param>
    void Tick(Tick currentTick);
}
