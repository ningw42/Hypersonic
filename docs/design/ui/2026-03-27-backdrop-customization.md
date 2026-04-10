# Backdrop Customization — Design Doc

## Context

Hypersonic's `MainWindow.xaml` hardcodes `<MicaBackdrop/>` as the system backdrop. Users have no way to change the window material. Some users prefer Acrylic's more prominent transparency, others want no backdrop at all (especially on VMs or older hardware where Mica/Acrylic is unsupported or distracting).

This design adds a persisted, live-switching backdrop selector following the same architecture as the existing theme selector.

---

## Scope

### P0 (this design)

1. **Persisted setting**: User-selectable backdrop material stored via `ILocalSettingsService`.
2. **4 options**: None, Mica, Mica Alt, Acrylic.
3. **Live switching**: Changing the setting applies the backdrop immediately, no restart required.
4. **Settings UI**: RadioButton group in the Personalization section of `SettingsPage`, between the theme selector and the About section.
5. **Default**: `Mica` — preserves current behavior for existing users.

### Deferred

**Queue popup and search results popup.** Both are `Popup` controls in `ShellPage.xaml` with `Border` backgrounds using `SolidBackgroundFillColorBaseBrush`. WinUI 3 `Popup` does not support `SystemBackdrop`. Applying acrylic to popups would require either attaching a `DesktopAcrylicController` to the popup's composition visual or replacing the `Popup` with a windowed flyout — both are significant additional complexity with limited visual payoff. The solid background renders correctly across all backdrop choices (when the window backdrop is `None`, the window is solid and popups match; when the backdrop is `Mica` or `Acrylic`, the popups overlay with a solid brush, which is visually consistent). Deferred to a future iteration.

---

## Part 1: New Types

### BackdropType Enum

New file: `Hypersonic/Models/BackdropType.cs`

```csharp
namespace Hypersonic.Models;

public enum BackdropType
{
    None,
    Mica,
    MicaAlt,
    Acrylic,
}
```

Follows the `RepeatMode.cs` convention: file-scoped namespace, simple enum, trailing comma.

### Enum-to-SystemBackdrop Mapping

| `BackdropType` | `Window.SystemBackdrop` value |
|---|---|
| `None` | `null` (solid window background, no material) |
| `Mica` | `new MicaBackdrop()` |
| `MicaAlt` | `new MicaBackdrop { Kind = MicaKind.BaseAlt }` |
| `Acrylic` | `new DesktopAcrylicBackdrop()` |

Reference: WinUI Gallery's `SampleBuiltInSystemBackdropsWindow.xaml.cs` uses this exact pattern — setting `Window.SystemBackdrop` directly with built-in backdrop objects. This is the recommended simple approach over the controller-based pattern, which provides more customization but adds complexity we don't need.

---

## Part 2: Service Layer

### IBackdropSelectorService Interface

New file: `Hypersonic/Contracts/Services/IBackdropSelectorService.cs`

Mirrors `IThemeSelectorService` exactly:

```csharp
public interface IBackdropSelectorService
{
    BackdropType Backdrop { get; }
    Task InitializeAsync();
    Task SetBackdropAsync(BackdropType backdrop);
    Task SetRequestedBackdropAsync();
}
```

| Method | Purpose |
|---|---|
| `Backdrop` | Read-only accessor for current selection |
| `InitializeAsync()` | Load persisted value from settings (called during app activation) |
| `SetBackdropAsync(backdrop)` | Update property, apply to window, persist to settings |
| `SetRequestedBackdropAsync()` | Apply current value to the window (called at startup after window has content) |

### BackdropSelectorService Implementation

New file: `Hypersonic/Services/BackdropSelectorService.cs`

Mirrors `ThemeSelectorService` structurally:

- **Settings key**: `"AppRequestedBackdrop"` (distinct from theme key `"AppBackgroundRequestedTheme"`).
- **Default**: `BackdropType.Mica`.
- **Persistence**: Stored as string enum name via `ILocalSettingsService.SaveSettingAsync<string>()`, parsed back with `Enum.TryParse`. Invalid or case-mismatched saved values will fall back to `Mica` (the `Enum.TryParse` call returns `false`, and the load method returns the default).
- **Apply logic**: `SetRequestedBackdropAsync()` maps the enum to the corresponding `SystemBackdrop` object and assigns it to `App.MainWindow.SystemBackdrop`. When backdrop is `None`, it also sets an explicit solid background on the root page (black for dark theme, white for light) because the unpainted window surface is always white regardless of theme. When switching to a material backdrop, it clears the solid background so the material shows through.

