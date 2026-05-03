namespace Hypersonic.Contracts.Services;

public interface ITrayIconService : IDisposable
{
    void Initialize();
    void RefreshVisibility();
    void RequestExit();
    void ShowMainWindow();
}
