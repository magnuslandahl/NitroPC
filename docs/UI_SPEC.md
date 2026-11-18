# NitroPC — UI & Visual Specification

This document defines the exact visual design, layout metrics, colors, icons, and interaction states for **NitroPC**.

> **Interactive Reference**: Open [`docs/ui-preview.html`](ui-preview.html) in any browser to inspect spacing, animations, and interactive toggles live.

---

## 1. Visual Reference Gallery

### Original Inspiration (CPU + RAM Strip)
![Original Reference Bar](images/reference-bar.png)

### NitroPC Taskbar Strip — Awake State (`1.45×` Close-Up)
![Taskbar Strip Close-Up — Awake](images/ui-strip-closeup.png)

### NitroPC Taskbar Strip — Idle State (`1.45×` Close-Up)
![Taskbar Strip Close-Up — Idle](images/ui-strip-idle-closeup.png)

### NitroPC Toolbox Flyout + Docked Windows 11 Taskbar
![Flyout and Taskbar Close-Up](images/ui-flyout-closeup.png)

### Full Concept Overview — Awake vs Idle
| Awake State | Idle State |
| --- | --- |
| ![Full Concept Awake](images/ui-full-concept.png) | ![Full Concept Idle](images/ui-idle-state.png) |

---

## 2. Color Tokens (Exact WPF `#AARRGGBB` Values)

> **WARNING FOR WPF XAML**: WPF uses `#AARRGGBB` (Alpha channel first!), **NOT** CSS `#RRGGBBAA`. Copy the **WPF XAML Hex** column directly into `Themes/NitroTheme.xaml`.

| Token Name | Purpose | CSS Equivalent | WPF XAML Hex (`#AARRGGBB`) |
| --- | --- | --- | --- |
| `StripBackgroundBrush` | Taskbar strip pill background (subtle translucent dark slate) | `rgba(35, 56, 64, 0.85)` | `#D9233840` |
| `StripHoverBrush` | Taskbar strip hover background | `rgba(48, 74, 84, 0.92)` | `#EB304A54` |
| `StripBorderBrush` | Subtle 1px border on strip | `rgba(255, 255, 255, 0.10)` | `#1AFFFFFF` |
| `FlyoutBackgroundBrush` | Main flyout window background | `rgba(24, 36, 43, 0.96)` | `#F518242B` |
| `FlyoutBorderBrush` | Main flyout 1px outer border | `rgba(255, 255, 255, 0.14)` | `#24FFFFFF` |
| `CardBackgroundBrush` | Inner telemetry / Keep Awake card fill | `rgba(255, 255, 255, 0.05)` | `#0DFFFFFF` |
| `CardBorderBrush` | Inner card 1px border | `rgba(255, 255, 255, 0.07)` | `#12FFFFFF` |
| `DividerBrush` | Vertical 1px separator on taskbar strip | `rgba(255, 255, 255, 0.16)` | `#29FFFFFF` |
| `TextPrimaryBrush` | Primary metric numbers & titles | `#F3F7F9` | `#FFF3F7F9` |
| `TextMutedBrush` | Uppercase labels (`CPU`, `RAM`) & subtitles | `#97AEB7` | `#FF97AEB7` |
| `CpuCoralBrush` | CPU vertical bars & CPU % highlight | `#F3A683` | `#FFF3A683` |
| `RamBlueBrush` | RAM sparkline stroke, RAM %, active switch | `#6EC6FF` | `#FF6EC6FF` |
| `RamFillBrush` | RAM sparkline area fill under curve | `rgba(110, 198, 255, 0.25)` | `#406EC6FF` |
| `SparkGuideBrush` | Top dashed ceiling guide line in RAM sparkline | `rgba(255, 255, 255, 0.20)` | `#33FFFFFF` |
| `KaffeAmberBrush` | Active Keep-Awake icon, progress bar & Stop button | `#FBBF24` | `#FFFBBF24` |
| `KaffeGlowFillBrush` | Active Keep-Awake badge background | `rgba(251, 191, 36, 0.18)` | `#2EFBBF24` |
| `KaffeGlowBorderBrush` | Active Keep-Awake badge border | `rgba(251, 191, 36, 0.35)` | `#59FBBF24` |
| `KaffeLabelAwakeBrush` | `AWAKE` small label when active | `#FCD34D` | `#FFFCD34D` |
| `KaffeValueAwakeBrush` | `49m` / `∞` value text when active | `#FEF3C7` | `#FFFEF3C7` |
| `KaffeIdleIconBrush` | Coffee cup icon when idle | `#7E969F` | `#FF7E969F` |

