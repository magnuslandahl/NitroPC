# AGENTS.md — NitroPC Implementation Instructions

> **READ THIS FIRST.** Follow these rules strictly from start to finish. Do not improvise architecture when an exact specification is already provided.

## 1. Required Reading Before Writing Any Code
1. Read [`docs/UI_SPEC.md`](docs/UI_SPEC.md) and inspect the images in [`docs/images/`](docs/images/) (plus [`docs/ui-preview.html`](docs/ui-preview.html)) to see the exact visual layout, colors, states, and typography.
2. Read [`docs/IMPLEMENTATION_PLAN.md`](docs/IMPLEMENTATION_PLAN.md) completely and implement **Phase 1 through Phase 7 in exact order**.

## 2. Build & Test Commands
- **Target Framework**: `net10.0-windows` (`.NET 10` SDK is installed on this machine).
- **Do NOT add a `global.json` pinning `.NET 8.0`** — keep the project on `net10.0-windows`.
- **Do NOT add unnecessary NuGet packages** in `src/NitroPC/NitroPC.csproj` — all Win32 telemetry, keep-awake input simulation, taskbar docking, JSON settings, registry startup, and tray icon features use built-in `.NET 10` WPF + WinForms + Win32 P/Invoke APIs. (`tests/NitroPC.Tests` uses standard `xunit`).
- Run after **every** phase:
  ```powershell
  dotnet build NitroPC.sln
  dotnet test NitroPC.sln --nologo
  ```

## 3. Critical Pitfalls to Avoid (Mandatory Rules)

1. **WPF `#AARRGGBB` Hex Colors (NOT CSS `#RRGGBBAA`)**:
   - In WPF XAML and `ColorConverter.ConvertFromString`, 8-digit hex is **`#AARRGGBB`** (Alpha first!), NOT CSS `#RRGGBBAA`.
   - Always copy the exact `#AARRGGBB` values from the Color Token table in [`docs/UI_SPEC.md`](docs/UI_SPEC.md).

2. **WPF + WinForms Namespace Collisions**:
   - `NitroPC.csproj` sets `<UseWPF>true</UseWPF>` and `<UseWindowsForms>true</UseWindowsForms>` (for `NotifyIcon` and `Screen`).
   - **NEVER** put `using System.Windows.Forms;` or `using System.Drawing;` at the top of WPF files — it causes `CS0104` ambiguity errors on `Application`, `Window`, `Color`, `Brush`, `Point`, `Control`, `Timer`, and `MouseEventArgs`.
   - Only use WinForms/System.Drawing inside `Services/TrayIconService.cs` and window-bounds helpers via explicit alias: `using WinForms = System.Windows.Forms;`.

3. **CPU Percentage Formula (`KernelTime` Includes `IdleTime`)**:
   - In `NtQuerySystemInformation(SystemProcessorPerformanceInformation)`, Windows includes `IdleTime` inside `KernelTime`.
   - You **MUST** compute:
     ```csharp
     long dIdle   = curr.IdleTime   - prev.IdleTime;
     long dKernel = curr.KernelTime - prev.KernelTime;
     long dUser   = curr.UserTime   - prev.UserTime;
     long total   = dKernel + dUser;
     long busy    = (dKernel - dIdle) + dUser;
     double pct   = total > 0 ? Math.Clamp((double)busy / total * 100.0, 0.0, 100.0) : 0.0;
     ```
   - Forgetting `- dIdle` makes an idle PC report ~100% CPU.

4. **x64 `SendInput` Struct Size Must Be 40 Bytes**:
   - On 64-bit Windows, `user32.dll!SendInput` silently returns `0` if `Marshal.SizeOf<INPUT>()` is not `40`.
   - The `InputUnion` struct **MUST** include `MOUSEINPUT` alongside `KEYBDINPUT` (because `MOUSEINPUT` determines the union size). Copy `Interop/NativeMethods.cs` verbatim from `docs/IMPLEMENTATION_PLAN.md`.

5. **Physical Pixels vs WPF DIPs When Docking in the Taskbar**:
   - `GetWindowRect` and `SetWindowPos` use **physical screen pixels**.
   - WPF `ActualWidth` / `ActualHeight` / `Left` / `Top` use **Device-Independent Pixels (DIPs, 96 DPI)**.
   - Multiply WPF DIPs by `VisualTreeHelper.GetDpi(window).DpiScaleX` (and `DpiScaleY`) when calling `SetWindowPos`, and divide physical pixels by `DpiScaleX` / `DpiScaleY` when setting WPF `Left` / `Top`.

6. **App Lifecycle & Flyout Toggle Debounce**:
   - Set `ShutdownMode="OnExplicitShutdown"` in `App.xaml` and do **NOT** set `StartupUri` (create windows in `App.OnStartup`).
   - Record `_lastDeactivatedUtc = DateTime.UtcNow` when `ToolboxFlyoutWindow.Deactivated` fires and ignore strip clicks within `180ms` if the flyout just closed on deactivation, preventing the flyout from immediately reopening when clicking the taskbar strip to close it.
   - Always reset `SetThreadExecutionState(ES_CONTINUOUS)` and dispose `NotifyIcon` on `Application.Exit`.

7. **Update Package Integrity & Replacement**:
   - Select only the exact architecture-specific ZIP and exactly one
     `SHA256SUMS.txt` entry from `magnuslandahl/NitroPC` GitHub release assets.
   - Bind downloads to immutable numeric GitHub asset IDs, verify the declared
     size and SHA-256, and reject unsafe ZIP paths.
   - Run the copied `NitroPC.Updater.exe` from a temporary directory only after
     NitroPC exits; never replace locked files from the main WPF process.
   - Keep [`docs/AUTO_UPDATE.md`](docs/AUTO_UPDATE.md) aligned with release
     packaging and updater behavior.

8. **Quiet Mode Safety**:
   - Change only the active scheme's AC/DC `Processor performance boost mode`
     (`PERFBOOSTMODE`) setting. Never write fan curves, maximum processor
     state, OEM firmware controls, or unrelated power settings.
   - Persist the original plan/value tuple before applying boost mode `0`,
     restore it on exit, and recover/clear the marker on the next launch after
     an interrupted session.
