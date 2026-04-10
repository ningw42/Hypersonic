using Hypersonic.Core.Models;

namespace Hypersonic.Contracts.Services;

public interface IBackdropSelectorService
{
    BackdropType Backdrop
    {
        get;
    }

    Task InitializeAsync();

    Task SetBackdropAsync(BackdropType backdrop);

    Task SetRequestedBackdropAsync();
}