---

## 3. Component Dimensions & Typography (in WPF DIPs)

- **Font Family**: `Segoe UI Variable Display, Segoe UI`
- **Numeric alignment**: Use `Typography.NumeralAlignment="Tabular"` on metric `TextBlock` elements so percentages don't jitter horizontally as numbers change.

### A. Taskbar Strip (`TaskbarStripWindow`)
- **Outer Window Height**: Matches taskbar height dynamically (on standard Windows 11 taskbar = `48` DIPs; inner pill height = `36` DIPs, vertically centered, `CornerRadius="7"`, horizontal padding `10,0`).
- **Width**: `SizeToContent="Width"` (typically `~220–250` DIPs with badge, `~155` DIPs without badge).
- **Horizontal Element Sequence** (gap `10` DIPs between sections):
  1. **CPU Unit**:
     - `CpuBarsControl`: `Width="50"`, `Height="18"`, **14 vertical rounded bars** (`barWidth = 2.2`, `gap = 1.4`, `cornerRadius = 1.1`, minimum bar height = `18%` of control height so idle cores still render visible coral ticks).
       - *Note*: If the PC has `> 14` logical cores (e.g., 24 cores), downsample/average adjacent cores into 14 buckets for the strip so the width stays fixed at `50` DIPs!
     - Stacked Text (`Margin="7,0,0,0"`):
       - Top: `CPU` (`FontSize="9"`, `FontWeight="Bold"`, `Foreground="{StaticResource TextMutedBrush}"`)
       - Bottom: `24%` (`FontSize="13.5"`, `FontWeight="Bold"`, `Foreground="{StaticResource TextPrimaryBrush}"`)
  2. **Divider 1**: `Border` `Width="1"`, `Height="20"`, `Background="{StaticResource DividerBrush}"`.
  3. **RAM Unit**:
     - `RamSparklineControl`: `Width="44"`, `Height="20"`:
       - Top dashed horizontal guide line at `Y = 3` (`StrokeThickness = 1`, dash `3,3`, `SparkGuideBrush`).
       - Smooth polyline/curve of last `12` RAM % samples (`0–100%` mapped to `Y = Height - 2` .. `Y = 3`), filled underneath with `RamFillBrush` and stroked on top with `RamBlueBrush` (`StrokeThickness = 2.0`).
     - Stacked Text (`Margin="7,0,0,0"`):
       - Top: `RAM` (`FontSize="9"`, `FontWeight="Bold"`, `Foreground="{StaticResource TextMutedBrush}"`)
       - Bottom (`RamDisplayMode` setting):
         - `Percent` (default): `64%`
         - `Gigabytes`: `21 GB` (or `20.5 GB`)
         - `Both`: `64% · 21G`
  4. **Divider 2** (hidden when `KeepAwakeBarMode == Hidden`): `Width="1"`, `Height="20"`.
  5. **Keep-Alive ("Kaffe") Slot** (controlled by `KeepAwakeBarMode`):
     - `Badge` (default): Pill `CornerRadius="6"`, `Padding="6,3"`:
       - **When Awake**: `Background="{StaticResource KaffeGlowFillBrush}"`, `BorderBrush="{StaticResource KaffeGlowBorderBrush}"`, `BorderThickness="1"`, amber coffee icon (`15×15`), `AWAKE` (`FontSize="8.5"`, `KaffeLabelAwakeBrush`) over remaining minutes (`49m`, `2h 15m`, or `∞`, `FontSize="13"`, `KaffeValueAwakeBrush`).
       - **When Idle**: `Background="Transparent"`, `BorderBrush="Transparent"`, muted coffee icon (`KaffeIdleIconBrush`), `IDLE` (`TextMutedBrush`) over `Off` (`TextPrimaryBrush`).
     - `CompactIcon`: Shows only the `15×15` coffee icon (amber glow when Awake, muted gray when Idle) with tooltip showing remaining time.
     - `Hidden`: Hides Divider 2 and Keep-Alive slot on the taskbar strip (controlled via Flyout / Tray icon).

