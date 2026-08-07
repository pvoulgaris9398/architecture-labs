using System.Collections.Concurrent;
using Server.Models;

namespace Server.Services;

public sealed class SseConnectionManager
{
    private readonly ConcurrentDictionary<Guid, SseConnection> _connections = new();
    public IEnumerable<SseConnection> Connections => _connections.Values;

    public void Add(SseConnection connection) => _connections[connection.Id] = connection;

    public void Remove(Guid id) => _connections.TryRemove(id, out _);
}
