# Core Library Extraction & Test Project Reorganization — Design Spec

## Context

Hypersonic's testable business logic (models, services, helpers) lives inside the WinUI app project (`net10.0-windows`). A plain `net10.0` test project cannot reference it due to TFM mismatch, forcing the current `Hypersonic.UnitTest` to use 22 manually linked `.cs` files — fragile and discouraging new tests.

The root fix is architectural: extract platform-independent code into a `net10.0` class library, enabling proper `ProjectReference` from both the WinUI app and a plain test project.

Additionally, `Hypersonic.Tests.MSTest` is a WinUI test project that can't run `dotnet test` due to a .NET SDK 10.0.201 bug: `Microsoft.NET.Test.Sdk` 18.3.0 skips `OutputType=Exe` for `UseWinUI=true` projects, and the SDK lacks `testhost-10.0.runtimeconfig.json` (dotnet/sdk#53412). The fix is two csproj overrides: `<OutputType>Exe</OutputType>` and `<GenerateProgramFile>true</GenerateProgramFile>`. Verified end-to-end.

---

## Decision

### Extract Hypersonic.Core and create two test projects

| Project | TFM | Purpose |
|---|---|---|
| **Hypersonic.Core** | `net10.0` | Platform-independent models, contracts, services, helpers |
| **Hypersonic** | `net10.0-windows10.0.19041.0` | WinUI app — Views, ViewModels, platform services. References Hypersonic.Core |
| **Hypersonic.Client** | `net10.0` | Subsonic API client (unchanged) |
| **Hypersonic.Core.Tests** | `net10.0` | Tests for Core code. References Hypersonic.Core |
| **Hypersonic.Tests** | `net10.0-windows10.0.19041.0` | Tests for WinUI code. References Hypersonic (Core available transitively) |
| **Hypersonic.Client.UnitTest** | `net10.0` | API client tests (unchanged) |

---

## Hypersonic.Core boundary

### Guiding principle

A file moves to Core if it compiles on plain `net10.0` without any Windows/WinUI types. Files with `#if WINDOWS` conditional compilation stay in Hypersonic — the conditional code paths would be lost in a `net10.0` library.

### Files moving to Hypersonic.Core

**Models/ (15 files — plain data models and enums):**
- `AlbumInfo.cs`, `AppState.cs`, `ArtistInfo.cs`, `BackdropType.cs`, `LocalSettingsOptions.cs`, `LogEntry.cs`, `LogLevel.cs`, `PlaybackTrack.cs`, `PlaylistInfo.cs`, `QueueSource.cs`, `RepeatMode.cs`, `SearchSuggestionItem.cs`, `ServerAccount.cs`, `ServerCredential.cs`, `SuggestionItem.cs`

**Contracts/Services/ (8 interfaces):**
- `IAppStateService.cs`, `ICredentialService.cs`, `IFileService.cs`, `ILocalSettingsService.cs`, `ILogger.cs`, `ILoggingService.cs`, `ILogSink.cs`, `IPlaybackService.cs`

**Helpers/ (2 files):**
- `Json.cs`, `VaultKeyHelper.cs`

**Services/ (5 files):**
- `PlaybackQueue.cs`, `FileService.cs`, `LoggingService.cs`, `Logging/DebugSink.cs`, `Logging/FileSink.cs`

**Total: 30 files**

### Files staying in Hypersonic

**Models/ (3 files — presentation models with MVVM bindings):**
- `DiscHeaderItem.cs`, `QueueItem.cs`, `SongInfo.cs` — these use `[ObservableProperty]` / `ObservableObject` from `CommunityToolkit.Mvvm` because they are bound directly from XAML. They are presentation state, not domain logic, and keeping them in Hypersonic eliminates the `CommunityToolkit.Mvvm` dependency from Core entirely.

**Contracts/Services/ (8 interfaces):**
- `IActivationService.cs`, `IBackdropSelectorService.cs`, `INavigationService.cs`, `INavigationViewService.cs`, `IPageService.cs`, `ISubsonicClientService.cs`, `ITaskbarService.cs`, `IThemeSelectorService.cs`

