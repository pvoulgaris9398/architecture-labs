using Grpc.Core;
using Grpc.Net.Client;
using Realtime.Grpc;

var address = args.ElementAtOrDefault(0) ?? "http://127.0.0.1:5003";
var afterId = long.TryParse(args.ElementAtOrDefault(1), out var parsed) ? parsed : 0;
using var channel = GrpcChannel.ForAddress(address);
var client = new RealtimeTransport.RealtimeTransportClient(channel);
using var call = client.Subscribe(new SubscribeRequest { AfterId = afterId });

Console.WriteLine($"Streaming events after id {afterId} from {address}. Press Ctrl+C to stop.");
await foreach (var item in call.ResponseStream.ReadAllAsync())
{
    Console.WriteLine($"{item.Id}\t{item.CreatedAtUtc}\t{item.Message}");
}
