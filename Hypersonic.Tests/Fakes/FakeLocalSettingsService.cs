using Hypersonic.Core.Contracts.Services;

namespace Hypersonic.Tests.Fakes;

/// <summary>In-memory ILocalSettingsService for tests. Stores the last value per key verbatim.</summary>
public sealed class FakeLocalSettingsService : ILocalSettingsService
{
    public Dictionary<string, object?> Store { get; } = [];
    public int WriteCount { get; private set; }

    public T? GetAs<T>(string key)
        => Store.TryGetValue(key, out var v) && v is not null ? (T)v : default;

    public Task<T?> ReadSettingAsync<T>(string key)
        => Task.FromResult(Store.TryGetValue(key, out var v) && v is not null ? (T?)v : default);

    public Task SaveSettingAsync<T>(string key, T value)
    {
        Store[key] = value;
        WriteCount++;
        return Task.CompletedTask;
    }

    public Task RemoveSettingAsync(string key)
    {
        Store.Remove(key);
        return Task.CompletedTask;
    }
}
