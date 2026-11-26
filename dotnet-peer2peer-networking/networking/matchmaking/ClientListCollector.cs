using NetworkingDemo.Shared.Logging;

namespace NetworkingDemo.Networking;

public enum ClientListResult
{
    IdNotFound,
    Pending,
    Timeout,
    AllPacketsReceived,
    Success
}

public class ClientListCollector
{
    private Dictionary<uint, PendingClientList> _packets = new();
    public uint MaxRetries = 5;

    public void AddClient(PendingClientInfo clientInfo, uint listId)
    {
        if (!_packets.TryGetValue(listId, out PendingClientList? pendingClientList))
        {
            _packets[listId] = new PendingClientList
            {
                ExpectedCount = 100,
                Slots = [],
                StartTime = DateTime.UtcNow,
                Duration = TimeSpan.FromSeconds(5),
                Retries = 0
            };
            pendingClientList = _packets[listId];
        }
        
        pendingClientList.Slots.Add(clientInfo);
        pendingClientList.StartTime = DateTime.UtcNow;
    }

    public void NewPendingList(TimeSpan timeout, uint listId, uint expectedCount, int retry)
    {
        var list = new PendingClientList
        {
            ExpectedCount = expectedCount,
            Slots = [],
            StartTime = DateTime.UtcNow,
            Duration = timeout,
            Retries = retry,
        };

        if (_packets.TryGetValue(listId, out PendingClientList? pendingClientList))
        {
            list.Slots.AddRange(pendingClientList.Slots);
        }
        
        _packets[listId] = list;
    }

    public void ConfirmPacketReceived(uint listId, uint expectedCount)
    {
        if (_packets.TryGetValue(listId, out PendingClientList? pendingClientList) && pendingClientList.ExpectedCount == expectedCount)
        {
            pendingClientList.GotConfirmPacket = true;
        }
    }

    public ClientListResult IsDone(uint listId)
    {
        if (!_packets.TryGetValue(listId, out PendingClientList? pendingClientList))
        {
            return ClientListResult.IdNotFound;
        }
        
        bool timeoutPassed = pendingClientList.StartTime + pendingClientList.Duration <= DateTime.UtcNow;
        bool allClientsReceived = pendingClientList.Slots.Count >= pendingClientList.ExpectedCount;
        
            // Either this:
            // if (allClientsReceived && pendingClientList.GotConfirmPacket) { return ClientListResult.Success; }
            // if (allClientsReceived && !pendingClientList.GotConfirmPacket) { return ClientListResult.AllPacketsReceived; }
            // if (timeoutPassed) { return ClientListResult.Timeout; }
            // return ClientListResult.Pending;
            // Or this:
        return allClientsReceived switch
        {
            true when pendingClientList.GotConfirmPacket => ClientListResult.Success,
            true when !pendingClientList.GotConfirmPacket => ClientListResult.AllPacketsReceived,
            _ => timeoutPassed ? ClientListResult.Timeout : ClientListResult.Pending
        };
    }

    public List<PendingClientInfo> GetPendingClientList(uint listId)
    {
        return _packets[listId].Slots;
    }
    
    public void Cleanup()
    {
        _packets.Clear();
    }
}