**Helpers/ (7 files):**
- `EnumToBooleanConverter.cs`, `FrameExtensions.cs`, `NavigationHelper.cs`, `ResourceExtensions.cs`, `RuntimeHelper.cs`, `SettingsStorageExtensions.cs`, `TitleBarHelper.cs`, `TrackListTemplateSelector.cs`

**Services/ (11 files):**
- `ActivationService.cs`, `AppStateService.cs`, `BackdropSelectorService.cs`, `CredentialService.cs`, `LocalSettingsService.cs`, `NavigationService.cs`, `NavigationViewService.cs`, `PageService.cs`, `PlaybackService.cs`, `SubsonicClientService.cs`, `TaskbarService.cs`, `ThemeSelectorService.cs`

Plus all ViewModels, Views, Controls, Activation, Behaviors, Interop, Collections, Converters, Styles, Themes, Strings, Assets.

### Rationale for borderline decisions

- **`ISubsonicClientService` / `SubsonicClientService`**: The interface references `Hypersonic.Client` types and `ServerCredential` (movable model), and the implementation has no WinUI deps. However, `SubsonicClientService` uses `ILogger` from Core and manages the HTTP client lifecycle — it could live in Core. But since it's only consumed by ViewModels in the WinUI project and `ISubsonicClientService` references `ConnectionTestResult` which is defined alongside it, keeping it in Hypersonic avoids pulling `Hypersonic.Client` into Core's dependency chain. The `ResolveServerUrls` static method is testable from `Hypersonic.Tests` via ProjectReference.

- **`LocalSettingsService`**: Uses `#if WINDOWS` for `Windows.Storage` (MSIX path) and `RuntimeHelper.IsMSIX` in constructor. Stays in Hypersonic. Its tests move to `Hypersonic.Tests`.

- **`AppStateService`**: Uses `Windows.Storage` for MSIX path detection. Stays in Hypersonic.

- **`IActivationService`, `IBackdropSelectorService`, `ITaskbarService`, `IPageService`**: Technically no WinUI `using` statements, but they serve exclusively WinUI services and are not useful outside that context. Keeping them in Hypersonic avoids a misleading API surface in Core.

### Future extraction opportunities

Some testable pure logic remains on the WinUI side. These are not blockers for the current change but could be extracted to Core later to increase `net10.0` test coverage:

- **`SubsonicClientService.ResolveServerUrls`**: A pure static URL parser with zero WinUI dependency. Could be extracted to a helper in Core. Currently testable from `Hypersonic.Tests` via ProjectReference.
- **`LocalSettingsService` migration logic**: The `MigrateDoubleSerializedValues` method is pure JSON manipulation. The `#if WINDOWS` code paths are narrow (MSIX storage access); the bulk of the service is plain serialization/persistence.
- **`AppStateService` serialization**: Similar — the Windows-specific part is just the MSIX path detection; the rest is JSON file I/O.

---

## Namespace strategy

Hypersonic.Core uses `Hypersonic.Core` as its root namespace — distinct from Hypersonic's `Hypersonic` namespace. This means:

- Moved files get new namespaces: `Hypersonic.Models.AppState` becomes `Hypersonic.Core.Models.AppState`, `Hypersonic.Services.PlaybackQueue` becomes `Hypersonic.Core.Services.PlaybackQueue`, etc.
- Files staying in Hypersonic that reference moved types add `using Hypersonic.Core.Models;`, `using Hypersonic.Core.Services;`, etc.
- Test files update similarly.

**Why not share `RootNamespace=Hypersonic`?** Shared root namespaces between assemblies are ambiguous — you can't tell from `using Hypersonic.Models` which assembly a type lives in. Separate namespaces make the Core/App boundary visible in code, improve IntelliSense navigation, and follow the standard convention that assembly name matches root namespace.

The namespace changes are mechanical (find-and-replace) and are a one-time cost for a permanently clear boundary.

