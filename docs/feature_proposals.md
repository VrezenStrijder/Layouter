# Layouter Feature Proposals and Competitive Benchmark

> **Document type**: Product design proposal (design only, no implementation)
> **Benchmark targets**: Stardock Fences 6 (primary), Stardock Groupy 2 (secondary), Windows 11 Snap Layouts / Snap Groups (secondary)
> **Written**: 2026-10-27
> **Applicable version**: Layouter 2.0.0 (latest entry in `docs/changelog.md`) / `Layouter.csproj` `Version 1.0.5`
> **Chinese version**: [feature_proposals_cn.md](./feature_proposals_cn.md)

---

## Table of Contents

- [1. Context and Purpose](#1-context-and-purpose)
- [2. Benchmark: Stardock Fences 6](#2-benchmark-stardock-fences-6)
  - [2.1 Fences 6 Release Cadence](#21-fences-6-release-cadence)
  - [2.2 Fences 6 Feature Landscape](#22-fences-6-feature-landscape)
  - [2.3 Secondary Benchmarks: Groupy 2 and Windows 11 Snap](#23-secondary-benchmarks-groupy-2-and-windows-11-snap)
  - [2.4 Gap Summary](#24-gap-summary)
- [3. Layouter Today](#3-layouter-today)
  - [3.1 Capabilities That Already Exist](#31-capabilities-that-already-exist)
  - [3.2 Structural Weaknesses](#32-structural-weaknesses)
- [4. Feature Proposals](#4-feature-proposals)
  - [4.1 Priority Table](#41-priority-table)
  - [4.2 P0: Near Term (Next 1–2 Releases)](#42-p0-near-term-next-12-releases)
  - [4.3 P1: Mid Term](#43-p1-mid-term)
  - [4.4 P2: Long Term](#44-p2-long-term)
- [5. UI and Visual Refinement Proposals](#5-ui-and-visual-refinement-proposals)
  - [5.1 Design Token Baseline](#51-design-token-baseline)
  - [5.2 On-Desktop Partition Window](#52-on-desktop-partition-window)
  - [5.3 Manager Windows](#53-manager-windows)
  - [5.4 Tray Menu](#54-tray-menu)
  - [5.5 Plugin Windows](#55-plugin-windows)
- [6. Recommended Roadmap](#6-recommended-roadmap)
  - [6.1 Near Term (Next 1–2 Releases)](#61-near-term-next-12-releases)
  - [6.2 Mid Term](#62-mid-term)
  - [6.3 Long Term](#63-long-term)
  - [6.4 Explicitly Not Recommended](#64-explicitly-not-recommended)
- [7. References](#7-references)

---

## 1. Context and Purpose

Layouter is a Windows desktop organizer built on .NET / WPF. Its core capability sits in the same quadrant as Stardock's Fences: it creates movable, translucent "partitions" on the desktop with their own title-bar styling, collects desktop icons into them, and adds per-partition appearance customization, a management panel, a tray menu, and a plugin system.

The two products are close in positioning but clearly differ in approach:

| Dimension | Stardock Fences 6 | Layouter |
|---|---|---|
| Business model | Closed-source commercial software, personal and enterprise editions | Open source and free (Apache-2.0) |
| Technology | Native Win32/C++ | .NET 6 + WPF + MVVM (CommunityToolkit.Mvvm) |
| Core asset | 15 years of Shell/desktop-level compatibility hardening | Lightweight, quiet, extensible; the plugin system is the differentiator |
| Release cadence | Commercial-grade; six releases in the last 12 months (6.46 / 6.52 / 6.54 / 6.58) | Single maintainer, long version gaps (1.0.0 → 2.0.0) |

**The goal of this document is not "turn Layouter into Fences."** It answers three concrete questions:

1. Where does Fences 6 actually lead Layouter, and which of those gaps are **fundamental capability gaps** worth closing (cheap wins) versus which are commercial scale effects (not worth chasing)?
2. Under the "simple and quiet" positioning, where can Layouter pursue **its own differentiated direction** and turn the tables?
3. How should the visual layer be unified onto the token system in `Layouter/Styles/Theme.xaml` + `AppControls.xaml` so the UI stops degrading as features are added?

Effort estimates use the following scale: S ≈ 1–2 person-days, M ≈ 3–7 person-days, L ≈ 1–3 weeks (assuming one developer already familiar with the codebase). Value is rated high / medium / low by how much the feature contributes toward "users are willing to make Layouter their primary desktop organizer."

> **A note on Fences versioning**: the original brief referenced "Fences 2 / the 2024+ version." The actual research finding is different: as of 2026-10, the Fences product line is **Fences 6** (6.0 shipped 2025-05-28, and the current latest is **6.58, released 2026-10-14**). This document uses Fences 6 as the benchmark baseline throughout.

---

## 2. Benchmark: Stardock Fences 6

### 2.1 Fences 6 Release Cadence

The Stardock Help Center changelog index shows the Fences 6 release cadence as follows (verified entries only):

| Version | Release date | Key content |
|---|---|---|
| 6.0 (GA) | 2025-05-28 | Fences 6 general availability; tab colors on by default; fixed a Peek performance issue; support for dragging icons onto tab headers |
| 6.20 (UI) / 6.2.0.1 (binary) | 2025-12-09 | Roll-up groups gained "require click to open" and "require mouse hover to open" trigger modes; enhanced alignment snapping near display edges with fixed-distance spacing; new automation rule "icon's name is exactly"; multi-keyboard-layout detection so the default Peek hotkey (Win+Space) does not steal the input-method switch key |
| 6.46 | 2026-04-27 | Refined multi-monitor handling (icons and groups stay put as monitor configurations change); faster startup for slow network-drive Folder Portals; DPI support raised to 400%; improved icon-tint configuration; Peek icon can be shown in the taskbar |
| 6.48 | 2026-06-04 | Fixed icons briefly shuffling when right-clicking the desktop with icon position animation enabled |
| 6.52 | 2026-08-04 | Fixed overlapping icon spacing in "Large" mode; improved DPI-change handling |
| 6.54 | 2026-08-18 | Peek support for left/right-edge taskbars; fixed frame-rate drops when moving groups |
| 6.58 | 2026-10-14 | New desktop spacing detection algorithm (enabled by default for all icon sizes); improved scrollbar animations on rolled-up groups; fixed failed icon drag-and-drop inside rolled-up groups |

**Three conclusions can be drawn from this cadence:**

1. Fences 6's iteration focus has shifted from "adding features" to "**fixing details**" — DPI, multi-monitor, animation frame rate, icon spacing, scrollbar behavior. These are exactly the things that generate the most support tickets, and they are where a small team most easily falls into traps.
2. **Multi-monitor plus high DPI** appears repeatedly in the Fences changelog. That makes it a hard problem in desktop utilities, not an optional extra.
3. Throughout 6.x, Stardock has continued to add **enterprise and scripting** capabilities for Fences (the help center also carries AltTabby, Clairvoyance, and similar entries), but those belong to a separate product line.

### 2.2 Fences 6 Feature Landscape

The following features are confirmed by both the Fences 6 product page and the changelogs listed above:

**Partitions (core)**
- Fence groups: translucent areas on the desktop that hold desktop icons, with adjustable size, color, and transparency
- Group locking: position fixed so it never moves (the product page explicitly mentions "lock their location so that they never move" in an accessibility context)
- Quick-hide groups to get a distraction-free desktop
- Hold Shift while resizing to temporarily disable snapping

**Peek™**
- Default hotkey `Win + Space`, which temporarily places all groups above all windows for quick access to desktop icons
- Optionally show the Peek icon in the taskbar (confirmed by the "Show in Taskbar" fix item in the 6.46 changelog)
- Support for left/right-edge taskbars since 6.54

**Folder Portals™**
- Mirror any directory onto the desktop as a group, supporting local directories, network drives, and cloud placeholders
- "List" and "horizontal list" presentation styles; option to hide the folder icon on the title bar
- Right-click → "Open" opens in a new Explorer window (even when "Navigate" mode is enabled)

**Tabs**
- A group can hold multiple tabs, each with its own independent set of icons
- Tab colors on by default; icons can be dragged directly onto a tab header
- Tabs can be undocked from a tab group; per-group custom title fonts and title font colors apply to their tabs

**Chameleon™ and Icon Tint**
- Chameleon: desktop icons fade into the wallpaper by dominant color for a background-blended look
- Icon Tint: applies a uniform tint to icons so icons from different sources look consistent

**Roll-up**
- A group can collapse down to just its title bar
- Since 6.2, two trigger modes are supported: "require click to open" and "require mouse hover to open"
- 6.58 improved scrollbar animations on rolled-up groups

**Alignment snapping**
- Alignment snapping when placing a group near the edge of a display, with a "fixed distance spacing" mode

**Automation rules**
- Rule-based automation: sort by file type / name / time, move icons to a target location
- 6.2 added the "icon's name is exactly" rule and intelligent detection of changes to the desktop directory

**Other**
- Snapping during resize can be temporarily disabled with Shift
- Simplified Chinese localization is supported
- Enterprise scenarios: network-drive deployment, content locking, scriptable clean-up

**Fences capabilities that this research did NOT verify** (recorded as open questions, not asserted):

- Whether there is "Tab-key cycling between fences" keyboard navigation
- Whether there is full "taskbar / dock integration" (only the Peek icon appearing in the taskbar was verified)
- Dedicated support for external drives / removable disks (only network-drive support in Folder Portals was verified)
- Integration with chat software or notification popups
- The concept of "custom desktops" (virtual desktops)

**On AI**: Stardock does have a separate AI product line (DesktopGPT, Clairvoyance AI Staff), but **Fences itself has no AI features**. This matters for the competitive analysis — AI is not a Fences selling point, and Layouter does not need to treat it as a required benchmark (see §6.4).

### 2.3 Secondary Benchmarks: Groupy 2 and Windows 11 Snap

**Stardock Groupy 2** — released around the same time as Fences but solves a different problem: drag multiple application windows together and merge them under a unified tab bar. Key capabilities: compatible windows integrate tabs into the title bar; windows with custom title bars show tabs above them; moving the mouse to the top of an ungrouped window's title bar pops up a Groupy bar; a grouping settings page covers hotkeys, Explorer enhancements, and options such as "show the add-tab button on all windows."

**Implication for Layouter**: Groupy manipulates *application windows*; Fences manipulates *desktop icon containers*. Their tab interactions are highly similar (tab coloring, drag-and-drop, undocking). **If Layouter ever ships partition tabs (F-17), the interaction model can borrow Groupy 2's tab strip directly** instead of inventing one from scratch.

**Windows 11 Snap Layouts / Snap Groups** — built into the OS: hovering a window's maximize button or pressing `Win + Z` opens a layout flyout; dragging a window to the top of the screen also opens it; selecting a zone lets Snap Assist fill the remaining zones; Snap Groups can save the arrangement of a set of windows and restore it later.

**Implication for Layouter**: Snap Groups solves "I opened a set of windows this morning and want to restore that exact arrangement" — precisely the need that Layouter's **layout snapshots (F-09)** address, but for desktop icons. The concepts align, and the existence of Snap Groups confirms the demand is real and validated.

### 2.4 Gap Summary

| Capability area | Fences 6 | Layouter today | Gap verdict |
|---|---|---|---|
| Partition container + icon hosting | ✅ | ✅ | Aligned |
| Partition appearance (title bar / opacity / icon size / alignment) | ✅ | ✅ (`PartitionSettings`) | Aligned |
| Partition roll-up | ✅ (configurable trigger) | ⚠️ `IsRolledUp` field exists, interaction unpolished | Needs refinement |
| Show/hide partitions | ✅ | ⚠️ Only via checkboxes in the manager window | **Gap** |
| Hotkeys | ✅ (Peek defaults to Win+Space, with multi-layout detection) | ❌ No hotkey layer at all | **Gap** |
| Peek / desktop-above-windows preview | ✅ | ❌ | **Gap** |
| Drag snapping + equal-spacing guides | ✅ | ⚠️ Only a mention of "auto snap" in the README | **Gap** |
| Multi-monitor | ✅ (continuous investment) | ❌ Only `SystemParameters.WorkArea` | **Gap** |
| High DPI (>200%) | ✅ (up to 400%) | ❌ Not addressed | **Gap** |
| Tabs | ✅ | ❌ | Gap (long term) |
| Folder portals | ✅ | ❌ | Gap (long term) |
| Icon tinting / background blending | ✅ | ❌ | Gap (mid term) |
| Automation rules | ✅ | ❌ | Gap (long term) |
| Plugin system | ❌ | ✅ | **Layouter's unique advantage** |
| Built-in code editor | ❌ | ✅ | **Layouter's unique advantage** |

**Conclusion**: Layouter is close to Fences on the partition itself. The real gap sits in **desktop-level interaction efficiency** (hotkeys, visibility, Peek, snapping, multi-monitor, DPI) and in **how partition contents are organized** (tabs, folder portals). The former is foundational and must be closed; the latter is what actually separates the products.

---

## 3. Layouter Today

### 3.1 Capabilities That Already Exist

Confirmed by reading the code, and treated as the basis for the proposals below:

| Capability | Location |
|---|---|
| Desktop-level partition window | `Views/DesktopManagerWindow.xaml`, placed at desktop level via `SysUtil.SetDesktopLevelWindow` |
| Partition appearance customization | `Models/PartitionSettings.cs`: `TitleForeground` / `TitleBackground` / `TitleFont` / `TitleFontSize` / `TitleAlignment` / `Opacity` / `IconSize` / `IconTextSize` / `IsLocked` / `IsRolledUp` |
| Global style | `Models/GlobalPartitionSettings`, `GeneralSettings.EnableGlobalStyle`, `WindowManagerService.UpdateAllWindowStyles()` |
| Partition data persistence | `Services/PartitionDataService.cs` (`PartitionData` / `PartitionMetadata`), archived per `windowId` |
| Desktop icon enumeration and hiding | `Services/DesktopIconService.cs`, paired with `GeneralSettings.HiddenStoreDirectory` |
| One-click partition arrangement | `WindowManagerService.ArrangeWindows()` / `ArrangeAllPartitionWindows()` (√N grid across the work area) |
| Batch partition management | `PartitionManagerViewModel`: `ToggleSelectAll` / `ToggleVisibilitySelected` / `DeleteSelected` / `EditGlobalStyle` |
| Tray menu | `Services/TrayMenuService.cs`: new partition / partition manager / autostart / plugins / about / exit |
| Built-in updater | `Views/UpdateWindow.xaml` + `Services/UpdateService` (version list plus multi-version download selection) |
| Plugin system | `Plugins/PluginManager.cs`, `Plugins/PluginLoader.cs`, built-in code editor (`Plugins/Controls/CodeEditor.xaml`, AvalonEdit) |
| Shell interop | `Utility/ShellUtil.cs`, `Utility/ShortcutUtil.cs` |
| Visual token foundation | `Styles/Theme.xaml`, `Styles/AppControls.xaml`, `Styles/TrayIconResources.xaml`, already exposing `AppPageTitleText`, `ColorSwatchStyle`, `TrayMenuStyle` and similar resource keys |

### 3.2 Structural Weaknesses

Structural problems visible directly in the code that will affect how the proposals above can be implemented:

1. **No hotkey layer.** `Utility/SysUtil.cs` currently exposes only `GetWindowHandle` / `SetDesktopLevelWindow` / `IsWindows11` / `MeasureText`, with no `RegisterHotKey` wrapper. Every hotkey-related proposal needs this layer first.
2. **Multi-monitor is implicitly ignored.** `WindowManagerService.ArrangeAllPartitionWindows()` uses `SystemParameters.WorkArea` (the primary screen), and `PartitionData` has no field recording which display a partition belongs to. Cross-screen behavior is currently undefined.
3. **No DPI handling.** The target framework is `net6.0-windows7.0` with no `PerMonitorV2` declaration or DPI-change response. Fences spent the better part of a year iterating on exactly this.
4. **The visibility model is scattered.** `GeneralSettings.PartitionVisibility` holds a dictionary, but the only UI entry point is `PartitionManagerWindow`; neither the tray nor a hotkey offers direct access.
5. **Styling is "one set of values per partition," not "tokens plus overrides."** `PartitionSettings` stores absolute color values (the `Color` type) with no layering semantics for "follow global / override locally." Batch restyling can only assign wholesale values.
6. **Version and documentation disagree.** `docs/readme_cn.md` claims ".NET 8" while `Layouter.csproj` targets `net6.0-windows7.0`; `docs/changelog.md` reaches `2.0.0 (2026-06-03)` while the assembly version in the csproj is still `1.0.5`. This directly affects upgrade decisions and issue triage.
7. **Visible technical debt.** `Layouter.csproj` uses roughly 40 `<Compile Remove>` / `<Page Remove>` entries to exclude `Plugins/Deprecated/**`, `Services/ConfigurationService.cs`, `Views/Feature1Window.xaml`, and other historical files. This needs one cleanup pass before features keep accumulating.
8. **No accessibility baseline.** The buttons on the partition title bar have no `AutomationProperties` annotations and no keyboard focus styling. Fences 6.46 specifically "improved the accessibility of titlebar style options in the Customization pane," which shows this is a baseline expectation for professional software.

---

## 4. Feature Proposals

### 4.1 Priority Table

| ID | Proposal | One-line description | Effort | Value | Priority |
|---|---|---|---|---|---|
| F-01 | Quick partition show/hide + focus mode | Collapse all partitions with one action, or toggle a single partition | S | High | P0 |
| F-02 | Tray partition quick panel | Tray menu lists every partition with visibility checkboxes | S | High | P0 |
| F-03 | Global hotkey framework | New configurable global hotkey service with conflict detection | M | High | P0 |
| F-04 | Partition drag snapping and equal-spacing guides | Snap to edges, to neighbours, and to equal spacing, with 1px guides | M | High | P0 |
| F-05 | One-click layout presets | Grid / horizontal / vertical / primary-secondary layouts | M | Medium | P0 |
| F-06 | Documentation and version consistency | Align readme, changelog, and csproj version and framework claims | S | Medium | P0 |
| F-07 | Peek desktop preview mode | Hotkey places all partitions above all windows, then restores | M | High | P1 |
| F-08 | Multi-monitor partition ownership | Record each partition's display and restore position across display changes | L | High | P1 |
| F-09 | Layout snapshots | Save multiple named layouts and switch the whole desktop in one action | M | High | P1 |
| F-10 | Icon sorting and automatic grouping rules | Sort by name/type/date and auto-route icons into partitions by rule | L | Medium | P1 |
| F-11 | Icon tinting and background blending | Port Icon Tint and Chameleon: unify icon color / blend into wallpaper | M | Medium | P1 |
| F-12 | Batch styling and batch renaming | Multi-select partitions and change color, opacity, or title font in bulk | M | Medium | P1 |
| F-13 | Decouple opacity / content background / shadow | Three independent visual dimensions replacing today's single `Opacity` | M | Medium | P1 |
| F-14 | Config backup, restore, and migration | Turn `BackupRootDirectory` into a working backup/restore feature with config version migration | M | High | P1 |
| F-15 | Safe mode and crash recovery | Enter safe mode (plugins disabled) after an abnormal exit, preserving the crash scene | M | High | P1 |
| F-16 | Plugin API versioning and capability declaration | Plugin manifests declare API version and required capabilities; the host negotiates | L | Medium | P1 |
| F-17 | Partition tabs | Multiple tabs inside one partition, each with an independent icon set | L | High | P2 |
| F-18 | Folder portals | Map any directory to a virtual partition, including network and cloud drives | L | High | P2 |
| F-19 | Theme packs / appearance marketplace | Import/export theme bundles (colors, fonts, radii, shadows) | M | Low | P2 |
| F-20 | Localization (English UI) | Extract UI strings into resources and support English | M | Medium | P2 |

### 4.2 P0: Near Term (Next 1–2 Releases)

#### F-01 Quick partition show/hide + focus mode

**Description**: Provide a "hide all partitions" (focus mode) action and per-partition visibility toggling, wired to both the tray and hotkeys.

**User value**: The central tension of a desktop organizer is that partitions must exist yet stay out of the way during work. Today, hiding the desktop requires opening the partition manager and unchecking every box one at a time — the most frequent and most awkward operation in the product. Focus mode (the distraction-free desktop equivalent in Fences) lets users clear the desktop to start working and restore it with one action.

**Implementation outline**:
- Add a `FocusModeEnabled` flag to `GeneralSettings`. Entering focus mode calls the bulk hide path in `WindowManagerService` — reuse the same implementation behind `PartitionManagerViewModel.UpdateWindowVisibility`, but push that logic down into `WindowManagerService` so ViewModels do not call each other.
- Hidden partitions must not lose icon ownership: `GeneralSettings.PartitionVisibility` already exists, so reuse it directly.
- Tray entry point per F-02; hotkey entry point per F-03.

**Effort**: S　**Value**: High
**Dependencies and risks**: `PartitionManagerViewModel.UpdateWindowVisibility` currently manipulates `WindowManagerService` and window instances directly, so a small refactor (pushing visibility handling down into `WindowManagerService`) is required first to avoid ViewModel-to-ViewModel coupling.

#### F-02 Tray partition quick panel

**Description**: In `TrayMenuService.CreateTrayMenu()`, insert a "Partitions" submenu immediately after "New partition," listing all current partitions with checkboxes that toggle visibility.

**User value**: The tray is the only real control center for a tool like this. It currently holds just four items (new / manage / autostart / plugins), so hiding a partition takes two clicks, a window, and a hunt for a checkbox. Adding this turns 90% of daily actions into tray-only operations.

**Implementation outline**:
- Add `CreatePartitionMenuItems()` to `TrayMenuService`, sourced from `PartitionDataService.Instance.GetMetadata().WindowIds` plus `PartitionDataService.Instance.GetPartitionData(id)` — the same retrieval logic as `PartitionManagerViewModel.LoadPartitions()`. Prefer extracting this into a `GetAllPartitionSummaries()` method on `PartitionDataService` to avoid duplicating it in two places.
- Subscribe to `GeneralSettingsService.Instance.PartitionVisibilityChanged` for incremental refresh so the menu is not fully rebuilt on every open; `PartitionManagerViewModel` already does the equivalent, so follow that pattern.
- Add a 16px FluentIcons glyph beside each partition name to convey state.

**Effort**: S　**Value**: High
**Dependencies and risks**: Low. `TrayMenuService` already keeps a `menuItems` dictionary for keyed lookups; extend that pattern.

#### F-03 Global hotkey framework

**Description**: Add `Services/HotkeyService.cs` wrapping `RegisterHotKey` / `UnregisterHotKey`, shipping a set of default hotkeys with user customization, conflict detection, and a friendly "already in use" message.

**User value**: Desktop organization is a high-frequency activity, and a product reachable only by mouse gets demoted to "something I open occasionally." Fences binds Peek to `Win + Space` by default and even added multi-keyboard-layout detection to avoid colliding with input-method switching — the kind of detail that shows hotkeys are table stakes for professional desktop utilities, not a nice-to-have.

**Implementation outline**:
- `Services/HotkeyService.cs`: `RegisterHotKey` wrapper + a `HotkeyBinding` model (modifiers + key + action ID) + `HotkeySettings` persisted into `GeneralSettings`.
- Suggested defaults: `Win + Space` → Peek (F-07), `Win + Shift + D` → focus mode, `Win + Shift + N` → new partition, `Win + Shift + M` → open partition manager.
- On registration failure (hotkey held by another program), surface a balloon tip via `TrayIconService.Instance.ShowBalloonTip`, matching the existing error-handling style in `TrayMenuService.ToggleAutoStart`.
- Register during the `App.xaml.cs` startup sequence and unregister on exit.

**Effort**: M　**Value**: High
**Dependencies and risks**: `Win + Space` is the default Chinese/English input-method switch key and will contend with the OS. **Registration must perform keyboard-layout detection and offer an "assign a different combination" option** — a problem Fences has already hit (its 6.2 changelog records exactly this).

#### F-04 Partition drag snapping and equal-spacing guides

**Description**: While dragging a partition window, compute snapping candidates against screen edges and other partitions in real time; on a hit, draw a 1px accent-colored guide and lock the position. Support a "fixed spacing" mode that keeps gaps between partitions consistent.

**User value**: This is the dividing line between "partitions are usable" and "partitions feel good." Without snapping, a multi-partition desktop is aligned by eye alone; with snapping plus equal spacing, it becomes an actual grid. Fences specifically enhanced "alignment snapping near the edge of a display with fixed distance spacing" in 6.2, which tells us users actively asked for it.

**Implementation outline**:
- Extract the snapping math into `Services/SnapEngine.cs` as a pure function: candidate rectangles + other partition rectangles + screen work area in, snapped rectangle and hit guide list out. Pure functions are unit-testable.
- Hook `SnapEngine` into the drag logic of `DesktopManagerWindow`; the existing `isArrangingWindows` flag in `WindowManagerService` can be reused to suppress snapping for programmatic moves.
- Draw guides with an `AdornerDecorator` or a topmost `Popup`, 1px in the `Theme.xaml` accent token (`#2563EB`), 80% opacity.
- Store the fixed-spacing value in `GeneralSettings`, default 16.

**Effort**: M　**Value**: High
**Dependencies and risks**: Medium. WPF drag events are `Thumb.DragDelta`-shaped and interact poorly with direct `Window.Left` / `Top` assignment, producing visible jitter; this needs repeated tuning at real DPI settings. Suggest shipping screen-edge snapping first and deferring equal-spacing to the following iteration.

#### F-05 One-click layout presets

**Description**: Beyond the existing `WindowManagerService.ArrangeAllPartitionWindows()`, add layout strategies: grid (today's behavior), horizontal, vertical, primary-secondary, and "keep manual."

**User value**: When users change monitors, change resolution, or simply want to "tidy up again," the √N grid is currently the only outcome available. Horizontal even-split and primary-secondary cover the overwhelming majority of real scenarios.

**Implementation outline**:
- Add a `Models/LayoutStrategy.cs` enum (`Grid` / `Horizontal` / `Vertical` / `PrimarySecondary` / `KeepManual`).
- Split `WindowManagerService.ArrangeAllPartitionWindows()` into "compute layout" and "apply layout" steps, branching the compute step by strategy. The existing grid algorithm becomes the `Grid` branch with unchanged behavior.
- Expose strategy selection in the `PartitionManagerWindow` toolbar (see §5.3) and an "apply layout" entry in the `SettingsPopup` of `DesktopManagerWindow`.

**Effort**: M　**Value**: Medium
**Dependencies and risks**: Low. `ArrangeAllPartitionWindows()` already guards with `isArrangingWindows` and persists through `PartitionDataService.Instance.SavePartitionData`; keep both when splitting.

#### F-06 Documentation and version consistency

**Description**: Unify version numbering and framework claims, and clean up deprecated project files.

**User value**: This produces no feature on its own, but it directly affects upgrade willingness and issue-triage speed. `readme_cn.md` claims .NET 8 while the csproj targets net6.0, and the changelog reaches 2.0.0 while the assembly version is stuck at 1.0.5 — such inconsistencies make users suspect the software is broken.

**Implementation outline**:
- Decide the versioning policy (recommend the `docs/changelog.md` as the source of truth) and sync `Layouter.csproj`'s `<Version>` / `<AssemblyVersion>` / `<FileVersion>`. Confirm which field `UpdateWindow.xaml`'s update check actually reads.
- Correct the framework claim in `docs/readme_cn.md` / `readme.md` — either raise the csproj to net8.0-windows or change the copy. Given `net6.0-windows7.0` is out of support, raising the csproj is the better direction.
- Delete the already-excluded legacy files and the `Plugins/Deprecated/**` directory, including `Plugins/Deprecated/PluginManagerWindow.xaml`.
- Add post-2.0.0 entries to `docs/changelog.md` and record this document's proposal numbering scheme.

**Effort**: S　**Value**: Medium
**Dependencies and risks**: Low, but **changing the csproj target framework changes runtime behavior** (Shell interop and desktop-level windows must be re-validated). Keep it as a separate commit.

### 4.3 P1: Mid Term

#### F-07 Peek desktop preview mode

**Description**: One hotkey places all partitions above all windows (with a Peek frame and backdrop); pressing again, or clicking elsewhere, restores the previous state.

**User value**: Fences' most celebrated feature. It solves the frequent situation of "I need a file from the desktop but don't want to minimize every window first."

**Implementation outline**:
- Reuse the approach in `SysUtil.SetDesktopLevelWindow`, but temporarily promote to a higher level (`WS_EX_TOPMOST`). Note that Layouter's current "desktop level" sits *below* normal windows; Peek requires lifting to topmost.
- Model as a state machine: `Normal → Peeking → Normal`. On entry, record all window Z-orders and positions; on exit, restore them exactly, to avoid fighting other applications' Z-order management.
- Backdrop is a semi-transparent black layer (opacity decoupled from the partition `Opacity` — see F-13).
- Peek must not steal focus, so the user's typing window keeps focus — a performance and experience trap Fences hit (6.0 fixed a Peek performance issue).

**Effort**: M　**Value**: High
**Dependencies and risks**: **High.** Z-order manipulation is global Windows state and interferes with third-party software (taskbars, virtual-desktop utilities in particular). Recommend doing this only after §6.3, and shipping it behind a disableable experimental flag.

#### F-08 Multi-monitor partition ownership

**Description**: Record the owning display (device name + resolution + scale) for each partition, and restore partitions to their original position on the correct display when the monitor configuration changes (hot-plug, resolution change, primary display switch).

**User value**: Almost everyone with a laptop has an external monitor. Without this, partitions scatter the moment the resolution changes — the classic "installed once, uninstalled immediately" failure mode.

**Implementation outline**:
- Add `DisplayDeviceName` / `DisplayBounds` to `Models/PartitionData.cs`, with backward-compatible serialization (older configs missing the field fall back to defaults).
- Enumerate displays via `System.Windows.Forms.Screen` (requires referencing `UseWindowsForms`) or a direct `EnumDisplayMonitors` P/Invoke, replacing the fixed `SystemParameters.WorkArea`.
- Listen for `Microsoft.Win32.SystemEvents.DisplaySettingsChanged` and re-position once, marshaling to the Dispatcher because the event does not arrive on the UI thread.
- DPI: the target framework must declare `PerMonitorV2` (add `<dpiAwareness>PerMonitorV2</dpiAwareness>` to `app.manifest`) and handle `WM_DPICHANGED`.

**Effort**: L　**Value**: High
**Dependencies and risks**: **High.** DPI and multi-monitor are the areas where Fences spent the better part of a year iterating, and they involve raw native window messaging. **Do DPI awareness first (raise to PerMonitorV2, handle `WM_DPICHANGED`), then multi-monitor ownership** — the order cannot be reversed.

#### F-09 Layout snapshots

**Description**: Package "position, size, visibility, and style of every partition" into a named snapshot (e.g. "Work," "Gaming," "Presenting") that can be applied in one action, and that can restore a layout after a monitor or resolution change.

**User value**: Structurally the same need as Windows 11's Snap Groups — "put that arrangement back exactly as it was." For Layouter it becomes inevitable once partition count grows: past eight partitions, manual re-layout is no longer practical.

**Implementation outline**:
- Add `Models/LayoutSnapshot.cs` (name + dictionary of partition id → geometry/style/visibility + creation timestamp).
- Store under the directory pointed to by `GeneralSettings.BackupRootDirectory` — that field already exists but has no productized entry point, so this is a natural way to put it to work.
- Add `WindowManagerService.ApplySnapshot(snapshot)`, reusing the "apply layout" pipeline extracted in F-05.

**Effort**: M　**Value**: High
**Dependencies and risks**: Medium. Depends on the F-05 layout-pipeline split landing first.

#### F-10 Icon sorting and automatic grouping rules

**Description**: Support sorting by name / type / size / modification time, plus simple rules that automatically move matching icons into a designated partition.

**User value**: Once partitions exist, icons for newly downloaded files scatter across the desktop and must be dragged manually. Automatic routing is the core value of Fences' "automation rules" and becomes essential as partition count grows.

**Implementation outline**:
- Sorting: `DesktopIconService` already enumerates icons; add `SortIcons(partitionId, IconSortRule)` that reorders icons within `PartitionData` and writes the order back to the actual desktop (the Shell interop in `Utility/ShellUtil.cs` is available for this).
- Auto-grouping: add `Models/AutoGroupRule.cs` (condition + target partition id), triggered by directory-change monitoring. Use a `FileSystemWatcher`, running on a background thread and marshaling back to the UI thread.
- Rules UI goes in a dedicated group within the partition settings window, kept separate from appearance settings.

**Effort**: L　**Value**: Medium
**Dependencies and risks**: Medium. File-change monitoring performance and thread model are the main risks. Recommend shipping sorting first and deferring live monitoring; a periodic poll plus manual trigger is enough for v1.

#### F-11 Icon tinting and background blending

**Description**: Port Fences' Icon Tint (uniform icon color) and Chameleon (icons blend into the wallpaper by dominant color). Provide a global switch plus per-partition overrides.

**User value**: Visual consistency of desktop icons is one of the main sources of the "messy" feeling on a Windows desktop. Tinting immediately makes a desktop look tidy, and it is often the first Fences feature users notice.

**Implementation outline**:
- Tinting: Layouter draws its desktop icons itself (the `iconsContainer` in `DesktopManagerWindow`), so a hue shift and saturation adjustment via `Image.Source` plus a `ColorMatrix` (`System.Windows.Media.Imaging`) in the icon presenter is inexpensive.
- Background blending (Chameleon) requires sampling the wallpaper's dominant color and fading icons accordingly — considerably more work; schedule it as phase two.
- Reuse the existing color-picker interaction pattern (`ColorSwatchStyle` plus `TitleForegroundButton`) from `PartitionSettingsWindow`.

**Effort**: M (tinting) / L (blending)　**Value**: Medium
**Dependencies and risks**: Low. Tinting reduces icon legibility, so a one-click "off" must always be available.

#### F-12 Batch styling and batch renaming

**Description**: In `PartitionManagerWindow`, multi-select partitions and set title bar foreground/background, font, size, opacity, and icon size in bulk; also support batch renaming with prefix substitution.

**User value**: `PartitionManagerViewModel` already has multi-select machinery (`AllSelected` / `AnySelectedVisible`), but batch operations only cover visibility and deletion. Batch styling is the minimal increment that closes the loop.

**Implementation outline**:
- `PartitionSettingsWindow` currently follows a "single ViewModel plus an `isGlobal` flag" pattern (`new PartitionSettingsWindow(viewModel, false)` / `(viewModel, true)`). For batch styling, add a lightweight "batch style" dialog reusing the same controls but writing to multiple partitions.
- On save, iterate and call `PartitionSettingsService.Instance.SaveSettings(vm, false)` per partition.

**Effort**: M　**Value**: Medium
**Dependencies and risks**: Low. Note that when `EnableGlobalStyle` is on, the global style overrides per-partition styles; warn before a batch change in that state.

#### F-13 Decouple opacity / content background / shadow

**Description**: Split today's single `Opacity` into three independent dimensions: overall partition opacity, icon-area content background opacity, and shadow/outline presence and strength.

**User value**: With one opacity value, a "solid title bar over a translucent icon area" is impossible — the user can only dial one value for the whole thing. Fences separates "transparency" from "opacity" into two independent settings, which is what allows it to produce visual layering.

**Implementation outline**:
- Add `ContentOpacity`, `ShadowEnabled` / `ShadowBlur` / `ShadowOpacity` to `Models/PartitionSettings.cs`; keep `Opacity` semantics unchanged for backward compatibility (initialize `ContentOpacity` to 1 when loading old configs).
- In `DesktopManagerWindow.xaml`, `IconsContainer`'s `Background="{Binding ContentBackground}"` becomes a brush with its own alpha; shadows use `Effect` + `DropShadowEffect`. Note that `Effect` triggers offscreen rendering, which can hurt performance on large partitions — apply it to the title bar only, or use a `Border`-based fake shadow.

**Effort**: M　**Value**: Medium
**Dependencies and risks**: Low to medium. `Effect` rendering cost needs measurement.

#### F-14 Config backup, restore, and migration

**Description**: Turn `GeneralSettings.BackupRootDirectory` into a genuinely usable feature: periodic or manual backups of all configuration (partition data, styles, general settings, plugins), restore from a backup list, and introduce a config format version with migration logic.

**User value**: Desktop layouts are an asset users spend time accumulating, and losing them is unrecoverable. Layouter stores configuration under the user profile, so a reinstall or a machine change wipes it. On the competitive side, Stardock's enterprise edition explicitly offers deployment to network drives and content locking, and individual users care just as much.

**Implementation outline**:
- Backup = zip the data directories used by `PartitionDataService` / `GeneralSettingsService` / `PartitionSettingsService` (`System.IO.Compression` is already a dependency).
- Restore = extract to a temp directory → validate → atomically replace. Before replacing, all partition windows must be closed and rebuilt, so `WindowManagerService` needs a `ReloadAll()`.
- Migration: keep a `config-version` file in the config root and migrate step by step on read. Do this together with the F-06 versioning work.

**Effort**: M　**Value**: High
**Dependencies and risks**: Medium. A window rebuild failure during restore can lose partitions, so "validate before replace" is mandatory.

#### F-15 Safe mode and crash recovery

**Description**: If the previous run ended abnormally (no clean-exit marker), the next launch enters safe mode: no plugins are loaded, only core partition functionality runs, and the user is told.

**User value**: Layouter has a built-in code editor and a plugin loader (`Plugins/PluginLoader.cs`), so a user-written plugin that throws and takes the whole application down is a genuine risk. Without safe mode, the only remedy is manually deleting the config directory.

**Implementation outline**:
- Write `running.lock` (including the process id) into the config directory and delete it on clean exit; a leftover file at startup indicates an abnormal exit.
- Add a `LoadAll(bool includePlugins)` parameter to `PluginManager`; safe mode skips plugin loading.
- Preserve the scene: Serilog is already integrated, so the last N log entries from the rolling file can be bundled for export.

**Effort**: M　**Value**: High
**Dependencies and risks**: Low.

#### F-16 Plugin API versioning and capability declaration

**Description**: Introduce `ApiVersion` and `Capabilities` fields in the plugin manifest (`Plugins/PluginTemplates/PluginInfo.json`). The host checks version compatibility and negotiates capabilities before enabling a plugin.

**User value**: The plugin system is Layouter's core differentiator against Fences and also its most fragile surface. Today a plugin manifest carries no version concept, so a host upgrade can silently break or crash old plugins. With versions and capability declarations, plugin authors can upgrade with confidence.

**Implementation outline**:
- Add an API version constant and compatibility check to `Plugins/PluginManager.cs`; on incompatibility, disable the plugin and state the reason in `PluginsManagerWindow` instead of throwing.
- `Capabilities` declares what a plugin needs (e.g. "requires partition API v2", "requires icon list access"); the host uses it to decide whether to allow the plugin to load.

**Effort**: L　**Value**: Medium
**Dependencies and risks**: Medium. This is an investment in the future with little short-term payoff, but the cost of doing it later only goes up.

### 4.4 P2: Long Term

#### F-17 Partition tabs

**Description**: A partition can hold multiple tabs, each with an independent set of desktop icons. Tabs can be colored, accept dropped icons, and be detached into a standalone partition.

**User value**: Fences 6's most recognizable feature. When users have 30+ desktop icons, a single partition grows until it needs scrolling; tabs compress the space requirement dramatically.

**Implementation outline**:
- Data model: add a `Tabs` collection to `PartitionData`, each tab holding its own icon list; add `ActiveTabIndex` and per-tab colors to `PartitionSettings`.
- UI: insert a tab strip above or below the `TitleBar` in `DesktopManagerWindow.xaml` (currently `Height="30"`); the strip is about 24px tall.
- The interaction model can borrow Groupy 2's tab strip (see §2.3), including "drag onto a tab header" and "detach from the tab group."
- Drop-target detection requires extending the existing icon drag-and-drop logic in `DesktopManagerWindow.xaml.cs`.

**Effort**: L　**Value**: High
**Dependencies and risks**: **High.** This is the proposal with the largest impact on the existing data model, affecting `PartitionDataService` serialization, `PartitionManagerWindow` list rendering, and `DesktopIconService` ownership logic simultaneously. Do it after F-08 (multi-monitor) and F-14 (config migration), otherwise config migration has to absorb two large changes at once.

#### F-18 Folder portals

**Description**: Map any local, network, or cloud directory as a virtual partition, displaying its contents as a list or icon grid, with the ability to navigate into subdirectories.

**User value**: The counterpart to Fences' Folder Portals. It brings a frequently used project directory straight onto the desktop — more natural than creating a partition and filling it with shortcuts.

**Implementation outline**:
- Add `Models/FolderPortal.cs` (path, display mode, whether to show the folder icon, whether to hide the original directory title).
- Keep it separate from `DesktopIconService`: a portal does not manage real desktop icons, only directory enumeration plus `ShellUtil` open operations.
- Enumerating network directories is blocking I/O, so it must run on a background thread with caching — `GeneralSettings` already has precedents such as `HiddenStoreDirectory`.

**Effort**: L　**Value**: High
**Dependencies and risks**: **High.** F-18 shares the "partition content model" core change with F-17; design the data structures together or implement F-18 right after F-17.

#### F-19 Theme packs / appearance marketplace

**Description**: Bundle a set of appearance parameters (accent color, radii, title bar font, shadow, scrollbar style) into importable/exportable theme files.

**User value**: Lets users switch visual styles in one action and share a "same desktop" look with the community.

**Implementation outline**:
- The theme model maps one-to-one onto the tokens in `Styles/Theme.xaml`; at runtime, override token values in `Application.Current.Resources` (the standard WPF theming approach).
- Import/export as JSON, stored alongside `GeneralSettings`.

**Effort**: M　**Value**: Low
**Dependencies and risks**: Low. The precondition is that `Theme.xaml`'s token set is complete enough to be replaced wholesale (see §5.1).

#### F-20 Localization (English UI)

**Description**: Extract UI strings from code and XAML into resources, supporting Chinese and English (system-driven or manually chosen).

**User value**: Layouter is open source with an international GitHub audience, and Fences supports multiple languages including Simplified Chinese. Layouter's interface is currently hardcoded Simplified Chinese throughout — `TrayMenuService`, `PartitionManagerViewModel`, and the strings in `UpdateWindow` are the obvious examples — which is a hard blocker for localization.

**Implementation outline**:
- Create `Resources/Strings.zh-CN.xaml` and `Strings.en-US.xaml`, replacing hardcoded strings via `{x:Static}` or binding.
- Prioritize `UpdateWindow.xaml` version notes, `TrayMenuService` menu titles, and `PartitionManagerViewModel` prompt text.

**Effort**: M　**Value**: Medium
**Dependencies and risks**: Medium. The work is broad but mechanical; doing it together with F-06 avoids two passes over every UI file.

---

## 5. UI and Visual Refinement Proposals

### 5.1 Design Token Baseline

The unification direction is already set: `Layouter/Styles/Theme.xaml` holds semantic tokens (color, type scale, radii, spacing), `Layouter/Styles/AppControls.xaml` holds control styles built on those tokens, and `Styles/TrayIconResources.xaml` holds tray-specific resources. The following is the precondition for every proposal in this section — **the tokens must be filled in first, or each screen will re-hardcode colors**.

The following semantic tokens are recommended for `Theme.xaml` (names TBD; the principle is semantic naming, not value naming):

| Token category | Purpose | Suggested initial value |
|---|---|---|
| `Accent` / `AccentHover` / `AccentPressed` | Accent color for primary buttons, selected states, guides | `#2563EB` plus light/dark variants |
| `Surface` / `SurfaceElevated` / `SurfaceSunken` | Cards, inset regions, input backgrounds | Three-step gray scale |
| `Border` / `BorderStrong` | Regular and emphasized outlines | 1px low-saturation gray |
| `TextPrimary` / `TextSecondary` / `TextDisabled` | Three text levels | |
| `TitleBarForeground` / `TitleBarBackground` | Default partition title-bar colors (currently only settable per partition) | Leave empty to distinguish "unset" from "set to this value" |
| `RadiusCard` / `RadiusControl` | Corner radii | 8px (card) / 6px (control) |
| `SpaceX1..X6` | Spacing scale | 4 / 8 / 12 / 16 / 24 / 32 |
| `IconSizeSm/Md/Lg` | Icon sizes | 16 / 20 / 24 |
| `ScrollbarWidth` | Thin scrollbar width | 6px |
| `ShadowCard` | Card shadow | Low opacity, high blur |

Three hard rules:

1. **No literal colors in any view.** `TrayMenuService.CreateMenuItem(..., Colors.DodgerBlue)` is the canonical example — DodgerBlue looks close to `#2563EB` but is not the same color, and mixing them makes the accent inconsistent. Pull colors from the resource dictionary instead.
2. **Partition title-bar colors become optional, not required.** `PartitionSettings.TitleForeground` / `TitleBackground` are `Color` value types and cannot express "unset." Switch to `Color?` or add a `UseThemeColor` flag, so a new default theme can apply to existing users.
3. **Dark mode is out of scope for this round.** `Theme.xaml` has no theme-switching mechanism today. Introducing dark mode would also affect every control template and icon tinting (F-11). Open it as a separate effort once the token system stabilizes.

### 5.2 On-Desktop Partition Window

Files: `Layouter/Views/DesktopManagerWindow.xaml`, `Views/DesktopManagerWindow.xaml.cs`

Current structure: `MainBorder` (`CornerRadius="8"`) → `TitleBar` (`Height="30"`) → `IconsContainer` (`CornerRadius="0,0,8,8"`). The card shape is already there; what is missing is **state expression** and **interaction feedback**.

| ID | Proposal | Current state | Recommendation |
|---|---|---|---|
| U-01 | Title-bar buttons appear on demand | `NewPartitionButton` / `LockButton` / `SettingsButton` are always visible | Default to low opacity (0.35), transition to 1.0 on mouse enter over `TitleBar` and back on leave. Use a 120ms `Storyboard` opacity transition so it does not feel abrupt |
| U-02 | Consistent lock-state iconography | `LockIcon` binds `FluentIcons.Common.Symbol.LockOpen`; when locked it should become `LockClosed` | Tint the locked-state icon with `TextDisabled` so it reads at a glance, and sync the `ToolTip` text (currently the hardcoded string "未锁定") |
| U-03 | Distinct visual for the rolled-up state | `IsRolledUp` only affects `IconsRowDefinition` height, with no dedicated styling | When collapsed, turn the title bar into a 4–6px handle strip: keep a 1px accent-colored top edge plus very narrow title text. Expand with a 150ms height animation rather than an instant change |
| U-04 | Drag snapping guides | None | On a hit, draw a 1px `Accent` guide with an 8%-opacity `Accent` fill band; fade out over 200ms after release |
| U-05 | Alignment preview while dragging | None | Leave a 6%-opacity "ghost" outline at the original position during the drag so the user understands the landing point |
| U-06 | Icon hover feedback | Relies on default ListBox behavior | Show a rounded rectangle background on hover (`RadiusControl`) and a 4% scale on press. Take the radius and scale values from tokens rather than hardcoding them |
| U-07 | Visible focus | Partition buttons have no focus styling | Add a 2px `Accent` outer stroke to every focusable element (triggered by `IsKeyboardFocused` on the WPF side) to satisfy keyboard accessibility |
| U-08 | Title-bar height scale | Fixed `Height="30"` | Offer compact (24) / standard (30) / relaxed (36) tiers, linked to `TitleFontSize` so large title text is never clipped |

### 5.3 Manager Windows

Files: `Views/PartitionManagerWindow.xaml`, `Views/PartitionSettingsWindow.xaml`, `Views/UpdateWindow.xaml`

#### 5.3.1 Partition Manager (`PartitionManagerWindow.xaml`, 780×620)

**Information hierarchy problem**: the window currently flattens the global-style toggle, select-all, the partition list, and batch operations together, so users cannot tell at a glance which are global and which act on the current selection. Recommend this structure:

```
┌─ Header ──────────────────────────────────┐
│  Partitions                    [Global style ●]│  ← global toggle on its own row, with tooltip
├───────────────────────────────────────────┤
│  Toolbar: [New] [Edit] [Show] [Hide] [Delete] │  ← acts on the current selection
├───────────────────────────────────────────┤
│  ☑  Work          · 8 icons   [Locked][Rolled] │  ← list rows
│  ☐  Development   · 24 icons               │
│  ...                                       │
├───────────────────────────────────────────┤
│  2 selected                    [Delete selected]│  ← status bar
└───────────────────────────────────────────┘
```

| ID | Proposal | Description |
|---|---|---|
| U-09 | Separate toolbar from batch actions | Promote new/edit/show/hide/delete into a top toolbar acting on the **current selection**; move select-all / invert into the bottom status bar. This prevents users from assuming delete applies to every partition at once |
| U-10 | Empty state | An empty list currently shows nothing at all, which reads as "the app is broken." Show a centered guide block: a 48px FluentIcons glyph (`FluentIcons.Common.Symbol.Grid`), headline "No partitions yet", subtext "Create a partition to gather your desktop icons", a primary "New partition" button (`Accent` fill), and a shortcut hint "Ctrl/⌘ + Shift + N" |
| U-11 | Row density and status chips | Tighten row height to 40–44px; partition name at 13px; on the right use small rounded chips for state (locked / rolled up / hidden) with a `SurfaceSunken` background and `TextSecondary` text — avoid saturated colors |
| U-12 | Icon-count hint | Show the icon count per row (available from `DesktopIconService`) so users can spot the bloated partition; use `TextDisabled` above 30 items |
| U-13 | Undo instead of modal confirmation for destructive actions | Deleting a partition executes immediately today. Replace it with a 5-second "Undo" toast — lighter than a modal dialog and far less costly when triggered by accident |
| U-14 | Search and sort | Once partitions exceed ten, add a search box on the right of the toolbar for name filtering, alongside the F-05 layout-strategy dropdown |
| U-15 | Window sizing and DPI | `MinWidth="640"` overflows on small 125%-scaled screens. Compute the initial size from `SystemParameters.WorkArea` and clamp it to 60%–90% of the work area |

#### 5.3.2 Partition Settings (`PartitionSettingsWindow.xaml`, 460×660)

The current structure is already good: `HeaderTitleText` uses the `AppPageTitleText` style, colors use `ColorSwatchStyle` previews, and the footer has "Reset / Cancel / Save". Recommendations:

| ID | Proposal | Description |
|---|---|---|
| U-16 | Group the settings | A single-column form 460px wide carries too much information density. Split into "Title bar", "Content", and "Icons" groups, each with a small heading and hairline separator, spaced 24px (`SpaceX6`) apart |
| U-17 | Live preview | The existing `PreviewBorder` should reflect every setting change live (not just color), and display a "global style will override this partition" notice when `EnableGlobalStyle` is on |
| U-18 | Slider and value coupling | The values beside `TitleFontSizeSlider` / `OpacitySlider` / `TextSizeSlider` should show the real number and be click-to-edit (the font-size label is currently the hardcoded `Text="14"`) |
| U-19 | Primary / secondary button hierarchy | "Save" is a solid `Accent` primary button, "Cancel" a `Secondary` secondary button, and "Reset" a `TextButton` text button, so the three weights are unmistakable |

#### 5.3.3 Update Window (`UpdateWindow.xaml`, 640×520)

| ID | Proposal | Description |
|---|---|---|
| U-20 | Information hierarchy in the version list | Each entry in `VersionList` should show version number, release date, a summary of changes, and file size. The fixed `rbVersionA` / `rbVersionB` radio buttons are hardcoded mock state and should become an `ItemsControl` with a `DataTemplate` bound to the real version collection |
| U-21 | Download button states | The `Download` button must distinguish "available / downloading / downloaded"; show an indeterminate progress bar and disable repeat clicks while downloading |
| U-22 | Release-page link | The `OpenReleasePage` hyperlink should carry `TextDecorations="None"` and the `Accent` color so the default underline does not break the visual system |

### 5.4 Tray Menu

Files: `Services/TrayMenuService.cs`, `Styles/TrayIconResources.xaml`

**Current problems**: every menu icon uses `Colors.DodgerBlue` (inconsistent with the `#2563EB` design token); "Autostart" is a parent item with an "On/Off" child, using two levels of nesting to express a boolean switch — a clear anti-pattern; and the plugin submenu's "Plugin manager" and "Create plugin" use near-identical glyphs (`PlugConnected` / `PlugConnectedSettings` / `PlugConnectedAdd`).

| ID | Proposal | Description |
|---|---|---|
| U-23 | Flatten boolean switches | Make "Autostart" a directly checkable item (`IsCheckable="True"`) and drop the secondary submenu; two-way bind the check state to `GeneralSettings.AutoStartEnabled` |
| U-24 | Token-consistent menu icons | All menu icons use the `Accent` token from `Theme.xaml`, or differentiate by role (primary action `Accent`, secondary `TextSecondary`) |
| U-25 | Partition quick panel | Per F-02. Placed directly below "New partition" as a first-level submenu with entries like `☑ Work` / `☐ Development` |
| U-26 | Grouping and separators | Use separators to split the menu into three groups: **Actions** (new partition, create, focus mode), **Partitions** (the list), and **More** (plugins, settings, check for updates, exit) |
| U-27 | Version and update check | Add a muted "v2.0.0 · Check for updates" line at the bottom that opens `UpdateWindow`. The tray currently shows no version, so users cannot tell whether they are up to date |
| U-28 | Icon differentiation | Choose semantically distinct FluentIcons glyphs for "Plugins", "Plugin manager", and "Create plugin" instead of three similar ones |
| U-29 | Radii and padding | `TrayMenuStyle` should adopt a unified radius (`RadiusCard`) and spacing scale; 32px item height, 16px icons, using the same metrics as the buttons in `AppControls.xaml` |

### 5.5 Plugin Windows

Files: `Plugins/PluginsManagerWindow.xaml`, `Plugins/Views/FloatingWindow.xaml`, `Plugins/Views/DetailedTemplateWindow.xaml`, `Plugins/Controls/CodeEditor.xaml`

| ID | Proposal | Description |
|---|---|---|
| U-30 | Card-ify the plugin list | Plugin entries in `PluginsManagerWindow` become `RadiusCard` rounded cards containing icon, name, version, enable toggle, and status tag; grid layout with `SpaceX4` gaps |
| U-31 | Semantic status colors | Enabled / disabled / load-failure use status dots (`Accent` / `TextDisabled` / error red) rather than text labels, reducing visual noise |
| U-32 | Bring the floating window into the same system | `FloatingWindow` currently belongs to a separate visual language. Unify it with `Theme.xaml` tokens: `SurfaceElevated` background, `Surface` title bar, `Border` outline, `RadiusCard` corners |
| U-33 | Code editor theme linkage | `CodeEditor.xaml` uses AvalonEdit with a hardcoded VS dark syntax theme. Read colors from `Theme.xaml` instead (keywords use `Accent`, strings and comments use desaturated `TextSecondary` variants) so the editor matches the application theme |
| U-34 | Editor toolbar | Add a slim toolbar above the editor: Run, Save, Format, font size. Plugin development is Layouter's signature capability and deserves dedicated polish |
| U-35 | Complete the template reference | `DetailedTemplateWindow`, which presents a detailed template, should list the classes, properties, and lifecycle hooks it contains and link to the corresponding anchors in `docs/plugin_development_cn.md`, closing the documentation-to-code loop |
| U-36 | Inline plugin failure reporting | When a plugin fails to load, show the failure reason inline in `PluginsManagerWindow` (taken from the last Serilog exception) rather than leaving it only in the log file |

---

## 6. Recommended Roadmap

### 6.1 Near Term (Next 1–2 Releases)

Goal: **fix the hard problem that everyday actions are not mouse-reachable**, plus one lightweight engineering cleanup.

| Order | Proposal | Rationale |
|---|---|---|
| 1 | **F-06** Documentation and version consistency | Cheapest item, and establishes a credible baseline for every subsequent release |
| 2 | **F-03** Global hotkey framework | F-01 and F-07 both depend on it, so the hotkey layer must come first |
| 3 | **F-02** Tray partition quick panel | Immediately useful efficiency win with a small change surface |
| 4 | **F-01** Quick partition show/hide + focus mode | Combined with F-02 and F-03, forms a complete "fast actions" loop |
| 5 | **F-04** Partition drag snapping and equal-spacing guides | The core feel of partition use; ship screen-edge snapping first |
| 6 | **F-05** One-click layout presets | Reuses the geometry code from F-04 |
| 7 | **U-01 – U-08** On-desktop partition window visuals | First beneficiaries of the token system |
| 8 | **U-09 – U-15** Partition manager window visuals | Same |
| 9 | **U-23 – U-29** Tray menu visuals | Delivered alongside F-02 |

**Not recommended for this horizon**: F-08 (multi-monitor), F-17 (tabs), F-18 (folder portals). Reasoning in §6.4.

### 6.2 Mid Term

Goal: **complete desktop-level scenario switching**, and build out reliability.

| Order | Proposal | Rationale |
|---|---|---|
| 1 | **F-08 prerequisite: DPI awareness upgrade** (PerMonitorV2 + `WM_DPICHANGED`) | The foundation for every geometry-related feature. It is not in the proposal table, but it is a correctness prerequisite for F-04, F-05, and F-09, and it must start in the mid term |
| 2 | **F-08** Multi-monitor partition ownership | Only after this does Layouter genuinely suit dual-screen users |
| 3 | **F-09** Layout snapshots | Depends on the F-05 layout-pipeline split |
| 4 | **F-14** Config backup, restore, and migration | Protects user assets, and doubles as the safety net before the F-17 / F-18 data-model changes |
| 5 | **F-15** Safe mode and crash recovery | The safety net for the plugin ecosystem |
| 6 | **F-11** Icon tinting | Low-risk visual payoff |
| 7 | **F-12 / F-13** Batch styling and opacity decoupling | Completes the styling system |
| 8 | **F-10** Icon sorting and automatic grouping rules | Ship sorting first; defer the rule engine until the plumbing is stable |
| 9 | **F-16** Plugin API versioning | Cheapest while the plugin ecosystem is still small |
| 10 | **U-16 – U-22 / U-30 – U-36** Remaining visual unification | Finish |

### 6.3 Long Term

Goal: **evolve from a partition container into a desktop content manager**.

| Order | Proposal | Rationale |
|---|---|---|
| 1 | **F-07** Peek desktop preview mode | Affordable once the hotkey framework exists, but Z-order manipulation is risky, so ship it as a disableable experimental feature |
| 2 | **F-17** Partition tabs | Requires F-14 config migration and F-08 multi-monitor as prerequisites |
| 3 | **F-18** Folder portals | Shares the content model with F-17; implement them adjacently |
| 4 | **F-20** Localization | A function of community size; place after feature stability |
| 5 | **F-19** Theme packs | Nice-to-have, lowest priority |

**A strategic note for the long term**: once F-17 and F-18 land, Layouter's data structures will have genuinely diverged from the "partition = icon container" model. That is the moment to bump the plugin API in `Plugins/PluginEntry` to v3 so plugins can reach tab and portal content. That is the path by which the plugin system becomes a genuine moat against Fences.

### 6.4 Explicitly Not Recommended

The following ideas look attractive but do not pay for themselves in a small open-source project. They are recorded here to avoid relitigating them.

| Idea | Why not |
|---|---|
| **AI icon classification / natural-language layout** | First, Fences 6 **has no AI features** — Stardock's AI capabilities live in separate products such as DesktopGPT and Clairvoyance, so AI is not a category threshold. Second, implementing it means model dependencies, API key management, network behavior, and privacy disclosure, all of which work against a "simple, quiet, offline-capable" positioning. If this is ever built, put it in the plugin system rather than the core. |
| **Virtual desktops / custom desktops** | Directly conflicts with Windows 10/11's own virtual desktop concept; running two desktop-switching models side by side would thoroughly confuse users. It also needs substantial Shell-level hacks for uncertain benefit. |
| **Built-in file manager / replacing Explorer** | That is a different product category (the Total Commander class). Layouter's purpose is desktop organization, not file management. A half-built version satisfies neither audience. |
| **Taskbar replacement / full dock integration** | Extremely invasive, collides head-on with mature tools such as Start11, ExplorerPatcher, and WindowBlinds, and can break with every Windows update. A small project cannot carry that maintenance burden. |
| **Built-in scripting engine (à la Fences enterprise scripting)** | High security risk (user scripts execute inside the main process), high sandboxing cost, heavy maintenance burden. If users need automation, the existing plugin system is the better vehicle — extending the `PluginEntry` API beats inventing a scripting language from scratch. |
| **Online accounts / cloud sync / theme marketplace** | Requires servers and operations, a long-term liability for a single-maintainer open-source project. If sync is wanted, local file sync (cloud drive / Git) is sufficient. |
| **macOS / Linux port** | Layouter depends deeply on the Windows Shell (`ShellUtil.cs`, `ShortcutUtil.cs`, the icon-view implementation in `DesktopIconService`). Cross-platform means rewriting. |
| **3D desktop / animated effects / wallpaper rotation** | Directly contradicts the "simple, quiet, distraction-free" positioning. Fences, after all these years, still sticks to restrained glass and shadows. |
| **Built-in screenshot / annotation / clipboard tools** | A crowded space unrelated to the positioning, and Windows' built-in snipping tool is already sufficient. |
| **Replacing the desktop icon view wholesale (custom Shell icon view)** | Fences and Layouter both work within the bounded scope of "hosting some icons inside an icon view," because fully taking over Explorer's icon view breaks drag-and-drop, thumbnails, context menus, and search. Layouter's current approach is the safe one; do not cross that line. |

---

## 7. References

The sources below were actually consulted in preparing this document. All Fences feature descriptions are based on Stardock's own first-party materials; anything that could not be verified is explicitly marked in §2.2.

**Fences product page and help center (first-party)**

1. Stardock Fences 6 product page — https://www.stardock.com/products/fences/
2. Official Fences wiki: How to Customize Stardock Fences — https://wiki.stardocksoftware.com/index.php/How_to_Customize_Stardock_Fences
3. Stardock Help Center: Fences Information & Usage — https://support.stardock.com/space/SHC/pages/1331789828/Fences+Information+Usage
4. Fences 6.58 changelog (2026-10-14, current latest) — https://stardock.atlassian.net/wiki/spaces/SHC/pages/3856138244/Fences+6.58+Changelog+Released+10+14+2026
5. Fences v6.54 changelog (2026-08-18) — https://stardock.atlassian.net/wiki/spaces/SHC/pages/3799187483/Fences+v6.54+Changelog+Released+8+18+2026
6. Fences v6.52 changelog (2026-08-04) — https://stardock.atlassian.net/wiki/spaces/SHC/pages/3613065236/Fences+v6.52+Changelog+Released+8+04+2026
7. Fences 6.46 changelog (2026-04-27) — https://stardock.atlassian.net/wiki/spaces/SHC/pages/2957279305/Fences+6.46+Changelog+Released+4+27+2026
8. Fences 6.2.0.1 changelog (2025-12-09) — https://stardock.atlassian.net/wiki/spaces/SHC/pages/2506883119/Fences+6.2.0.1+Changelog+Released+12-09-2025
9. Fences 6.0 changelog (2025-05-28, general availability) — https://stardock.atlassian.net/wiki/spaces/SHC/pages/2463989872/Fences+6.0+Changelog+Released+5+28+2025
10. Fences 6 Public Beta 2 changelog (2025-05-07) — https://stardock.atlassian.net/wiki/spaces/SHC/pages/2455502876/Fences+6+Public+Beta+2+Changelog+Released+05+07+2025
11. Stardock forums: Fences 6.0 release feedback — https://forums.stardock.com/536148/fences-6-0-release-feedback

**Secondary benchmarks**

12. Stardock Groupy 2 product page — https://www.stardock.com/products/groupy/
13. Microsoft Support: Snap Your Windows (Snap Layouts / Snap Assist / Snap bar) — https://support.microsoft.com/en-us/windows/experience/snap-your-windows
14. Microsoft Learn: Support snap layouts for desktop apps (how the layout flyout is invoked, Win + Z / hovering the maximize button) — https://learn.microsoft.com/en-us/windows/apps/desktop/modernize/ui/apply-snap-layout-menu
15. Microsoft Tech Community: Mastering Snap Layouts and Snap Groups in Windows 11 — https://techcommunity.microsoft.com/discussions/windows11/mastering-snap-layouts-and-snap-groups-in-windows-11/4444796

**Search entry points (used to locate the first-party pages above)**

16. DuckDuckGo HTML search: Stardock Fences 2025 features portal windows snap — https://html.duckduckgo.com/html/?q=Stardock+Fences+2025+features+portal+windows+snap
17. DuckDuckGo HTML search: Stardock Fences 6 AI features Clairvoyance DesktopGPT — https://html.duckduckgo.com/html/?q=Stardock+Fences+6+AI+features+Clairvoyance+DesktopGPT+desktop+organizer
18. DuckDuckGo HTML search: Stardock Groupy 2 features tabs windows grouping — https://html.duckduckgo.com/html/?q=Stardock+Groupy+2+features+tabs+windows+grouping
19. DuckDuckGo HTML search: Microsoft Windows 11 Snap Layouts official support page — https://html.duckduckgo.com/html/?q=Microsoft+Windows+11+Snap+Layouts+official+support+page

**Internal repository basis** (all class names and file paths cited in this document come from the following)

- `Layouter/Layouter.csproj`, `Layouter/docs/readme_cn.md`, `Layouter/docs/readme.md`, `Layouter/docs/changelog.md`, `Layouter/docs/DevelopPlan.md`
- `Layouter/Models/`: `PartitionSettings.cs`, `PartitionData.cs`, `DesktopIcon.cs`, `IconPartition.cs`, `GeneralSettings.cs`
- `Layouter/Services/`: `WindowManagerService.cs`, `DesktopIconService.cs`, `PartitionDataService.cs`, `PartitionSettingsService.cs`, `GeneralSettingsService.cs`, `TrayMenuService.cs`, `TrayIconService.cs`
- `Layouter/ViewModels/`: `DesktopManagerViewModel.cs`, `PartitionManagerViewModel.cs`, `TrayIconViewModel.cs`
- `Layouter/Views/`: `DesktopManagerWindow.xaml`, `PartitionManagerWindow.xaml`, `PartitionSettingsWindow.xaml`, `UpdateWindow.xaml`
- `Layouter/Styles/`: `Theme.xaml`, `AppControls.xaml`, `TrayIconResources.xaml`
- `Layouter/Plugins/`: `PluginManager.cs`, `PluginLoader.cs`, `PluginsManagerWindow.xaml`, `Views/FloatingWindow.xaml`, `Controls/CodeEditor.xaml`
- `Layouter/Utility/`: `SysUtil.cs`, `ShellUtil.cs`, `ShortcutUtil.cs`

---

*This document proposes only; it does not represent implemented functionality. Effort and value ratings are design-stage estimates, and actual scheduling should be adjusted against available manpower and community feedback at the time.*
