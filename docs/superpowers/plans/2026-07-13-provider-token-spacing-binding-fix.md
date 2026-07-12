# Provider Token Spacing Binding Fix Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Make the reorderable lyrics-provider chips use the same two-DIP horizontal and vertical spacing as the standard font token controls.

**Architecture:** CommunityToolkit's `TokenizingTextBox` items panel finds its owner through `FrameworkElementExtensions.AncestorType="controls:TokenizingTextBox"`. That helper compares exact runtime types, so it does not recognize `ReorderableTokenizingTextBox`; the spacing bindings therefore resolve to zero. Override only the provider control's `ItemsPanel` with the Toolkit panel definition, changing the ancestor type to `ReorderableTokenizingTextBox` while retaining the existing `TokenSpacing="2"` property.

**Tech Stack:** WinUI 3 XAML, CommunityToolkit WinUI `TokenizingTextBox` and `WrapPanel`, MSTest, LINQ to XML.

## Global Constraints

- Preserve the existing drag-to-reorder behavior and `StretchChild="Last"` editor layout.
- Use `TokenSpacing="2"`; do not compensate with a larger magic number or token-item margins.
- Bind both `HorizontalSpacing` and `VerticalSpacing` through the derived control owner.
- Preserve CRLF in modified C# and XAML files.
- Create a new fix commit; do not amend `dca9590` or `c075075`.

---

### Task 1: Restore the derived panel's spacing bindings

**Files:**
- Modify: `Hypersonic.Tests/Views/SettingsPageLayoutTests.cs`
- Modify: `Hypersonic/Views/SettingsPage.xaml`

**Interfaces:**
- Consumes: `ReorderableTokenizingTextBox.TokenSpacing`, CommunityToolkit `WrapPanel`, and `FrameworkElementExtensions.AncestorType`.
- Produces: a provider `ItemsPanelTemplate` whose horizontal and vertical spacing bind to the derived control's `TokenSpacing`.

- [ ] **Step 1: Write the failing layout regression**

Add a test that locates `LyricsProviderBox`, its local `ItemsPanel`, and the contained `WrapPanel`. Require:

```csharp
Assert.AreEqual(
    "hcontrols:ReorderableTokenizingTextBox",
    wrapPanel.Attribute(toolkitNamespace + "FrameworkElementExtensions.AncestorType")?.Value);
Assert.AreEqual(
    "{Binding (ui:FrameworkElementExtensions.Ancestor).TokenSpacing, RelativeSource={RelativeSource Self}}",
    wrapPanel.Attribute("HorizontalSpacing")?.Value);
Assert.AreEqual(
    "{Binding (ui:FrameworkElementExtensions.Ancestor).TokenSpacing, RelativeSource={RelativeSource Self}}",
    wrapPanel.Attribute("VerticalSpacing")?.Value);
Assert.AreEqual("Last", wrapPanel.Attribute("StretchChild")?.Value);
```

- [ ] **Step 2: Run the focused test and verify RED**

Run:

```powershell
rtk proxy dotnet test Hypersonic.Tests\Hypersonic.Tests.csproj -p:Platform=x64 -p:AppxPackageSigningEnabled=false --filter FullyQualifiedName~SettingsPageLayoutTests --no-restore --nologo
```

Expected: the new test fails because `LyricsProviderBox` has no local `ItemsPanel`.

- [ ] **Step 3: Override the provider items panel**

Declare `xmlns:ui="using:CommunityToolkit.WinUI"` on `SettingsPage` and add:

```xml
<hcontrols:ReorderableTokenizingTextBox.ItemsPanel>
    <ItemsPanelTemplate>
        <controls:WrapPanel
            ui:FrameworkElementExtensions.AncestorType="hcontrols:ReorderableTokenizingTextBox"
            HorizontalSpacing="{Binding (ui:FrameworkElementExtensions.Ancestor).TokenSpacing, RelativeSource={RelativeSource Self}}"
            StretchChild="Last"
            VerticalSpacing="{Binding (ui:FrameworkElementExtensions.Ancestor).TokenSpacing, RelativeSource={RelativeSource Self}}" />
    </ItemsPanelTemplate>
</hcontrols:ReorderableTokenizingTextBox.ItemsPanel>
```

- [ ] **Step 4: Run focused and full verification**

Run the focused test again, the signing-disabled app build, the full solution tests, CRLF checks, and `rtk git diff --check`.

- [ ] **Step 5: Commit**

Commit only the plan, test, and XAML as:

```text
fix(lyrics): restore provider token spacing binding
```
