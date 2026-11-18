# NitroPC — Step-by-Step Implementation Plan

> **Target Audience**: Coding agent implementing NitroPC from scratch.
> Follow **Phases 1 → 7** in exact sequential order. After finishing each phase, run `dotnet build NitroPC.sln` and `dotnet test NitroPC.sln` and fix any errors before moving to the next phase.

---

## Context & Background

NitroPC combines two utilities into one sleek Windows 11 taskbar toolbox:
1. **Real-Time Taskbar Gauges**: Coral multi-bar **CPU %** meter and sky-blue sparkline **RAM % / GB** meter docked directly inside the Windows 11 taskbar (`Shell_TrayWnd`, immediately to the left of `TrayNotifyWnd`).
2. **Keep-Alive ("Kaffe") Engine**: Replaces a legacy standalone WPF utility (`Kaffe`, which used a basic gray window, saved `KeepAwakeMinutes` in the registry, and toggled `NumLock` every 60 seconds) with a dual-layer Keep-Alive service (`SetThreadExecutionState` + silent `VK_F15` `SendInput` every 60s) controlled from a dark Windows 11 Flyout and quick taskbar badge.
3. **Start with Windows**: User-level registry auto-start (`HKCU\Software\Microsoft\Windows\CurrentVersion\Run\NitroPC`) requiring zero administrator elevation.

---

## Target Repository Structure

Create this exact project layout:

```text
NitroPC/
├── NitroPC.sln
├── AGENTS.md
├── README.md
├── .gitignore
├── docs/
│   ├── IMPLEMENTATION_PLAN.md
│   ├── UI_SPEC.md
│   ├── ui-preview.html
│   └── images/
├── src/
│   └── NitroPC/
│       ├── NitroPC.csproj
│       ├── app.manifest
│       ├── App.xaml
│       ├── App.xaml.cs
│       ├── Interop/
│       │   └── NativeMethods.cs
│       ├── Models/
│       │   ├── NitroSettings.cs
│       │   └── MetricsSnapshot.cs
│       ├── Helpers/
│       │   ├── CpuCalculator.cs
│       │   └── RingBuffer.cs
│       ├── Services/
│       │   ├── SettingsService.cs
│       │   ├── StartupService.cs
│       │   ├── SystemMetricsService.cs
│       │   ├── KeepAwakeService.cs
│       │   └── TrayIconService.cs
│       ├── Controls/
│       │   ├── CpuBarsControl.cs
│       │   └── RamSparklineControl.cs
│       ├── Themes/
│       │   └── NitroTheme.xaml
│       └── Views/
│           ├── TaskbarStripWindow.xaml
│           ├── TaskbarStripWindow.xaml.cs
│           ├── ToolboxFlyoutWindow.xaml
│           └── ToolboxFlyoutWindow.xaml.cs
└── tests/
    └── NitroPC.Tests/
        ├── NitroPC.Tests.csproj
        ├── CpuCalculatorTests.cs
        ├── RingBufferTests.cs
        ├── KeepAwakeServiceTests.cs
        └── SettingsServiceTests.cs
```

---

## Phase 1: Solution, Project Scaffolding & Models

### Step 1.1 — Create Solution and Projects
Run or create:
1. `NitroPC.sln` referencing `src\NitroPC\NitroPC.csproj` and `tests\NitroPC.Tests\NitroPC.Tests.csproj`.
2. **`src\NitroPC\NitroPC.csproj`** — use this exact configuration (zero external NuGet packages required):
   ```xml
   <Project Sdk="Microsoft.NET.Sdk">
     <PropertyGroup>
       <OutputType>WinExe</OutputType>
       <TargetFramework>net10.0-windows</TargetFramework>
       <Nullable>enable</Nullable>
       <ImplicitUsings>enable</ImplicitUsings>
       <UseWPF>true</UseWPF>
       <UseWindowsForms>true</UseWindowsForms>
       <ApplicationManifest>app.manifest</ApplicationManifest>
       <AssemblyName>NitroPC</AssemblyName>
       <RootNamespace>NitroPC</RootNamespace>
     </PropertyGroup>

     <ItemGroup>
       <InternalsVisibleTo Include="NitroPC.Tests" />
     </ItemGroup>
   </Project>
   ```
   > **IMPORTANT**: Because both `UseWPF` and `UseWindowsForms` are `true`, **do NOT** add `using System.Windows.Forms;` or `using System.Drawing;` globally or in WPF files. Alias them locally where needed (`using WinForms = System.Windows.Forms;`).

