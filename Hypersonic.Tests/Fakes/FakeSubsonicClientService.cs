using Hypersonic.Client;
using Hypersonic.Contracts.Services;
using Hypersonic.Core.Models;

namespace Hypersonic.Tests.Fakes;

public sealed class FakeSubsonicClientService : ISubsonicClientService
{
    public ISubsonicClient? Client { get; set; }
    public string? ActiveUserIdentity { get; set; }
    public void CreateClient(ServerCredential credential) { }
    public Task<ConnectionTestResult> TestConnectionAsync(ServerCredential credential) =>
        throw new NotImplementedException();
}
