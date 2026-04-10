using Hypersonic.Client;
using Hypersonic.Client.Models.Responses;
using Hypersonic.Contracts.Services;
using Hypersonic.Core.Contracts.Services;
using Hypersonic.Core.Models;

namespace Hypersonic.Services;

public class SubsonicClientService : ISubsonicClientService
{
    private const string ClientVersion = "1.16.1";
    private const string ClientId = "Hypersonic";
    private const string RestPath = "/rest";

    private readonly ILogger _log;

    public ISubsonicClient? Client { get; private set; }

    public SubsonicClientService(ILoggingService logService)
    {
        _log = logService.GetLogger(nameof(SubsonicClientService));
    }

    public void CreateClient(ServerCredential credential)
    {
        Client = new SubsonicClient(
            new HttpClient(),
            credential.ServerUrl,
            credential.Username,
            credential.Password,
            ClientVersion,
            ClientId);

        _log.Info("Client created", new { credential.ServerUrl, credential.Username });
    }

    public async Task<ConnectionTestResult> TestConnectionAsync(ServerCredential credential)
    {
        var urls = ResolveServerUrls(credential.ServerUrl);
        _log.Info("Testing connection", new { Input = credential.ServerUrl, CandidateUrls = urls });

        foreach (var url in urls)
        {
            try
            {
                _log.Debug("Pinging server", new { Url = url });

                var client = new SubsonicClient(
                    new HttpClient(),
                    url,
                    credential.Username,
                    credential.Password,
                    ClientVersion,
                    ClientId);

                var response = await client.PingAsync();

                _log.Info("Connection test succeeded", new { Url = url, response.Status });
                return new ConnectionTestResult(response, url);
            }
            catch (HttpRequestException ex) when (urls.Length > 1 && url != urls[^1])
            {
                // If there are multiple URLs to try and this isn't the last one,
                // swallow HTTP errors and try the next URL.
                _log.Warning("Connection attempt failed, trying next URL", new { Url = url }, ex);
                continue;
            }
            catch (HttpRequestException ex)
            {
                _log.Error("Connection test failed on final URL", new { Url = url }, ex);
                throw;
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
