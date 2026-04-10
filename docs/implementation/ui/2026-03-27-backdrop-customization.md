# Backdrop Customization — Implementation Details

Companion to [docs/design/ui/backdrop-customization.md](../../design/ui/backdrop-customization.md). This document records what was actually built and implementation specifics not covered in the design doc.

---

## Files Changed

### New files

| File | Purpose |
|---|---|
| `Hypersonic/Models/BackdropType.cs` | Enum: `None`, `Mica`, `MicaAlt`, `Acrylic` |
| `Hypersonic/Contracts/Services/IBackdropSelectorService.cs` | Service interface |
| `Hypersonic/Services/BackdropSelectorService.cs` | Service implementation with settings persistence and backdrop application |

### Modified files

| File | Changes |
|---|---|
| `Hypersonic/MainWindow.xaml` | Removed hardcoded `<Window.SystemBackdrop><MicaBackdrop/></Window.SystemBackdrop>` |
| `Hypersonic/Helpers/EnumToBooleanConverter.cs` | Generalized from `typeof(ElementTheme)` to `value.GetType()` / `targetType`; removed `using Microsoft.UI.Xaml` |
| `Hypersonic/App.xaml.cs` | Added `IBackdropSelectorService` singleton registration |
| `Hypersonic/Services/ActivationService.cs` | Added `IBackdropSelectorService` field + constructor parameter; added calls in `InitializeAsync()` and `StartupAsync()` |
| `Hypersonic/Services/ThemeSelectorService.cs` | `SetRequestedThemeAsync()` now calls `IBackdropSelectorService.SetRequestedBackdropAsync()` after applying theme |
| `Hypersonic/ViewModels/SettingsViewModel.cs` | Added `IBackdropSelectorService` dependency, `BackdropType` observable property, `SwitchBackdropCommand` |
| `Hypersonic/Views/SettingsPage.xaml` | Added `xmlns:models`, backdrop RadioButton group (4 buttons, `GroupName="AppBackdrop"`) |
| `Hypersonic/Strings/en-us/Resources.resw` | Added 5 entries: `Settings_Backdrop.Text`, `Settings_Backdrop_Mica.Content`, `Settings_Backdrop_MicaAlt.Content`, `Settings_Backdrop_Acrylic.Content`, `Settings_Backdrop_None.Content` |

---

## WinUI 3 Pitfalls Encountered

### 1. Custom enum CommandParameter marshalled as int

WinUI 3 XAML marshals custom CLR enum values in `CommandParameter` as their underlying `System.Int32` type. WinRT-projected enums (e.g., `ElementTheme`) retain their type identity, but user-defined enums do not.

**Symptom:** `RelayCommand<BackdropType>` throws `ArgumentException` at binding time: "Parameter cannot be of type System.Int32, as the command type requires BackdropType".

**Fix:** Use `RelayCommand<int>` and cast inside the handler:

```csharp
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

This does not affect `SwitchThemeCommand` because `ElementTheme` is a WinRT-projected enum.

### 2. Window surface is unpainted when SystemBackdrop is null

Setting `Window.SystemBackdrop = null` removes the backdrop material but does **not** set the window surface to a theme-appropriate color. The Win32 window surface underneath remains white regardless of `RequestedTheme`. This causes dark-themed content to render on a white window — controls using transparent or semi-transparent backgrounds become invisible or washed out.

**Fix:** When backdrop is `None`, explicitly set a `SolidColorBrush` on the root page's `Background` matching the current theme:

```csharp
if (App.MainWindow.Content is Page rootPage)
{
    var isDark = rootPage.ActualTheme == ElementTheme.Dark;
    rootPage.Background = new SolidColorBrush(isDark ? Colors.Black : Colors.White);
}
```

When switching back to a material backdrop, `rootPage.Background` is set to `null` so the material shows through.

### 3. Theme resource lookup ignores element-level RequestedTheme

`Application.Current.Resources["ApplicationPageBackgroundThemeBrush"]` resolves at the application level and does **not** respect `RequestedTheme` set on the root `FrameworkElement`. If the OS theme is Light but the app overrides to Dark, the resource still returns the Light color.

**Fix:** Use `rootPage.ActualTheme` to determine the effective theme and select explicit `Colors.Black` / `Colors.White` rather than relying on theme resource lookup from code-behind.

### 4. FrameworkElement does not have Background

`FrameworkElement` does not define `Background` — it lives on `Control` (and therefore `Page`). The root content (`ShellPage` or `ConnectPage`) is always a `Page`, so the cast is `App.MainWindow.Content is Page`.

---

## Theme–Backdrop Cross-Concern

When backdrop is `None`, the solid background color depends on the current theme. Changing the theme while in `None` mode must refresh the background. Rather than introducing an event or callback, `ThemeSelectorService.SetRequestedThemeAsync()` calls `BackdropSelectorService.SetRequestedBackdropAsync()` after applying the theme:

```csharp
// ThemeSelectorService.SetRequestedThemeAsync()
await App.GetService<IBackdropSelectorService>().SetRequestedBackdropAsync();
```

This resolves `IBackdropSelectorService` via `App.GetService<T>()` rather than constructor injection to keep `ThemeSelectorService` decoupled from backdrop implementation details — it only needs to trigger a refresh, not hold a reference to the backdrop service. When backdrop is not `None`, the call creates a new backdrop instance of the same type and reassigns it — WinUI handles this as a visual no-op.

### Call chain: theme change while backdrop is None

```
User clicks Dark RadioButton
  → SettingsViewModel.SwitchThemeCommand
    → ThemeSelectorService.SetThemeAsync(Dark)
      → SetRequestedThemeAsync()
        → rootElement.RequestedTheme = Dark
        → TitleBarHelper.UpdateTitleBar(Dark)
        → BackdropSelectorService.SetRequestedBackdropAsync()
          → rootPage.ActualTheme is now Dark
          → rootPage.Background = new SolidColorBrush(Colors.Black)
      → SaveThemeInSettingsAsync("Dark")