### Coffee Cup Vector Path (WPF `Path.Data`)
Use a `Viewbox` (`Width="15" Height="15"`) containing a `Canvas` (`Width="24" Height="24"`) with this exact stroke path (`StrokeThickness="2.2"`, `StrokeStartLineCap="Round"`, `StrokeEndLineCap="Round"`):
```xml
<Viewbox Width="15" Height="15">
    <Canvas Width="24" Height="24">
        <Path Data="M 17,8 H 18 A 4,4 0 1 1 18,16 H 17 M 3,8 H 17 V 17 A 4,4 0 0 1 13,21 H 7 A 4,4 0 0 1 3,17 Z M 6,2 V 4 M 10,2 V 4 M 14,2 V 4"
              Stroke="{Binding ...}"
              StrokeThickness="2.2"
              StrokeStartLineCap="Round"
              StrokeEndLineCap="Round"
              StrokeLineJoin="Round" />
    </Canvas>
</Viewbox>
```

---

### B. Toolbox Flyout (`ToolboxFlyoutWindow`)
- **Size**: `Width="352"`, `SizeToContent="Height"`, `CornerRadius="12"`, `Padding="16"`, outer margin `12` (room for subtle drop shadow).
- **Anchor Position**: `10` DIPs above the top of the Windows taskbar, right-aligned with `TaskbarStripWindow` (clamped to `Screen.WorkingArea` so it never goes off-screen).
- **Sections** (`12` DIP vertical spacing):
  1. **Header Row**:
     - Left: `24×24` rounded gradient badge (`#FFF3A683` -> `#FF6EC6FF`, bold dark `N`) + `NitroPC Toolbox` (`FontSize="13.5"`, `FontWeight="SemiBold"`).
     - Right: Status pill (`● Awake · 00:48:19` in amber when active, `● Idle` in muted gray when inactive).
  2. **2-Column Telemetry Grid** (`Gap = 10` DIPs):
     - **Left Card — CPU Load**:
       - Header: `CPU LOAD` + large coral `24%` (`FontSize="20"`, `FontWeight="Bold"`).
       - Graph: `CpuBarsControl` (`Height="28"`, up to `16` bars).
       - Subtitle: `{CoreCount} logical cores`.
     - **Right Card — Memory**:
       - Header: `MEMORY` + large sky-blue `64%` (`FontSize="20"`, `FontWeight="Bold"`).
       - Graph: `RamSparklineControl` (`Height="28"`, `24` history samples).
       - Subtitle: `{UsedGb:0.0} / {TotalGb:0.0} GB used`.
  3. **Keep Awake ("Kaffe") Card**:
     - Highlighted border (`KaffeGlowBorderBrush`) when active.
     - Top row: `☕ Keep Awake` title + status subtitle (`Active — prevents sleep, lock & Teams away` vs `Paused — normal Windows sleep & idle rules`) + **Start / Stop** button (`#FFFBBF24` amber background with dark text `#FF171205` when Awake; subtle `#1AFFFFFF` button with white text when Idle).
     - Preset row: 5 preset buttons (`30m`, `1h`, `2h`, `4h`, `∞`) + Custom Minutes `TextBox` (`Width="48"`, integer `1..1440`, pressing `Enter` or clicking `Start` applies custom minutes and saves to settings). Clicking any preset immediately starts Keep Awake for that duration!
     - Countdown progress bar (`Height="3"`, fills proportionally `remaining / total`, `100%` when `∞`, `0%` when Idle).
  4. **Settings / Display Row**:
     - Compact segmented selector or cycle pills for:
       - **RAM on Bar**: `%` | `GB` | `% + GB`
       - **Awake on Bar**: `Badge` | `Icon` | `Hide`
  5. **Footer Row**:
     - Left: Windows 11-style toggle switch for **Start with Windows**.
     - Right: Subtle **Quit** text button (`Quit`) to cleanly exit NitroPC.

---

## 4. Mouse & Tray Interactions
- **Left-click anywhere on `TaskbarStripWindow`**: Opens/closes `ToolboxFlyoutWindow`.
- **Middle-click on `TaskbarStripWindow`** (shortcut): Toggles Keep-Awake On/Off using the last saved duration!
- **Right-click on `TaskbarStripWindow` OR System Tray Icon**: Opens quick context menu:
  - `Open NitroPC Toolbox`
  - `Keep Awake: 1 Hour` / `2 Hours` / `Indefinite (∞)` / `Stop Keep Awake`
  - `Start with Windows` (checkable)
  - `Exit NitroPC`