3. **`src\NitroPC\app.manifest`** — enable Per-Monitor V2 DPI Awareness so taskbar coordinates are never blurry or virtualized:
   ```xml
   <?xml version="1.0" encoding="utf-8"?>
   <assembly manifestVersion="1.0" xmlns="urn:schemas-microsoft-com:asm.v1">
     <assemblyIdentity version="1.0.0.0" name="NitroPC.app"/>
     <application xmlns="urn:schemas-microsoft-com:asm.v3">
       <windowsSettings>
         <dpiAwareness xmlns="http://schemas.microsoft.com/SMI/2016/WindowsSettings">PerMonitorV2, PerMonitor</dpiAwareness>
         <dpiAware xmlns="http://schemas.microsoft.com/SMI/2005/WindowsSettings">true/pm</dpiAware>
       </windowsSettings>
     </application>
   </assembly>
   ```

4. **`tests\NitroPC.Tests\NitroPC.Tests.csproj`**:
   ```xml
   <Project Sdk="Microsoft.NET.Sdk">
     <PropertyGroup>
       <TargetFramework>net10.0-windows</TargetFramework>
       <Nullable>enable</Nullable>
       <ImplicitUsings>enable</ImplicitUsings>
       <IsPackable>false</IsPackable>
     </PropertyGroup>

     <ItemGroup>
       <PackageReference Include="Microsoft.NET.Test.Sdk" Version="17.*" />
       <PackageReference Include="xunit" Version="2.*" />
       <PackageReference Include="xunit.runner.visualstudio" Version="2.*" />
     </ItemGroup>

     <ItemGroup>
       <ProjectReference Include="..\..\src\NitroPC\NitroPC.csproj" />
     </ItemGroup>
   </Project>
   ```

### Step 1.2 — Models (`Models/NitroSettings.cs` & `Models/MetricsSnapshot.cs`)
- `NitroSettings`:
  ```csharp
  namespace NitroPC.Models;

  public enum RamDisplayMode
  {
      Percent,
      Gigabytes,
      Both
  }

  public enum KeepAwakeBarMode
  {
      Badge,
      CompactIcon,
      Hidden
  }

  public sealed class NitroSettings
  {
      /// <summary>0 means Indefinite (∞). Default 60 minutes.</summary>
      public int KeepAwakeMinutes { get; set; } = 60;
      public bool AutoStartKeepAwakeOnLaunch { get; set; } = false;
      public RamDisplayMode RamDisplayMode { get; set; } = RamDisplayMode.Percent;
      public KeepAwakeBarMode KeepAwakeBarMode { get; set; } = KeepAwakeBarMode.Badge;
  }
  ```
- `MetricsSnapshot`:
  ```csharp
  namespace NitroPC.Models;

  public sealed record MetricsSnapshot(
      double CpuTotalPercent,
      double[] CpuCorePercents,
      int LogicalCoreCount,
      double RamPercent,
      double RamUsedGb,
      double RamTotalGb,
      double[] RamHistoryPercents);
  ```

### Step 1.3 — `SettingsService` & `StartupService`
- **`Services/SettingsService.cs`**:
  - Saves/loads `NitroSettings` using `System.Text.Json` at `%LOCALAPPDATA%\NitroPC\settings.json` (accept an optional custom file path in constructor so unit tests can use a temp directory!).
  - Never crash on corrupt JSON; return `new NitroSettings()` on read failure.