```csharp
public async Task SetRequestedBackdropAsync()
{
    if (Backdrop == BackdropType.None)
    {
        App.MainWindow.SystemBackdrop = null;

        // With no backdrop, the window surface is unpainted (white by default).
        // Set a solid background matching the current theme.
        if (App.MainWindow.Content is Page rootPage)
        {
            var isDark = rootPage.ActualTheme == ElementTheme.Dark;
            rootPage.Background = new SolidColorBrush(isDark ? Colors.Black : Colors.White);
        }
    }
    else
    {
        App.MainWindow.SystemBackdrop = Backdrop switch
        {
            BackdropType.Mica => new MicaBackdrop(),
            BackdropType.MicaAlt => new MicaBackdrop { Kind = MicaKind.BaseAlt },
            BackdropType.Acrylic => new DesktopAcrylicBackdrop(),
            _ => new MicaBackdrop(),
        };

        // Clear any solid background so the material backdrop shows through.
        if (App.MainWindow.Content is Page rootPage)
        {
            rootPage.Background = null;
        }
    }

    await Task.CompletedTask;
}
```

**Why explicit colors instead of theme resource lookup:** `Application.Current.Resources["ApplicationPageBackgroundThemeBrush"]` resolves at the app level and does not respect the element-level `RequestedTheme` override — it always returns the system-default theme color. Using `rootPage.ActualTheme` to pick `Colors.Black` or `Colors.White` ensures correctness.

### DI Registration

In `App.xaml.cs`, register as singleton immediately after `IThemeSelectorService`:

```csharp
services.AddSingleton<IBackdropSelectorService, BackdropSelectorService>();
```

Singleton because the backdrop selection is global app state, same as theme.

---

## Part 3: Activation Flow Integration

File: `Hypersonic/Services/ActivationService.cs`

### Changes

1. Add `IBackdropSelectorService` as a constructor dependency (field + parameter).
2. In `InitializeAsync()`, load the persisted backdrop value:
   ```csharp
   await _backdropSelectorService.InitializeAsync().ConfigureAwait(false);
   ```
3. In `StartupAsync()`, apply the backdrop to the window:
   ```csharp
   await _backdropSelectorService.SetRequestedBackdropAsync();
   ```

### Ordering

The activation sequence is:

```
InitializeAsync()          ← loads theme + backdrop from settings
  ↓
Set window content         ← ShellPage or ConnectPage
  ↓
App.MainWindow.Activate()  ← window becomes visible
  ↓
StartupAsync()             ← applies theme + backdrop
```

`SetRequestedBackdropAsync()` runs after `Activate()`, matching the theme service pattern. `SystemBackdrop` can be set at any time and takes effect immediately — there may be a single frame with default appearance between activation and `StartupAsync`, identical to the existing theme behavior, which is acceptable.

### Theme–backdrop cross-concern

When backdrop is `None`, the solid background color depends on the current theme. If the user changes the theme while backdrop is `None`, the background must be refreshed. To handle this, `ThemeSelectorService.SetRequestedThemeAsync()` calls `BackdropSelectorService.SetRequestedBackdropAsync()` after applying the theme. This is safe because both are singletons resolved via `App.GetService<T>()`, and the call is cheap when backdrop is not `None` (it creates a new backdrop instance of the same type and reassigns it, which WinUI handles as a no-op visually).

---

## Part 4: MainWindow Change

File: `Hypersonic/MainWindow.xaml`

Remove the hardcoded backdrop:

```xml
<!-- REMOVE these 3 lines -->
<Window.SystemBackdrop>
    <MicaBackdrop/>
</Window.SystemBackdrop>
```

The backdrop is now set programmatically by `BackdropSelectorService.SetRequestedBackdropAsync()` during activation. With the default setting (`Mica`), behavior is identical to the current hardcoded state.

---

## Part 5: EnumToBooleanConverter Generalization (Mandatory)

File: `Hypersonic/Helpers/EnumToBooleanConverter.cs`

This change is a **prerequisite** for the backdrop RadioButtons — the converter must be generalized before adding them.

### Problem

The converter is currently hardcoded to `typeof(ElementTheme)`:

```csharp
// Current
if (!Enum.IsDefined(typeof(ElementTheme), value))
var enumValue = Enum.Parse(typeof(ElementTheme), enumString);
```

This prevents reuse with `BackdropType` RadioButtons.

### Fix

Make it generic by deriving the enum type from the runtime value:

```csharp
// Convert: use value.GetType()
if (!Enum.IsDefined(value.GetType(), value))
var enumValue = Enum.Parse(value.GetType(), enumString);

// ConvertBack: use targetType
return Enum.Parse(targetType, enumString);
```

This is fully backward compatible — existing theme RadioButtons work unchanged because `value.GetType()` returns `ElementTheme` when the bound property is `ElementTheme`. For the new backdrop RadioButtons, it returns `BackdropType`.

