using Hypersonic.Client;
using Hypersonic.Client.Models.Responses;
using Hypersonic.Contracts.Services;
using Hypersonic.Models;

namespace Hypersonic.Services;

public class SubsonicClientService : ISubsonicClientService
{
    private const string ClientVersion = "1.16.1";
    private const string ClientId = "Hypersonic";
    private const string RestPath = "/rest";

    public ISubsonicClient? Client { get; private set; }

    public void CreateClient(ServerCredential credential)
    {
        Client = new SubsonicClient(
            new HttpClient(),
            credential.ServerUrl,
            credential.Username,
            credential.Password,
            ClientVersion,
            ClientId);
    }

    public async Task<ConnectionTestResult> TestConnectionAsync(ServerCredential credential)
    {
        var urls = ResolveServerUrls(credential.ServerUrl);

        foreach (var url in urls)
        {
            try
            {
                var client = new SubsonicClient(
                    new HttpClient(),
                    url,
                    credential.Username,
                    credential.Password,
                    ClientVersion,
                    ClientId);

                var response = await client.PingAsync();
                return new ConnectionTestResult(response, url);
            }
            catch (HttpRequestException) when (urls.Length > 1 && url != urls[^1])
            {
                // If there are multiple URLs to try and this isn't the last one,
                // swallow HTTP errors and try the next URL.
                continue;
            }
        }

        // Unreachable in practice — the last URL attempt either returns or throws.
        throw new HttpRequestException("Could not connect to any resolved URL.");
    }

    /// <summary>
    /// Resolves user input into one or more candidate base URLs.
    /// <list type="bullet">
    ///   <item>Input has a path component (e.g. "host.com/rest", "host.com/custom") → use as-is.</item>
    ///   <item>Input is host-only (e.g. "example.com", "https://example.com") → try host/rest first, then host.</item>
    /// </list>
    /// Always ensures an https:// scheme if none is provided.
    /// </summary>
    public static string[] ResolveServerUrls(string input)
    {
        input = input.Trim().TrimEnd('/');

        // Ensure scheme
        if (!input.StartsWith("http://", StringComparison.OrdinalIgnoreCase) &&
            !input.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
        {
            input = "https://" + input;
        }

        var uri = new Uri(input);

        // If path is empty or just "/", it's host-only → try with /rest first, then without
        if (string.IsNullOrEmpty(uri.AbsolutePath) || uri.AbsolutePath == "/")
        {
            var baseUrl = uri.GetLeftPart(UriPartial.Authority).TrimEnd('/');
            return [baseUrl + RestPath, baseUrl];
        }

        // User specified a path — use as-is
        return [input.TrimEnd('/')];
    }
}