- **`Services/StartupService.cs`**:
  - Registry key: `HKEY_CURRENT_USER\Software\Microsoft\Windows\CurrentVersion\Run`, value name: `"NitroPC"`.
  - `bool IsEnabled()`: returns `true` if `"NitroPC"` value exists and is non-empty.
  - `void SetEnabled(bool enabled)`: when `true`, obtains `Environment.ProcessPath` and writes $"\"{exePath}\"" as `RegistryValueKind.String`; when `false`, calls `key.DeleteValue("NitroPC", throwOnMissingValue: false)`.

---

## Phase 2: Win32 Native Interop (`Interop/NativeMethods.cs`)

Copy this **verified** P/Invoke definition into `src/NitroPC/Interop/NativeMethods.cs`. Every struct layout and byte size has been verified on Windows 11 x64 (`.NET 10`):

```csharp
using System.Runtime.InteropServices;

namespace NitroPC.Interop;

internal static class NativeMethods
{
    // --- CPU Per-Core Performance (ntdll.dll) ---
    public const int SystemProcessorPerformanceInformation = 8;

    // Verified 48 bytes on x64. NOTE: KernelTime INCLUDES IdleTime!
    [StructLayout(LayoutKind.Sequential)]
    public struct SYSTEM_PROCESSOR_PERFORMANCE_INFORMATION
    {
        public long IdleTime;
        public long KernelTime;
        public long UserTime;
        public long DpcTime;
        public long InterruptTime;
        public int InterruptCount;
        public int Reserved;
    }

    [DllImport("ntdll.dll")]
    public static extern int NtQuerySystemInformation(
        int systemInformationClass,
        [Out] SYSTEM_PROCESSOR_PERFORMANCE_INFORMATION[] systemInformation,
        int systemInformationLength,
        out int returnLength);

    // --- Memory Status (kernel32.dll) ---
    [StructLayout(LayoutKind.Sequential)]
    public struct MEMORYSTATUSEX
    {
        public uint dwLength;
        public uint dwMemoryLoad;
        public ulong ullTotalPhys;
        public ulong ullAvailPhys;
        public ulong ullTotalPageFile;
        public ulong ullAvailPageFile;
        public ulong ullTotalVirtual;
        public ulong ullAvailVirtual;
        public ulong ullAvailExtendedVirtual;
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool GlobalMemoryStatusEx(ref MEMORYSTATUSEX lpBuffer);

    // --- Power / Sleep Execution State (kernel32.dll) ---
    [Flags]
    public enum EXECUTION_STATE : uint
    {
        ES_SYSTEM_REQUIRED = 0x00000001,
        ES_DISPLAY_REQUIRED = 0x00000002,
        ES_CONTINUOUS = 0x80000000
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    public static extern EXECUTION_STATE SetThreadExecutionState(EXECUTION_STATE esFlags);

    // --- SendInput for Idle Timer Reset (user32.dll) ---
    public const uint INPUT_KEYBOARD = 1;
    public const uint KEYEVENTF_KEYUP = 0x0002;
    public const ushort VK_F15 = 0x7E;

    // CRITICAL: InputUnion MUST include MOUSEINPUT so sizeof(INPUT) == 40 on x64!
    [StructLayout(LayoutKind.Sequential)]
    public struct INPUT
    {
        public uint type;
        public InputUnion U;
    }

    [StructLayout(LayoutKind.Explicit)]
    public struct InputUnion
    {
        [FieldOffset(0)] public MOUSEINPUT mi;
        [FieldOffset(0)] public KEYBDINPUT ki;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct MOUSEINPUT
    {
        public int dx;
        public int dy;
        public uint mouseData;
        public uint dwFlags;
        public uint time;
        public IntPtr dwExtraInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct KEYBDINPUT
    {
        public ushort wVk;
        public ushort wScan;
        public uint dwFlags;
        public uint time;
        public IntPtr dwExtraInfo;
    }

    [DllImport("user32.dll", SetLastError = true)]
    public static extern uint SendInput(uint nInputs, INPUT[] pInputs, int cbSize);

    // --- Window & Taskbar Positioning (user32.dll) ---
    public const int GWL_EXSTYLE = -20;
    public const int WS_EX_TOOLWINDOW = 0x00000080;
    public const int WS_EX_NOACTIVATE = 0x08000000;

    public static readonly IntPtr HWND_TOPMOST = new(-1);
    public const uint SWP_NOACTIVATE = 0x0010;
    public const uint SWP_SHOWWINDOW = 0x0040;
    public const uint SWP_NOMOVE = 0x0002;
    public const uint SWP_NOSIZE = 0x0001;

    [StructLayout(LayoutKind.Sequential)]
    public struct RECT
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
        public int Width => Right - Left;
        public int Height => Bottom - Top;
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    public static extern IntPtr FindWindow(string? lpClassName, string? lpWindowName);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    public static extern IntPtr FindWindowEx(IntPtr hWndParent, IntPtr hWndChildAfter, string? lpszClass, string? lpszWindow);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool GetWindowRect(IntPtr hWnd, out RECT lpRect);

    [DllImport("user32.dll")]
    public static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW", SetLastError = true)]
    public static extern IntPtr GetWindowLongPtr(IntPtr hWnd, int nIndex);

    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW", SetLastError = true)]
    public static extern IntPtr SetWindowLongPtr(IntPtr hWnd, int nIndex, IntPtr dwNewLong);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int X, int Y, int cx, int cy, uint uFlags);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool DestroyIcon(IntPtr hIcon);
}
```