The `ConvertBack` change (using `targetType` instead of `typeof(ElementTheme)`) is strictly for correctness — in the current codebase, `ConvertBack` is never invoked because the RadioButtons use `Mode=OneWay` for `IsChecked` and a separate `Command` + `CommandParameter` for the write path. But making it correct costs nothing.

---

## Part 6: SettingsViewModel Changes

File: `Hypersonic/ViewModels/SettingsViewModel.cs`

### New dependencies and properties

```csharp
private readonly IBackdropSelectorService _backdropSelectorService;

[ObservableProperty]
private BackdropType _backdropType;

public ICommand SwitchBackdropCommand { get; }
```

### Constructor signature

```csharp
public SettingsViewModel(IThemeSelectorService themeSelectorService, IBackdropSelectorService backdropSelectorService)
```

### Constructor body changes

```csharp
_backdropSelectorService = backdropSelectorService;
_backdropType = _backdropSelectorService.Backdrop;

SwitchBackdropCommand = new RelayCommand<int>(
    async (param) =>
    {
        var backdrop = (BackdropType)param;
        if (BackdropType != backdrop)
        {
            BackdropType = backdrop;
            await _backdropSelectorService.SetBackdropAsync(backdrop);
        }
    });
```

**Why `RelayCommand<int>` instead of `RelayCommand<BackdropType>`:** WinUI 3 XAML marshals custom CLR enum `CommandParameter` values as their underlying `int` type. WinRT-projected enums like `ElementTheme` retain their type identity, but user-defined enums do not. Using `RelayCommand<BackdropType>` causes an `ArgumentException` at binding time because the parameter arrives as `System.Int32`.

---

## Part 7: Settings Page UI

File: `Hypersonic/Views/SettingsPage.xaml`

### XAML namespace addition

```xml
xmlns:models="using:Hypersonic.Models"
```

### New section

Inserted between the theme `</StackPanel>` (after line 52) and the About `<TextBlock>` (current line 54):

```xml
<StackPanel Margin="{StaticResource SmallTopBottomMargin}">
    <TextBlock x:Uid="Settings_Backdrop" />

    <StackPanel Margin="{StaticResource XSmallTopMargin}">
        <RadioButton
            x:Uid="Settings_Backdrop_Mica"
            Command="{x:Bind ViewModel.SwitchBackdropCommand}"
            IsChecked="{x:Bind ViewModel.BackdropType, Converter={StaticResource EnumToBooleanConverter}, ConverterParameter=Mica, Mode=OneWay}"
            FontSize="15"
            GroupName="AppBackdrop">
            <RadioButton.CommandParameter>
                <models:BackdropType>Mica</models:BackdropType>
            </RadioButton.CommandParameter>
        </RadioButton>
        <RadioButton
            x:Uid="Settings_Backdrop_MicaAlt"
            Command="{x:Bind ViewModel.SwitchBackdropCommand}"
            IsChecked="{x:Bind ViewModel.BackdropType, Converter={StaticResource EnumToBooleanConverter}, ConverterParameter=MicaAlt, Mode=OneWay}"
            FontSize="15"
            GroupName="AppBackdrop">
            <RadioButton.CommandParameter>
                <models:BackdropType>MicaAlt</models:BackdropType>
            </RadioButton.CommandParameter>
        </RadioButton>
        <RadioButton
            x:Uid="Settings_Backdrop_Acrylic"
            Command="{x:Bind ViewModel.SwitchBackdropCommand}"
            IsChecked="{x:Bind ViewModel.BackdropType, Converter={StaticResource EnumToBooleanConverter}, ConverterParameter=Acrylic, Mode=OneWay}"
            FontSize="15"
            GroupName="AppBackdrop">
            <RadioButton.CommandParameter>
                <models:BackdropType>Acrylic</models:BackdropType>
            </RadioButton.CommandParameter>
        </RadioButton>
        <RadioButton
            x:Uid="Settings_Backdrop_None"
            Command="{x:Bind ViewModel.SwitchBackdropCommand}"
            IsChecked="{x:Bind ViewModel.BackdropType, Converter={StaticResource EnumToBooleanConverter}, ConverterParameter=None, Mode=OneWay}"
            FontSize="15"
            GroupName="AppBackdrop">
            <RadioButton.CommandParameter>
                <models:BackdropType>None</models:BackdropType>
            </RadioButton.CommandParameter>
        </RadioButton>
    </StackPanel>
</StackPanel>
```

**`GroupName="AppBackdrop"`** is distinct from `"AppTheme"` so the two RadioButton groups do not interfere.

### Localization strings

File: `Hypersonic/Strings/en-us/Resources.resw`

