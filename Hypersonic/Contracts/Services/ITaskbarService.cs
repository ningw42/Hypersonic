namespace Hypersonic.Contracts.Services;

public interface ITaskbarService : IDisposable
{
    void Initialize(IntPtr hwnd);
    void OnSystemThemeChanged();
}
