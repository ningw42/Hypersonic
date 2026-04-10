using Hypersonic.Client;
using Hypersonic.Client.Models.Responses;
using Hypersonic.Core.Models;

namespace Hypersonic.Contracts.Services;

public record ConnectionTestResult(SubsonicResponse Response, string ResolvedUrl);

public interface ISubsonicClientService
{
    ISubsonicClient? Client { get; }

    void CreateClient(ServerCredential credential);

    Task<ConnectionTestResult> TestConnectionAsync(ServerCredential credential);
}