---

## Phase 3: Core Helpers, Services & Unit Tests

### Step 3.1 — `Helpers/CpuCalculator.cs`
Write a pure, testable helper class:
1. `ComputeUsage(SYSTEM_PROCESSOR_PERFORMANCE_INFORMATION[] prev, SYSTEM_PROCESSOR_PERFORMANCE_INFORMATION[] curr)` returns `(double TotalPercent, double[] CorePercents)`:
   - For each core `i`:
     - `dIdle = Math.Max(0, curr[i].IdleTime - prev[i].IdleTime)`
     - `dKernel = Math.Max(0, curr[i].KernelTime - prev[i].KernelTime)`
     - `dUser = Math.Max(0, curr[i].UserTime - prev[i].UserTime)`
     - `total = dKernel + dUser`
     - `busy = Math.Max(0, (dKernel - dIdle) + dUser)`
     - `corePct = total > 0 ? Math.Clamp((double)busy / total * 100.0, 0.0, 100.0) : 0.0`
   - `TotalPercent` is computed from the sum of `busy` across all cores divided by sum of `total` across all cores (clamped `0..100`).
2. `DownsampleBars(IReadOnlyList<double> corePercents, int targetBars)`:
   - If `corePercents.Count == 0`, return `new double[targetBars]`.
   - If `corePercents.Count <= targetBars`, return `corePercents.ToArray()`.
   - Otherwise, partition `corePercents` evenly across `targetBars` buckets and take the average (or max) of each bucket so a 24-core CPU renders cleanly as `14` bars on the taskbar strip!

### Step 3.2 — `Helpers/RingBuffer.cs`
- Fixed-size `double` ring buffer (e.g., capacity `24`).
- `Push(double value)` appends `value` (and on the very first `Push` when empty, fills all initial slots with `value` so the RAM sparkline starts as a clean flat line at the current RAM % rather than jumping from `0%`!).
- `double[] ToArray()` returns values oldest-to-newest.