| Key | Value |
|---|---|
| `Settings_Backdrop.Text` | Backdrop material |
| `Settings_Backdrop_Mica.Content` | Mica |
| `Settings_Backdrop_MicaAlt.Content` | Mica Alt |
| `Settings_Backdrop_Acrylic.Content` | Acrylic |
| `Settings_Backdrop_None.Content` | None |

Inserted after the existing `Settings_Theme_Default.Content` entry and before `Settings_About.Text`.

---

## Edge Cases

| Case | Behavior |
|---|---|
| **Fresh install (no persisted setting)** | Default is `Mica` — identical to current hardcoded behavior. No visual change for existing users. |
| **Unsupported hardware (VMs, older Windows)** | WinUI 3 silently falls back to a solid color when Mica/Acrylic is unsupported. The setting is still selectable; the visual effect simply doesn't render. No crash, no error. |
| **Theme + backdrop independence** | Theme and backdrop are orthogonal — Dark+Acrylic, Light+MicaAlt, etc. all work correctly because `SystemBackdrop` and `RequestedTheme` are independent properties. When backdrop is `None`, `ThemeSelectorService` calls `SetRequestedBackdropAsync()` after theme changes to refresh the solid background color. |
| **Backdrop changed on ConnectPage** | The setting is applied in `StartupAsync()` regardless of which page is displayed. If the user previously changed the backdrop and is now on ConnectPage (failed credential), the backdrop still applies. However, the settings UI is only accessible from `SettingsPage` (inside `ShellPage`), so the user cannot change it from ConnectPage — only the previously persisted value applies. |

---

## Files Summary

### New files

| File | Purpose |
|---|---|
| `Hypersonic/Models/BackdropType.cs` | Enum: `None`, `Mica`, `MicaAlt`, `Acrylic` |
| `Hypersonic/Contracts/Services/IBackdropSelectorService.cs` | Service interface |
| `Hypersonic/Services/BackdropSelectorService.cs` | Service implementation |

### Modified files

| File | Changes |
|---|---|
| `Hypersonic/MainWindow.xaml` | Remove hardcoded `<MicaBackdrop/>` |
| `Hypersonic/Helpers/EnumToBooleanConverter.cs` | Generalize from `ElementTheme` to any enum |
| `Hypersonic/App.xaml.cs` | Register `IBackdropSelectorService` singleton |
| `Hypersonic/Services/ActivationService.cs` | Inject and wire backdrop service into init/startup |
| `Hypersonic/Services/ThemeSelectorService.cs` | Call `SetRequestedBackdropAsync()` after theme changes (for `None` background refresh) |
| `Hypersonic/ViewModels/SettingsViewModel.cs` | Add `BackdropType` property and `SwitchBackdropCommand` |
| `Hypersonic/Views/SettingsPage.xaml` | Add backdrop RadioButton group |
| `Hypersonic/Strings/en-us/Resources.resw` | Add 5 localization entries |

---

## Implementation Sequence

1. `BackdropType` enum
2. `IBackdropSelectorService` interface
3. `BackdropSelectorService` implementation
4. `EnumToBooleanConverter` generalization
5. `App.xaml.cs` DI registration
6. `MainWindow.xaml` remove hardcoded backdrop
7. `ActivationService.cs` integration
8. `SettingsViewModel.cs` property + command
9. `SettingsPage.xaml` RadioButton group + namespace
10. `Resources.resw` localization strings
11. Build + verify

---

## Verification

### Build

```bash
dotnet build Hypersonic.sln -p:Platform=x64
```

### Existing tests (no regressions)

```bash
dotnet test Hypersonic.Tests.MSTest/Hypersonic.Tests.MSTest.csproj
dotnet test Hypersonic.Client.UnitTest/Hypersonic.Client.UnitTest.csproj
```

### Manual smoke tests

- Fresh launch (no persisted setting) → window has Mica backdrop (default, identical to before)
- Navigate to Settings → Personalization section shows Theme (3 options) and Backdrop material (4 options)
- Mica is pre-selected on first visit
- Switch to each option → backdrop changes immediately:
  - **Mica**: Translucent, muted desktop bleed-through
  - **Mica Alt**: Slightly different tint variant
  - **Acrylic**: More prominent blur and transparency
  - **None**: Solid, opaque window background
- Close and reopen app → selected backdrop is restored
- Change both theme and backdrop → they work independently (e.g., Dark + Acrylic)
- Set backdrop to None + theme to Dark → solid black background, all controls visible with correct dark theme colors
- Set backdrop to None + theme to Light → solid white background, all controls visible with correct light theme colors
- Switch theme while backdrop is None → background color updates immediately
- Open search popup (Ctrl+K, type a query) and queue popup → both render correctly with solid backgrounds across all backdrop choices
- On a VM or unsupported system → setting is selectable, no crash; visual effect may not be visible (expected graceful fallback)