## Hypersonic.Core.csproj

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
  </PropertyGroup>
  <ItemGroup>
    <InternalsVisibleTo Include="Hypersonic.Core.Tests" />
  </ItemGroup>
</Project>
```

Notes:
- `RootNamespace` defaults to `Hypersonic.Core` (matching the assembly name). Moved files use `namespace Hypersonic.Core.Models`, `namespace Hypersonic.Core.Contracts.Services`, etc.
- No `CommunityToolkit.Mvvm` dependency — presentation models (`DiscHeaderItem`, `QueueItem`, `SongInfo`) that use `[ObservableProperty]` stay in Hypersonic. Core contains only plain data models and enums.
- `InternalsVisibleTo` for `Hypersonic.Core.Tests` — required because `DebugSink.Format` is `internal` and tested by `LoggingServiceTest`.
- No reference to `Hypersonic.Client` — Core stays domain-agnostic.

## Hypersonic.Core.Tests.csproj

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <RootNamespace>Hypersonic.Core.Tests</RootNamespace>
    <LangVersion>latest</LangVersion>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include="Microsoft.NET.Test.Sdk" Version="18.3.0" />
    <PackageReference Include="MSTest" Version="4.1.0" />
    <PackageReference Include="coverlet.collector" Version="8.0.1">
      <PrivateAssets>all</PrivateAssets>
      <IncludeAssets>runtime; build; native; contentfiles; analyzers; buildtransitive</IncludeAssets>
    </PackageReference>
  </ItemGroup>
  <ItemGroup>
    <Using Include="Microsoft.VisualStudio.TestTools.UnitTesting" />
  </ItemGroup>
  <ItemGroup>
    <ProjectReference Include="..\Hypersonic.Core\Hypersonic.Core.csproj" />
  </ItemGroup>
</Project>
```

## Hypersonic.Tests.csproj

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net10.0-windows10.0.19041.0</TargetFramework>
    <RootNamespace>Hypersonic.Tests</RootNamespace>
    <Platforms>x86;x64;arm64</Platforms>
    <IsPackable>false</IsPackable>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
    <UseWinUI>true</UseWinUI>
    <WindowsAppSDKSelfContained>true</WindowsAppSDKSelfContained>
    <ProjectPriFileName>resources.pri</ProjectPriFileName>
    <!-- Override Microsoft.NET.Test.Sdk WinUI exemptions (it skips OutputType=Exe
         and GenerateProgramFile for UseWinUI projects, breaking testhost on .NET 10).
         Can be removed once dotnet/sdk#53412 is fixed. -->
    <OutputType>Exe</OutputType>
    <GenerateProgramFile>true</GenerateProgramFile>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include="Microsoft.NET.Test.Sdk" Version="18.3.0" />
    <PackageReference Include="MSTest" Version="4.1.0" />
    <PackageReference Include="coverlet.collector" Version="8.0.1">
      <PrivateAssets>all</PrivateAssets>
      <IncludeAssets>runtime; build; native; contentfiles; analyzers; buildtransitive</IncludeAssets>
    </PackageReference>
  </ItemGroup>
  <ItemGroup>
    <Using Include="Microsoft.VisualStudio.TestTools.UnitTesting" />
    <Using Include="Microsoft.VisualStudio.TestTools.UnitTesting.AppContainer" />
  </ItemGroup>
  <ItemGroup>
    <ProjectReference Include="..\Hypersonic\Hypersonic.csproj" />
  </ItemGroup>
</Project>
```

## Updated Hypersonic.csproj

Add reference to Core and update InternalsVisibleTo:

```xml
<ItemGroup>
  <InternalsVisibleTo Include="Hypersonic.Tests" />
</ItemGroup>
<ItemGroup>
  <ProjectReference Include="..\Hypersonic.Core\Hypersonic.Core.csproj" />
  <ProjectReference Include="..\Hypersonic.Client\Hypersonic.Client.csproj" />