### Step 3.3 — `Services/SystemMetricsService.cs`
- Uses a `DispatcherTimer` (`Interval = TimeSpan.FromSeconds(1)`), taking an initial baseline reading immediately on `Start()`.
- Calls `NtQuerySystemInformation(8, ...)` for `Environment.ProcessorCount` cores and `GlobalMemoryStatusEx` (`dwLength = (uint)Marshal.SizeOf<MEMORYSTATUSEX>()`).
- Computes `RamUsedGb = (totalBytes - availBytes) / (1024.0 * 1024.0 * 1024.0)` and `RamTotalGb = totalBytes / (1024.0 * 1024.0 * 1024.0)`.
- Raises `event Action<MetricsSnapshot>? MetricsUpdated`.

### Step 3.4 — `Services/KeepAwakeService.cs`
- Abstract out a small `IKeepAwakePlatform` interface (with production implementation calling `NativeMethods.SetThreadExecutionState` and `NativeMethods.SendInput` with `VK_F15` keydown + keyup) so `KeepAwakeServiceTests` can test timers/expiry without sending real OS keystrokes during `dotnet test`!
- Methods & Properties:
  - `bool IsAwake { get; }`
  - `bool IsIndefinite { get; }`
  - `TimeSpan? Remaining { get; }`
  - `TimeSpan? TotalDuration { get; }`
  - `void Start(TimeSpan? duration)` (`null` or `TimeSpan.Zero` = Indefinite `∞`):
    - Calls `platform.EnableExecutionState()` (`ES_CONTINUOUS | ES_SYSTEM_REQUIRED | ES_DISPLAY_REQUIRED`).
    - Calls `platform.SendActivityPulse()` immediately, and starts a 60-second pulse timer + 1-second UI tick timer.
    - Sets `_endTimeUtc = duration is { TotalSeconds: > 0 } d ? utcNow + d : null`.
  - `void Stop()`:
    - Stops timers, clears `_endTimeUtc`, calls `platform.ClearExecutionState()` (`ES_CONTINUOUS`).
  - `void Toggle(int defaultMinutes)`: if `IsAwake`, calls `Stop()`; otherwise calls `Start(defaultMinutes <= 0 ? null : TimeSpan.FromMinutes(defaultMinutes))`.
  - Raises `event Action? StateChanged` on start, stop, and every 1-second tick (auto-stopping when `utcNow >= _endTimeUtc`).
  - Implements `IDisposable` (calls `Stop()`).

### Step 3.5 — Write xUnit Tests (`tests/NitroPC.Tests/`)
Write focused unit tests covering:
1. `CpuCalculatorTests`:
   - Idle CPU (`dIdle == dKernel`, `dUser == 0`) **must** return `0%`, NOT `100%`.
   - 50% busy CPU (`dKernel = 100, dIdle = 50, dUser = 0` or `dKernel = 50, dIdle = 50, dUser = 50`) returns `50%`.
   - `DownsampleBars` maps 24 cores down to 14 bars accurately.
2. `RingBufferTests`:
   - First `Push(64.0)` initializes baseline to `64.0` across buffer; subsequent pushes shift oldest values out in FIFO order.
3. `KeepAwakeServiceTests`:
   - Starting timed session enables execution state, sends pulse, computes `Remaining`, and auto-stops when expired; stopping clears execution state.
   - Starting indefinite session (`null`) never expires until `Stop()`.
4. `SettingsServiceTests`:
   - Round-trips `NitroSettings` to a temp JSON file and gracefully returns defaults if JSON is corrupt.

Run `dotnet test NitroPC.sln` and verify all tests pass!

---

## Phase 4: Theme & Custom Vector Controls

### Step 4.1 — `Themes/NitroTheme.xaml`
Create `ResourceDictionary` containing all `#AARRGGBB` `SolidColorBrush` tokens from [`docs/UI_SPEC.md`](UI_SPEC.md) (`StripBackgroundBrush`, `FlyoutBackgroundBrush`, `CardBackgroundBrush`, `TextPrimaryBrush`, `TextMutedBrush`, `CpuCoralBrush`, `RamBlueBrush`, `RamFillBrush`, `SparkGuideBrush`, `KaffeAmberBrush`, `KaffeGlowFillBrush`, `KaffeGlowBorderBrush`, etc.) and clean borderless button/chip styles. Merge it in `App.xaml`.

