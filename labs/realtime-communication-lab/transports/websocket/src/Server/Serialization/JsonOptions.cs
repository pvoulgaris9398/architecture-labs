using System.Text.Json;

namespace Server.Serialization;

public static class JsonOptions
{
    public static readonly JsonSerializerOptions Default = new()
    {
        PropertyNameCaseInsensitive = true,
    };
}
