using System.Text.Json;
using Server.Models;
using Server.Serialization;

namespace Server.Services;

public sealed class SocketDispatcher
{
    private readonly Dictionary<string, IMessageHandler> _handlers;

    public SocketDispatcher(IEnumerable<IMessageHandler> handlers)
    {
        _handlers = handlers.ToDictionary(h => h.MessageType, StringComparer.OrdinalIgnoreCase);
    }

    public async Task DispatchAsync(
        ClientConnection connection,
        string json,
        CancellationToken cancellationToken
    )
    {
        MessageEnvelope? envelope;

        try
        {
            using var doc = JsonDocument.Parse(json);

            /*
            Console.WriteLine("Incoming JSON:");
            Console.WriteLine(doc.RootElement);

            foreach (var property in doc.RootElement.EnumerateObject())
            {
                Console.WriteLine($"Property: '{property.Name}' = {property.Value}");
            }
            */

            envelope = JsonSerializer.Deserialize<MessageEnvelope>(json, JsonOptions.Default);

            // Console.WriteLine($"Envelope.Type = '{envelope?.Type}'");
        }
        catch (JsonException)
        {
            Console.WriteLine($"Invalid JSON: {json}");
            return;
        }

        if (envelope is null || string.IsNullOrWhiteSpace(envelope.Type))
        {
            Console.WriteLine("Missing message type.");
            return;
        }

        if (!_handlers.TryGetValue(envelope.Type, out var handler))
        {
            Console.WriteLine($"Unknown message type '{envelope.Type}'.");

            return;
        }

        var message = (SocketMessage?)
            JsonSerializer.Deserialize(json, handler.MessageClrType, JsonOptions.Default);
        if (message is null)
        {
            Console.WriteLine($"Unable to deserialize '{envelope.Type}'.");

            return;
        }

        await handler.HandleAsync(connection, message, cancellationToken);
    }
}