### Step 4.2 — `Controls/CpuBarsControl.cs`
Custom lightweight `FrameworkElement` (`AffectsRender` dependency properties: `Values` (`IReadOnlyList<double>`), `MaxBars` (`int`, default `14`), `BarBrush` (`Brush`)):
- In `OnRender(DrawingContext dc)`:
  - Call `CpuCalculator.DownsampleBars(Values, MaxBars)` to get `n` values (`n = MaxBars` when empty, all `0`).
  - Compute `gap = MaxBars <= 14 ? 1.5 : 2.2`, `barWidth = Math.Max(1.8, (ActualWidth - gap * (n - 1)) / n)`.
  - For each bar `i`, normalize `pct = Math.Clamp(vals[i] / 100.0, 0.18, 1.0)` (minimum `18%` height so idle bars always look like crisp coral ticks!), `h = ActualHeight * pct`, `y = ActualHeight - h`, and call `dc.DrawRoundedRectangle(brush, null, new Rect(x, y, barWidth, h), 1.1, 1.1)`.

### Step 4.3 — `Controls/RamSparklineControl.cs`
Custom lightweight `FrameworkElement` (`Values` (`IReadOnlyList<double>`), `StrokeBrush`, `FillBrush`, `GuideBrush`):
- In `OnRender(DrawingContext dc)`:
  - Draw horizontal dashed guide line at `y = 3` from `x = 0` to `ActualWidth` (`Pen` with `DashStyle(new double[] { 3, 3 }, 0)`).
  - Map `Values` (`0..100`) to `Point(x, y)` where `y = ActualHeight - 2 - (val / 100.0) * (ActualHeight - 5)`.
  - Build a `StreamGeometry` for the area polygon (`points` -> `(ActualWidth, ActualHeight)` -> `(0, ActualHeight)` -> closed) and draw with `FillBrush`.
  - Build a `StreamGeometry` for the top curve/polyline and draw with `Pen(StrokeBrush, 2.0) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round, LineJoin = PenLineJoin.Round }`.

---

## Phase 5: Taskbar Strip Window & Companion System Tray Icon

### Step 5.1 — `Views/TaskbarStripWindow.xaml` & `.cs`
1. Window attributes:
   - `WindowStyle="None"`, `AllowsTransparency="True"`, `Background="Transparent"`, `ResizeMode="NoResize"`, `ShowInTaskbar="False"`, `Topmost="True"`, `SizeToContent="Width"`, `Height="48"`.
2. Layout inside XAML:
   - Outer `Border` (`Height="36"`, `VerticalAlignment="Center"`, `CornerRadius="7"`, `Padding="10,0"`, `Background="{StaticResource StripBackgroundBrush}"`, `BorderBrush="{StaticResource StripBorderBrush}"`, `BorderThickness="1"`, `Cursor="Hand"`).
   - Inside: horizontal `StackPanel` matching **Section 3.A** of [`docs/UI_SPEC.md`](UI_SPEC.md) (`CpuBarsControl` + `CPU` / `%`, 1px divider, `RamSparklineControl` + `RAM` / value, 1px divider, and Keep-Alive badge/icon with the coffee cup `Path`).
3. Window Styles (`OnSourceInitialized`):
   ```csharp
   var hwnd = new WindowInteropHelper(this).Handle;
   long exStyle = NativeMethods.GetWindowLongPtr(hwnd, NativeMethods.GWL_EXSTYLE).ToInt64();
   exStyle |= NativeMethods.WS_EX_TOOLWINDOW | NativeMethods.WS_EX_NOACTIVATE;
   NativeMethods.SetWindowLongPtr(hwnd, NativeMethods.GWL_EXSTYLE, new IntPtr(exStyle));
   ```
