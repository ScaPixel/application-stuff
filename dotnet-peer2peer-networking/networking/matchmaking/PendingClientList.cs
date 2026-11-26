namespace NetworkingDemo.Networking;

public class PendingClientList
{
    public uint ExpectedCount;
    public List<PendingClientInfo> Slots;
    public DateTime StartTime;
    public TimeSpan Duration;
    public int Retries;
    public bool GotConfirmPacket;
}
