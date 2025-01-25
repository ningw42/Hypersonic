using System.Text.Json.Serialization;

namespace Hypersonic.Client.Models.Responses;

/// <summary>
/// JSON wrapper of the response
/// </summary>
/// <typeparam name="T"></typeparam>
public class JsonResponseWrapper<T>
{
    [JsonPropertyName("subsonic-response")]
    public T SubsonicResponse
    {
        get; set;
    }
}