4. **Taskbar Docking & Fullscreen Auto-Hide Timer** (`DispatcherTimer` every `500ms` + on `SizeChanged`):
   - Find `shellTray = NativeMethods.FindWindow("Shell_TrayWnd", null)`. If `IntPtr.Zero`, fall back to bottom-right of `WinForms.Screen.PrimaryScreen.WorkingArea`.
   - Find `trayNotify = NativeMethods.FindWindowEx(shellTray, IntPtr.Zero, "TrayNotifyWnd", null)`.
   - Get `shellRect` (`GetWindowRect(shellTray, out var shellRect)`) and `notifyRect` (`GetWindowRect(trayNotify, out var notifyRect)`).
   - **Fullscreen check**: Get `fg = NativeMethods.GetForegroundWindow()`. If `fg != IntPtr.Zero && fg != shellTray && fg != hwnd` and `GetWindowRect(fg, out var fgRect)` covers the entire primary screen (`fgRect.Left <= 0 && fgRect.Top <= 0 && fgRect.Width >= screenBounds.Width && fgRect.Height >= screenBounds.Height`), hide the strip (`Visibility = Visibility.Hidden`) and `return`. Otherwise ensure `Visibility = Visibility.Visible`.
   - **DPI-accurate placement**:
     ```csharp
     var dpi = VisualTreeHelper.GetDpi(this);
     int stripWidthPhys = (int)Math.Ceiling(ActualWidth * dpi.DpiScaleX);
     int stripHeightPhys = shellRect.Height;
     int marginPhys = (int)Math.Round(6 * dpi.DpiScaleX);

     int targetX = (notifyRect.Width > 0 ? notifyRect.Left : shellRect.Right - (int)(160 * dpi.DpiScaleX)) - stripWidthPhys - marginPhys;
     int targetY = shellRect.Top;

     // Optional safety clamp against ReBarWindow32 so we never overlap pinned app icons
     IntPtr rebar = NativeMethods.FindWindowEx(shellTray, IntPtr.Zero, "ReBarWindow32", null);
     if (rebar != IntPtr.Zero && NativeMethods.GetWindowRect(rebar, out var rebarRect) && targetX < rebarRect.Right + marginPhys)
     {
         targetX = rebarRect.Right + marginPhys;
     }

     this.Height = shellRect.Height / dpi.DpiScaleY;
     NativeMethods.SetWindowPos(
         hwnd,
         NativeMethods.HWND_TOPMOST,
         targetX,
         targetY,
         stripWidthPhys,
         stripHeightPhys,
         NativeMethods.SWP_NOACTIVATE | NativeMethods.SWP_SHOWWINDOW);
     ```
5. Mouse interactions:
   - `MouseLeftButtonUp`: fires `ToggleFlyoutRequested` event.
   - `MouseDown` (`e.ChangedButton == MouseButton.Middle`): toggles Keep-Awake directly!
   - `MouseRightButtonUp`: opens context menu.

### Step 5.2 — `Services/TrayIconService.cs`
- Uses `using WinForms = System.Windows.Forms;` and `using Drawing = System.Drawing;`.
- Dynamically generates a crisp `32×32` `Drawing.Bitmap` icon (rounded dark-teal square `#233840` with coral/blue/amber `"N"` or amber coffee dot when Awake -> `bmp.GetHicon()` -> `Drawing.Icon.FromHandle`, remembering to call `NativeMethods.DestroyIcon` when swapping icons!). Zero external `.ico` file needed!
- Attaches a dark/clean `WinForms.ContextMenuStrip` (or WPF `ContextMenu`) with:
  - `Open NitroPC Toolbox`
  - `Keep Awake — 30m` / `1h` / `2h` / `4h` / `Indefinite (∞)`
  - `Stop Keep Awake`
  - `Start with Windows` (checked state synced with `StartupService.IsEnabled()`)
  - `Exit NitroPC`

---

## Phase 6: Toolbox Flyout Window (`Views/ToolboxFlyoutWindow.xaml` & `.cs`)

