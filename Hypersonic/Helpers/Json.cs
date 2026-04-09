using System.Text.Json;
using System.Text.Json.Serialization;

namespace Hypersonic.Helpers;

public static class Json
{
    private static readonly JsonSerializerOptions _options = new()
    {
        PropertyNameCaseInsensitive = true,
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() },
    };

    public static readonly JsonSerializerOptions CompactOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        WriteIndented = false,
        Converters = { new JsonStringEnumConverter() },
    };

    public static async Task<T?> ToObjectAsync<T>(string value)
    {
        return await Task.Run(() => JsonSerializer.Deserialize<T>(value, _options));
    }

    public static async Task<string> StringifyAsync(object value)
    {
        return await Task.Run(() => JsonSerializer.Serialize(value, _options));
    }

    public static JsonSerializerOptions Options => _options;
}