</ItemGroup>
```

---

## Test redistribution

### Hypersonic.Core.Tests (net10.0)

Tests for code that lives in Hypersonic.Core. Organized by folder/namespace:

| Test File | Tests | Namespace |
|---|---|---|
| `Services/PlaybackQueueTests.cs` | `PlaybackQueue` | `Hypersonic.Core.Tests.Services` |
| `Services/LoggingServiceTest.cs` | `LoggingService`, `FileSink`, `DebugSink` | `Hypersonic.Core.Tests.Services` |
| `Models/PersistenceTests.cs` | `VaultKeyHelper`, `Json`, `AppState` | `Hypersonic.Core.Tests.Models` |

### Hypersonic.Tests (net10.0-windows, WinUI)

Tests for code that lives in Hypersonic (WinUI-dependent). Organized by folder/namespace:

| Test File | Tests | Namespace |
|---|---|---|
| `Services/ResolveServerUrlsTest.cs` | `SubsonicClientService.ResolveServerUrls` | `Hypersonic.Tests.Services` |
| `Services/LocalSettingsServiceTests.cs` | `LocalSettingsService` | `Hypersonic.Tests.Services` |
| `ViewModels/PlayerViewModelToggleTests.cs` | `PlayerViewModel` toggle commands | `Hypersonic.Tests.ViewModels` |
| `Initialize.cs` | WinUI bootstrap (`[WinUITestTarget]`) | `Hypersonic.Tests` |

Infrastructure: `Initialize.cs` for `[UITestMethod]` support, `Usings.cs` with `AppContainer` using.

### Deleted

- `Hypersonic.Tests.MSTest/TestClass.cs` — scaffold boilerplate
- `Hypersonic.Tests.MSTest/README.md` — template doc
- `Hypersonic.UnitTest/` — entire project (replaced by Core.Tests)
- `Hypersonic.Tests.MSTest/` — entire project (replaced by Tests)

---

## Implementation steps

### 1. Create Hypersonic.Core project

- Create `Hypersonic.Core/Hypersonic.Core.csproj` per spec above
- Add to `Hypersonic.sln`
- Move the 30 source files, preserving directory structure:
  - `Models/` (15 files — excludes DiscHeaderItem, QueueItem, SongInfo which stay in Hypersonic)
  - `Contracts/Services/` (8 files)
  - `Helpers/Json.cs`, `Helpers/VaultKeyHelper.cs`
  - `Services/PlaybackQueue.cs`, `Services/FileService.cs`, `Services/LoggingService.cs`, `Services/Logging/DebugSink.cs`, `Services/Logging/FileSink.cs`
- Update namespaces in all moved files: `Hypersonic.Models` -> `Hypersonic.Core.Models`, `Hypersonic.Services` -> `Hypersonic.Core.Services`, `Hypersonic.Contracts.Services` -> `Hypersonic.Core.Contracts.Services`, `Hypersonic.Helpers` -> `Hypersonic.Core.Helpers`
- Update references across the entire Hypersonic project (see namespace impact below)
- Add `<ProjectReference Include="..\Hypersonic.Core\Hypersonic.Core.csproj" />` to `Hypersonic.csproj`

**Namespace impact on files staying in Hypersonic:**

The namespace rename touches more than just C# service/ViewModel files. All file types that reference moved types must be updated:

- **C# files (~30 files)**: ViewModels, Services, Converters, Collections, Activation, code-behind — add `using Hypersonic.Core.Models;`, `using Hypersonic.Core.Contracts.Services;`, `using Hypersonic.Core.Helpers;`, `using Hypersonic.Core.Services;` as needed. Replace old `using Hypersonic.Models;` etc.

- **XAML files (4 files)**: XAML xmlns declarations reference moved namespaces directly:
  - `Views/AlbumDetailPage.xaml`: `xmlns:models="using:Hypersonic.Models"` -> `xmlns:models="using:Hypersonic.Core.Models"`
  - `Views/PlaylistDetailPage.xaml`: same `models` xmlns update
  - `Views/ShellPage.xaml`: same `models` xmlns update
  - `Views/SettingsPage.xaml`: same `models` xmlns update

  Note: `xmlns:helpers="using:Hypersonic.Helpers"` in ShellPage, AlbumDetailPage, and SettingsPage does NOT change — those reference `TrackListTemplateSelector` and `EnumToBooleanConverter` which stay in Hypersonic.

- **XAML code-behind files**: `ShellPage.xaml.cs` references `Hypersonic.Models.QueueItem`, `AlbumDetailPage.xaml.cs` references `Hypersonic.Models`, etc. — these need updated `using` statements.

- **Converters**: `RepeatModeGlyphConverter.cs`, `RepeatModeToOpacityConverter.cs`, `RepeatModeToAccentBrushConverter.cs` reference `Hypersonic.Models.RepeatMode` — need `using Hypersonic.Core.Models;`.

- **App.xaml.cs / MainWindow.xaml.cs**: Likely reference Core types for DI registration — need updated `using` statements.

The namespace changes are mechanical (find-and-replace `Hypersonic.Models` -> `Hypersonic.Core.Models` etc.) but must cover all file types, not just `.cs`. The compiler and XAML designer will catch any missed references at build time.

### 2. Create Hypersonic.Core.Tests project

- Create `Hypersonic.Core.Tests/Hypersonic.Core.Tests.csproj` per spec above
- Add to `Hypersonic.sln`
- Move test files from `Hypersonic.UnitTest/`:
  - `PlaybackQueueTests.cs` -> `Services/PlaybackQueueTests.cs`
  - `LoggingServiceTest.cs` -> `Services/LoggingServiceTest.cs`
  - `PersistenceTests.cs` -> `Models/PersistenceTests.cs`
- Update namespaces to `Hypersonic.Core.Tests.*`

### 3. Create Hypersonic.Tests project (rename from Tests.MSTest)

- Rename `Hypersonic.Tests.MSTest/` -> `Hypersonic.Tests/`
- Rewrite csproj per spec above (add testhost fix, use MSTest meta-package)
- Update `Hypersonic.sln` project entry
- Update `InternalsVisibleTo` in `Hypersonic.csproj`
- Keep `Initialize.cs` (update namespace)
- Move from `Hypersonic.UnitTest/`:
  - `LocalSettingsServiceTests.cs` -> `Services/LocalSettingsServiceTests.cs`
- Move within project (already in Tests.MSTest):
  - `ResolveServerUrlsTest.cs` -> `Services/ResolveServerUrlsTest.cs`
  - `PlayerViewModelToggleTests.cs` -> `ViewModels/PlayerViewModelToggleTests.cs`
- Update all namespaces to `Hypersonic.Tests.*`
- Delete `TestClass.cs`, `README.md`

### 4. Delete Hypersonic.UnitTest

- Remove from `Hypersonic.sln`
- Delete directory

### 5. Build and verify

```bash
dotnet build Hypersonic.sln -p:Platform=x64
dotnet test Hypersonic.Core.Tests/Hypersonic.Core.Tests.csproj
dotnet test Hypersonic.Tests/Hypersonic.Tests.csproj -p:Platform=x64
dotnet test Hypersonic.Client.UnitTest/Hypersonic.Client.UnitTest.csproj
```

### 6. Update CLAUDE.md and AGENTS.md

Update solution structure, build/test commands, architecture notes, and testing guidelines.

---

## Risks

| Risk | Likelihood | Mitigation |
|---|---|---|
| Namespace update misses a reference | Medium | Build will catch immediately — compiler errors on unresolved types. XAML xmlns declarations must also be updated (4 View files). Mechanical find-and-replace across `.cs` and `.xaml`. |
| Circular dependencies between Core and Hypersonic | Low | Core has no reference to Hypersonic; dependency is one-way |
| Missing transitive dependency in Core | Medium | Build will catch immediately; add PackageReference as needed |
| WinUI testhost crash in Hypersonic.Tests | Low | `OutputType=Exe` + `GenerateProgramFile=true` verified end-to-end |
| `DebugSink.Format` inaccessible from Core.Tests | Low | `InternalsVisibleTo` in Hypersonic.Core.csproj grants access |
