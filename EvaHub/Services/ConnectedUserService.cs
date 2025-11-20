namespace EvaHub.Services;

public class ConnectedUserService
{
    private readonly HashSet<string> _connectedUsers = new();
    private readonly object _lock = new();

    public void AddUser(string userId)
    {
        lock (_lock)
        {
            _connectedUsers.Add(userId);
        }
    }

    public void RemoveUser(string userId)
    {
        lock (_lock)
        {
            _connectedUsers.Remove(userId);
        }
    }

    public bool IsUserConnected(string userId)
    {
        lock (_lock)
        {
            return _connectedUsers.Contains(userId);
        }
    }
}