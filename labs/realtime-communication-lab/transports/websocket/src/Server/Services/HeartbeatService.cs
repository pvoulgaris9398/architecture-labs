using System.Net.WebSockets;

namespace Server.Services;

public sealed class HeartbeatService : BackgroundService
{
    private readonly ConnectionManager _manager;

    public HeartbeatService(ConnectionManager manager)
    {
        _manager = manager;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            foreach (var c in _manager.Connections)
            {
                if (DateTime.UtcNow - c.LastSeenUtc > TimeSpan.FromMinutes(2))
                {
                    await c.Socket.CloseAsync(
                        WebSocketCloseStatus.NormalClosure,
                        "Heartbeat timeout",
                        stoppingToken
                    );
                }
            }

            await Task.Delay(TimeSpan.FromSeconds(30), stoppingToken);
        }
    }
}