1. Window attributes:
   - `WindowStyle="None"`, `AllowsTransparency="True"`, `Background="Transparent"`, `ResizeMode="NoResize"`, `ShowInTaskbar="False"`, `Topmost="True"`, `Width="368"`, `SizeToContent="Height"`.
2. Build the exact card layout specified in **Section 3.B** of [`docs/UI_SPEC.md`](UI_SPEC.md) (matching [`docs/images/ui-flyout-closeup.png`](images/ui-flyout-closeup.png)):
   - Header (`N` gradient badge + `NitroPC Toolbox` + status chip `● Awake · hh:mm:ss` or `● Idle`).
   - 2-column Telemetry Grid (`CPU LOAD` card + `MEMORY` card).
   - `☕ Keep Awake` card (`Start`/`Stop` button, preset chips `30m`, `1h`, `2h`, `4h`, `∞`, custom minutes `TextBox`, countdown progress bar).
   - Display preferences row (`RAM: % | GB | Both` and `Bar: Badge | Icon | Hide`) — clicking updates `NitroSettings`, saves via `SettingsService`, and updates `TaskbarStripWindow` live!
   - Footer (`Start with Windows` toggle switch wired to `StartupService` + `Quit` button).
3. Positioning & Deactivation Debounce:
   - Expose `DateTime LastDeactivatedUtc { get; private set; }`.
   - On `Deactivated`: set `LastDeactivatedUtc = DateTime.UtcNow; Hide();`.
   - On `PreviewKeyDown`: if `e.Key == Key.Escape`, `Hide()`.
   - In `ToggleNearStrip(Rect stripPhysicalRect)`:
     - If `IsVisible`, call `Hide()` and return.
     - If `(DateTime.UtcNow - LastDeactivatedUtc).TotalMilliseconds < 180`, return (prevents immediate reopen when clicking the taskbar strip to close an open flyout!).
     - Call `Show()`, `UpdateLayout()`, convert `ActualWidth` / `ActualHeight` using `VisualTreeHelper.GetDpi(this)`, and position `10` DIPs above `shellRect.Top`, right-aligned with `stripPhysicalRect.Right` (clamped inside `WinForms.Screen.PrimaryScreen.WorkingArea`), then call `Activate()`.

---

## Phase 7: Application Wiring, Single-Instance Guard & Verification

### Step 7.1 — `App.xaml` & `App.xaml.cs`
- `App.xaml`:
  - `ShutdownMode="OnExplicitShutdown"` (NO `StartupUri`!).
  - Merge `Themes/NitroTheme.xaml`.
- `App.xaml.cs`:
  - Acquire single-instance `Mutex` (`"Local\\NitroPC_SingleInstance_Mutex"`). If already running, exit cleanly (`Shutdown()`).
  - Instantiate `SettingsService`, `StartupService`, `SystemMetricsService`, `KeepAwakeService`, `TaskbarStripWindow`, `ToolboxFlyoutWindow`, and `TrayIconService`.
  - If `settings.AutoStartKeepAwakeOnLaunch` is `true`, start `KeepAwakeService`.
  - Show `TaskbarStripWindow` and start `SystemMetricsService`.
  - On `Exit`: dispose `TrayIconService`, `KeepAwakeService` (ensuring `SetThreadExecutionState(ES_CONTINUOUS)` is restored!), `SystemMetricsService`, and release the `Mutex`.

### Step 7.2 — Final Build, Test & Smoke Verification Checklist
1. Build in both Debug and Release:
   ```powershell
   dotnet build NitroPC.sln -c Release
   ```
   Confirm **0 errors** and **0 warnings**.
2. Run all unit tests:
   ```powershell
   dotnet test NitroPC.sln -c Release --nologo
   ```
   Confirm **100% pass**.
3. Smoke launch verification:
   - Launch `src\NitroPC\bin\Release\net10.0-windows\NitroPC.exe` in the background, wait 3 seconds, verify the process is alive (`Get-Process NitroPC`) and has NOT crashed on startup, then stop the process cleanly (`Stop-Process -Id <pid>`).
