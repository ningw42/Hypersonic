# Scrollbar UX Improvements

*Supersedes the horizontal carousel section of [2026-04-21-home-page.md](2026-04-21-home-page.md#horizontal-carousel) (lines 78–82). That doc described built-in ScrollViewer with Shift+wheel and visible horizontal scrollbars; this doc replaces that model with button-based carousel navigation and hidden scrollbar tracks.*

## Overview

Three interrelated scrollbar UX issues affect the app:

1. **Scrollbar proximity** — Both vertical and horizontal scrollbars render flush against content with no gap, making the UI feel cramped.
2. **Carousel scroll hijacking (MainPage)** — Horizontal ScrollViewers on the home page can intercept vertical mouse wheel input, preventing page scrolling when the pointer is over a carousel.
3. **Dead-end scrolling (ArtistDetailPage)** — The Similar Artists horizontal carousel at the bottom of the page doesn't respond to any scroll input when the outer ScrollViewer is at the bottom. Arrow buttons solve this by providing a clear horizontal navigation affordance that doesn't rely on scroll forwarding.

All three stem from the same root: WinUI 3's default ScrollViewer behavior doesn't match user expectations for nested horizontal/vertical scroll regions.

## Background

### Current architecture

Every page sits inside a `Frame` wrapped by a `Grid` with `Margin="56,24,56,0"` (`NavigationViewPageContentMargin` in `Thickness.xaml`). Pages that scroll use a top-level `ScrollViewer` with `VerticalScrollBarVisibility="Auto"`. The MainPage and ArtistDetailPage additionally nest horizontal `ScrollViewer`s for album/artist carousels.

### Affected pages and ScrollViewers

| Page | Vertical SV | Horizontal SVs | Notes |
|------|-------------|----------------|-------|
| MainPage | Line 14 | 5 (lines 24, 118, 212, 306, 400) | Each carousel is a section |
| ArtistDetailPage | Line 15 | 1 (line 385, Similar Artists) | At bottom of page |
| AlbumDetailPage | Line 125 | 0 | Vertical only |
| PlaylistDetailPage | Line 16 | 0 | Vertical only |
| AlbumsPage | Line 59 | 0 | Infinite-scroll grid |
| ArtistsPage | Line 59 | 0 | Grid |
| PlaylistsPage | Line 58 | 0 | Vertical list |
| ShellPage (search popup) | — | — | 3 ListViews with `MaxHeight="300"` (lines 89, 133, 177) |
| ShellPage (queue flyout) | — | — | 1 ListView with `MaxHeight="440"` (line 335) |

### WinUI 3 ScrollViewer limitations

- **No vertical-to-horizontal wheel translation**: Even with `VerticalScrollMode="Disabled"`, a horizontal-only ScrollViewer does not convert vertical mouse wheel delta into horizontal scrolling. This is a known WinUI limitation ([microsoft-ui-xaml#8553](https://github.com/microsoft/microsoft-ui-xaml/issues/8553), [#9818](https://github.com/microsoft/microsoft-ui-xaml/issues/9818)).
- **Shift+wheel not supported**: Unlike browsers, WinUI 3 ScrollViewers don't natively scroll horizontally on Shift+mouse wheel.
- **Nested scroll event routing is inconsistent**: When the pointer is over a nested horizontal ScrollViewer, vertical wheel events can sometimes be swallowed rather than bubbling to the parent, even with `VerticalScrollMode="Disabled"`.
- **PointerWheelChanged is class-handled by ScrollViewer**: ScrollViewer marks `PointerWheelChanged` as handled internally via class handling. Standard XAML event handlers on a ScrollViewer will not fire for wheel events. The workaround is `UIElement.AddHandler(PointerWheelChangedEvent, handler, handledEventsToo: true)`, which receives the event even after class handling. This is the [documented approach](https://learn.microsoft.com/en-us/windows/windows-app-sdk/api/winrt/microsoft.ui.xaml.controls.scrollviewer).

### Industry precedent (Netflix, Spotify, Apple Music)

Major streaming apps with horizontal carousel layouts universally follow the same pattern:
- **Mouse wheel always scrolls the page vertically** — carousels never capture it.
- **Horizontal carousels use arrow buttons** — left/right chevrons for mouse-based navigation. Horizontal scrolling is exclusively button-driven; mouse wheel, touch drag, and touchpad gestures do not scroll the carousel.
- **Arrow buttons are contextual** — disabled or hidden at the start/end of content.

This is the pattern we adopt, with the simplification that all pointer/gesture-based horizontal scrolling is disabled — navigation is arrow-button-only.

### Theme-refresh integration

MainPage has `ActualThemeChanged` handlers that force-rerender its 5 carousel `ItemsRepeater`s by cycling their `DataContext` (`MainPage.xaml.cs:34-46`). This is because theme-dependent brushes bound in `DataContextChanged` handlers (for artist hyperlinks) don't auto-update on theme change. The `CarouselPanel` does not own the `ItemsRepeater`, so this rerender path is unaffected. ArtistDetailPage's theme-refresh (`ArtistDetailPage.xaml.cs:57-88`) rerenders the discography grid and top-songs list, not the Similar Artists carousel — no `CarouselPanel` interaction there.

## Design

### 1. Scrollbar spacing

**Goal**: Consistent breathing room between content and scrollbar tracks in the vertical axis. (Horizontal scrollbar tracks are removed entirely by the carousel redesign in Section 2.)

#### Vertical scrollbar spacing (16px right padding)

Add 16px right padding to the content container inside every page-level vertical ScrollViewer, so content doesn't touch the scrollbar track.

#### Application matrix — page content areas

| Page | Element | Current Padding | New Padding |
|------|---------|-----------------|-------------|
| **MainPage** | Inner StackPanel | `0,0,0,24` | `0,0,16,24` |
| **ArtistDetailPage** | Inner StackPanel | `0,0,0,24` | `0,0,16,24` |
| **AlbumDetailPage** | Inner StackPanel | `0,0,0,24` | `0,0,16,24` |
| **PlaylistDetailPage** | Inner StackPanel | `0,0,0,24` | `0,0,16,24` |

The 16px value is consistent across all pages.

#### Grid pages (AlbumsPage, ArtistsPage, PlaylistsPage)

These pages use `UniformGridLayout` with `MinItemWidth="200"`. The same approach applies: add 16px right padding to a wrapper element inside the ScrollViewer (e.g., wrap the `ItemsRepeater` in a `Grid` or `Border` with `Padding="0,0,16,0"`). This reduces the width available to `UniformGridLayout` by 16px, which at exact threshold widths could drop a column. In practice, with `MinItemWidth="200"` and the existing 56px left + 56px right page margin (112px total), losing 16px more is negligible — the app window would need to be within 16px of a column threshold for the effect to be visible, and resizing by a few pixels restores it. The consistency benefit outweighs this edge case.

| Page | Element | Current | New |
|------|---------|---------|-----|
| **AlbumsPage** | New wrapper around ItemsRepeater | (none) | `Padding="0,0,16,0"` |
| **ArtistsPage** | New wrapper around ItemsRepeater | (none) | `Padding="0,0,16,0"` |
| **PlaylistsPage** | New wrapper around ItemsRepeater | (none) | `Padding="0,0,16,0"` |

#### ShellPage popup/flyout scrollers

The search result ListViews (`ShellPage.xaml:89,133,177`) and queue flyout ListView (`ShellPage.xaml:335`) live inside constrained popups/flyouts, not full pages. Their scrollbars are already adjacent to popup borders, so adding right padding would waste scarce space. These are **excluded** from this change — the proximity issue is only noticeable on full-page scrollers where content extends to the right edge.

### 2. Carousel navigation pattern

**Goal**: Replace free-scroll horizontal `ScrollViewer` interaction with a button-based carousel. Mouse wheel input is not captured by the carousel — it always scrolls the page vertically. Horizontal scrolling is exclusively controlled by arrow buttons.

#### `CarouselPanel` — Templated custom control

A new `CarouselPanel` extends `ContentControl` (not `UserControl`). This is structurally necessary because `UserControl` uses its `Content` property for its own root visual tree — it cannot also accept page-supplied child content. `ContentControl`, by contrast, is designed to host arbitrary child content via its `Content`/`ContentTemplate` properties while defining its own visual chrome in a `ControlTemplate`.

This eliminates duplicated XAML across 6 carousels (5 MainPage + 1 ArtistDetail).

**Contract**: Pages place their `ItemsRepeater` (with full template and bindings) as the `Content` of `CarouselPanel`. The control template wraps that content in a ScrollViewer with arrow buttons flanking it on either side. The control does not own or manage the `ItemsRepeater`.

This preserves:
- Page-local `x:Bind` and `Click` handlers (they stay on the `ItemsRepeater` and its templates)
- The existing theme-refresh rerender pattern (pages retain `x:Name` references to their repeaters and can call `TryGetElement()` directly)
- Per-carousel `ItemTemplate` differences (MainPage albums vs ArtistDetail artists)

```xml
<!-- Usage in MainPage.xaml -->
<controls:CarouselPanel>
    <ItemsRepeater x:Name="RecentlyPlayedRepeater"
                   ItemsSource="{x:Bind ViewModel.RecentlyPlayed, Mode=OneWay}">
        <ItemsRepeater.Layout>
            <StackLayout Orientation="Horizontal" Spacing="8"/>
        </ItemsRepeater.Layout>
        <ItemsRepeater.ItemTemplate>
            <!-- existing album card DataTemplate, unchanged -->
        </ItemsRepeater.ItemTemplate>
    </ItemsRepeater>
</controls:CarouselPanel>
```

#### Control template (`Themes/Generic.xaml`)

```xml
<Style TargetType="controls:CarouselPanel">
    <Setter Property="Template">
        <Setter.Value>
            <ControlTemplate TargetType="controls:CarouselPanel">
                <Grid x:Name="RootGrid">
                    <Grid.ColumnDefinitions>
                        <ColumnDefinition Width="Auto"/>
                        <ColumnDefinition Width="*"/>
                        <ColumnDefinition Width="Auto"/>
                    </Grid.ColumnDefinitions>

                    <!-- Left arrow -->
                    <Button x:Name="LeftArrowBtn"
                            Grid.Column="0"
                            Style="{StaticResource CarouselArrowButtonStyle}"
                            VerticalAlignment="Center"
                            Margin="0,0,8,0"
                            Visibility="Collapsed"
                            AutomationProperties.Name="Scroll left">
                        <FontIcon Glyph="&#xE76B;" FontSize="16"/>
                    </Button>

                    <!-- Scrollable content -->
                    <ScrollViewer x:Name="Scroller"
                                  Grid.Column="1"
                                  HorizontalScrollBarVisibility="Hidden"
                                  VerticalScrollBarVisibility="Disabled"
                                  HorizontalScrollMode="Disabled"
                                  VerticalScrollMode="Disabled">
                        <ContentPresenter Content="{TemplateBinding Content}"
                                          ContentTemplate="{TemplateBinding ContentTemplate}"/>
                    </ScrollViewer>

                    <!-- Right arrow -->
                    <Button x:Name="RightArrowBtn"
                            Grid.Column="2"
                            Style="{StaticResource CarouselArrowButtonStyle}"
                            VerticalAlignment="Center"
                            Margin="8,0,0,0"
                            Visibility="Collapsed"
                            AutomationProperties.Name="Scroll right">
                        <FontIcon Glyph="&#xE76C;" FontSize="16"/>
                    </Button>
                </Grid>
            </ControlTemplate>
        </Setter.Value>
    </Setter>
</Style>
```

The control retrieves template parts (`Scroller`, `LeftArrowBtn`, `RightArrowBtn`) in its `OnApplyTemplate()` override and wires up event handlers there.

Key points:
- **`HorizontalScrollMode="Disabled"`** — the ScrollViewer does not respond to any mouse wheel or pointer input. No scroll hijacking, no class-handled events, no need for `AddHandler` workarounds.
- **Buttons are outside the carousel** in a 3-column `Grid` layout (Left button | ScrollViewer | Right button). This eliminates hover show/hide complexity, click-through issues with overlaid disabled buttons, and z-order concerns. Buttons are always visible when content overflows.
- **Accessibility**: Arrow buttons have `AutomationProperties.Name` for Narrator ("Scroll left" / "Scroll right").

#### Arrow button behavior

- **Visible when content overflows**: Both arrow buttons become `Visible` after the control loads and detects `Scroller.ScrollableWidth > 0`. They remain visible as long as the carousel has overflow content — no hover show/hide logic. Updated on `SizeChanged` (window resize) and on `Scroller.ViewChanged` (which also fires when content extent changes, e.g., after items load or rerender). This covers both resize-driven and content-driven overflow changes.
- **Disabled at edges**: Left arrow has `IsEnabled="False"` and reduced opacity (40%) when `HorizontalOffset == 0`. Right arrow similarly when `HorizontalOffset >= ScrollableWidth`. Since buttons are outside the carousel content, disabled state does not cause click-through to album cards.
- **Click action**: Scrolls by one viewport width using `ChangeView()` with animation enabled. The control reads `Scroller.ViewportWidth` at click time and scrolls by that amount. This is layout-agnostic — it works regardless of item width, spacing, or `StackLayout` configuration in the hosted `ItemsRepeater`. No knowledge of card dimensions or spacing is needed.

#### Arrow button style (`CarouselArrowButtonStyle` in `Themes/Generic.xaml`)

```
- Shape: 36x36 circle (CornerRadius="18")
- Background: CardBackgroundFillColorDefaultBrush (semi-transparent)
- Border: CardStrokeColorDefaultBrush, 1px
- Hover: SubtleFillColorSecondaryBrush
- Disabled state: 40% opacity
- Vertical centering within the carousel row
- Horizontal: 8px gap between button and carousel content
```

#### Scroll event handling

**None needed.** With `HorizontalScrollMode="Disabled"` and `VerticalScrollMode="Disabled"`, the ScrollViewer does not process any pointer or wheel input. Mouse wheel events bubble normally to the parent page-level vertical ScrollViewer without interception. No `PointerWheelChanged` handlers, no `AddHandler`, no P/Invoke for scroll speed — the entire scroll-handling section from the original design is eliminated.

Horizontal scrolling is exclusively controlled by the arrow button click handlers calling `Scroller.ChangeView()`.

#### Touch and touchpad

- **Touch drag**: Does not work (intentional) — `HorizontalScrollMode="Disabled"` prevents touch panning in the carousel. Touch users use the arrow buttons. This matches the industry pattern (Netflix, Spotify) where touch on desktop carousel cards triggers navigation, not scrolling.
- **Precision touchpad gestures**: Vertical gestures scroll the page normally via the parent ScrollViewer. Horizontal touchpad gestures on the carousel are ignored (consistent with disabled scroll mode).

### 3. ArtistDetailPage Similar Artists

The Similar Artists carousel uses the same `CarouselPanel` as MainPage carousels. The flanking arrow buttons provide clear horizontal navigation affordance at the bottom of the page, solving the discoverability problem.

### 4. Theme-refresh rerender

The `CarouselPanel` wraps page-provided content without owning the `ItemsRepeater`. Pages retain `x:Name` references to their repeaters and continue to call `ReRenderRepeater()` / `ReRenderAllAlbumCards()` from their `ActualThemeChanged` handlers, exactly as today. No changes needed to the theme-refresh code paths in `MainPage.xaml.cs` or `ArtistDetailPage.xaml.cs`.

## Files to modify

| File | Change |
|------|--------|
| `Hypersonic/Controls/CarouselPanel.cs` | **New** — Templated `ContentControl`: `OnApplyTemplate`, arrow button click handlers calling `ChangeView`, disabled-at-edges logic, `SizeChanged`-based visibility |
| `Hypersonic/Themes/Generic.xaml` | Add `CarouselPanel` default `ControlTemplate` (3-column grid with flanking buttons) and `CarouselArrowButtonStyle` |
| `Hypersonic/Views/MainPage.xaml` | Replace 5 horizontal ScrollViewers with `CarouselPanel`; add `0,0,16,24` padding |
| `Hypersonic/Views/ArtistDetailPage.xaml` | Replace Similar Artists ScrollViewer with `CarouselPanel`; add `0,0,16,24` padding |
| `Hypersonic/Views/AlbumDetailPage.xaml` | Change inner StackPanel padding to `0,0,16,24` |
| `Hypersonic/Views/PlaylistDetailPage.xaml` | Change inner StackPanel padding to `0,0,16,24` |
| `Hypersonic/Views/AlbumsPage.xaml` | Wrap ItemsRepeater in `Border` with `Padding="0,0,16,0"` |
| `Hypersonic/Views/ArtistsPage.xaml` | Wrap ItemsRepeater in `Border` with `Padding="0,0,16,0"` |
| `Hypersonic/Views/PlaylistsPage.xaml` | Wrap ItemsRepeater in `Border` with `Padding="0,0,16,0"` |

## Scope exclusions

- **ShellPage search popup and queue flyout ListViews** — Constrained popup/flyout contexts where scrollbar proximity is not a user-facing issue. These are excluded from scrollbar spacing changes.
- **Keyboard navigation within carousels** (arrow key focus management) — future enhancement.
- **Carousel "peek" animation** (showing partial next card) — current card sizing already provides this naturally via 180px cards in a wider viewport.
- **Infinite/wrapping carousel behavior** — content has a definitive start and end.
