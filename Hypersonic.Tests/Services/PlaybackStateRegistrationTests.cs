using Hypersonic.Core.Contracts.Services;
using Hypersonic.Tests.Fakes;

using Microsoft.Extensions.DependencyInjection;

namespace Hypersonic.Tests.Services;

[TestClass]
public class PlaybackStateRegistrationTests
{
    [TestMethod]
    public void BothInterfaces_ResolveToTheSameSingleton()
    {
        var services = new ServiceCollection();
        services.AddSingleton<FakePlaybackService>();
        services.AddSingleton<IPlaybackService>(sp => sp.GetRequiredService<FakePlaybackService>());
        services.AddSingleton<IReadOnlyPlaybackState>(sp => sp.GetRequiredService<FakePlaybackService>());
        using var provider = services.BuildServiceProvider();

        var command = provider.GetRequiredService<IPlaybackService>();
        var read = provider.GetRequiredService<IReadOnlyPlaybackState>();

        Assert.AreSame(command, read, "observe and command handles must be one instance");
    }
}
