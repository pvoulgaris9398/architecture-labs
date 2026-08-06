using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using Server.Models;

namespace Server.Services;

public sealed class BroadcastService
{
    private readonly ConnectionManager _connections;

    public BroadcastService(ConnectionManager connections)
    {
        _connections = connections;
    }

    public async Task BroadcastAsync(
        EventRecord record,
        CancellationToken cancellationToken = default
    )
    {
        var payload = new EventMessage
        {
            Type = "event",
            Sequence = record.Sequence,
            Timestamp = record.Timestamp,
            Message = record.Message,
        };

        var json = JsonSerializer.Serialize(payload);

        var bytes = Encoding.UTF8.GetBytes(json);

        foreach (var connection in _connections.Connections)
        {
            if (connection.Socket.State != WebSocketState.Open)
                continue;

            try
            {
                await connection.Socket.SendAsync(
                    bytes,
                    WebSocketMessageType.Text,
                    true,
                    cancellationToken
                );
            }
            catch
            {
                // Part 3:
                // we'll disconnect dead sockets here
            }
        }
    }
}
