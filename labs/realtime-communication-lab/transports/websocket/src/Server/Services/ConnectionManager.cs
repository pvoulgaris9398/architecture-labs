using System.Collections.Concurrent;
using Server.Models;

namespace Server.Services;

public sealed class ConnectionManager
{
    private readonly ConcurrentDictionary<Guid, ClientConnection> _connections = new();

    public IEnumerable<ClientConnection> Connections => _connections.Values;

    public int Count => _connections.Count;

    public ClientConnection Add(ClientConnection connection)
    {
        _connections[connection.Id] = connection;
        return connection;
    }

    public void Remove(Guid id)
    {
        _connections.TryRemove(id, out _);
    }

    public bool TryGet(Guid id, out ClientConnection? connection)
    {
        return _connections.TryGetValue(id, out connection);
    }
}