```

### Call chain: backdrop change

```
User clicks Acrylic RadioButton
  → SettingsViewModel.SwitchBackdropCommand (receives int, casts to BackdropType)
    → BackdropSelectorService.SetBackdropAsync(Acrylic)
      → SetRequestedBackdropAsync()
        → App.MainWindow.SystemBackdrop = new DesktopAcrylicBackdrop()
        → rootPage.Background = null
      → SaveBackdropInSettingsAsync("Acrylic")
```

---

## Startup Sequence

```
App.OnLaunched
  → ActivationService.ActivateAsync
    → InitializeAsync
      → ThemeSelectorService.InitializeAsync    ← loads "AppBackgroundRequestedTheme"
      → BackdropSelectorService.InitializeAsync  ← loads "AppRequestedBackdrop"
    → Set window content (ShellPage or ConnectPage)
    → App.MainWindow.Activate
    → StartupAsync
      → ThemeSelectorService.SetRequestedThemeAsync
        → applies theme to root element
        → calls BackdropSelectorService.SetRequestedBackdropAsync  ← applies backdrop
      → BackdropSelectorService.SetRequestedBackdropAsync  ← redundant but harmless
```

Note: `SetRequestedBackdropAsync` is called twice during startup — once from `ThemeSelectorService` and once directly from `StartupAsync`. The second call is redundant but harmless (creates a new backdrop instance of the same type). This is acceptable for simplicity; eliminating it would require the `StartupAsync` method to know about the theme service's internal behavior.

---

## Settings Persistence

| Key | Type | Default | Example stored value |
|---|---|---|---|
| `AppRequestedBackdrop` | `string` | `"Mica"` (implicit — no entry means Mica) | `"Acrylic"`, `"MicaAlt"`, `"None"` |

Stored as the enum member name via `backdrop.ToString()`. Loaded via `Enum.TryParse<BackdropType>()` — invalid or case-mismatched values fall back to `BackdropType.Mica`.

---

## EnumToBooleanConverter Generalization

The converter was hardcoded to `typeof(ElementTheme)`. Both `Convert` and `ConvertBack` were updated:

| Method | Before | After |
|---|---|---|
| `Convert` | `Enum.IsDefined(typeof(ElementTheme), value)` | `Enum.IsDefined(value.GetType(), value)` |
| `Convert` | `Enum.Parse(typeof(ElementTheme), enumString)` | `Enum.Parse(value.GetType(), enumString)` |
| `ConvertBack` | `Enum.Parse(typeof(ElementTheme), enumString)` | `Enum.Parse(targetType, enumString)` |

The `using Microsoft.UI.Xaml` directive was removed as `ElementTheme` is no longer referenced.

`ConvertBack` is not invoked in the current codebase (RadioButtons use `Mode=OneWay` for `IsChecked` and a separate `Command` + `CommandParameter` for writes), but was corrected for completeness.
