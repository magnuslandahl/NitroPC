# NitroPC

A lightweight, native Windows 11 taskbar toolbox showing real-time **CPU** and **RAM** gauges alongside a built-in **Keep-Alive ("Kaffe")** controller and **Start with Windows** toggle.

![NitroPC Full UI Concept](docs/images/ui-full-concept.png)

## Features

- **Windows 11 Taskbar Strip**: Docked directly inside the taskbar next to the system tray (auto-hides during fullscreen apps/games), inspired by clean menu-bar monitors:
  - **CPU Gauge**: 14 per-core coral bars (`#F3A683`) + live total CPU `%`.
  - **Memory Gauge**: Sky-blue area sparkline (`#6EC6FF`) + configurable `%`, `GB`, or `% · GB` readout.
  - **Keep-Alive Badge**: Live amber `☕ AWAKE` countdown pill (configurable as `Icon + Timer`, `Compact Icon`, or `Hidden`).
- **Windows 11 Toolbox Flyout**: Click the taskbar strip to open a dark translucent flyout with detailed CPU/RAM cards, one-click Keep-Awake presets (`30m`, `1h`, `2h`, `4h`, `∞`, or custom minutes), display preferences, and **Start with Windows**.
- **Smart Keep-Alive Engine**: Upgrades legacy `Kaffe` by combining `SetThreadExecutionState` (prevents OS sleep & display lock) with a silent 60-second `VK_F15` activity pulse (keeps Teams/Slack active without flashing keyboard NumLock LEDs).
- **Zero-Bloat Native Stack**: Built with `.NET 10` WPF + direct Win32 P/Invoke (`NtQuerySystemInformation`, `GlobalMemoryStatusEx`, `SendInput`) — zero external runtime dependencies and `~18 MB` RAM footprint.

## Visual Previews

| Taskbar Strip (Awake) | Taskbar Strip (Idle) |
| --- | --- |
| ![Awake Strip](docs/images/ui-strip-closeup.png) | ![Idle Strip](docs/images/ui-strip-idle-closeup.png) |

| Toolbox Flyout + Taskbar |
| --- |
| ![Toolbox Flyout](docs/images/ui-flyout-closeup.png) |

## Documentation & Implementation Blueprint

- **[`AGENTS.md`](AGENTS.md)** — Mandatory rules, build/test commands, and Win32/WPF pitfalls to avoid for coding agents.
- **[`docs/IMPLEMENTATION_PLAN.md`](docs/IMPLEMENTATION_PLAN.md)** — Ordered Phase 1–7 step-by-step implementation guide, target folder structure, verified Win32 P/Invoke structs, and test plan.
- **[`docs/UI_SPEC.md`](docs/UI_SPEC.md)** — Complete UI specification, exact WPF `#AARRGGBB` color tokens, typography, vector paths, and interaction rules.
- **[`docs/ui-preview.html`](docs/ui-preview.html)** — Interactive HTML/CSS/SVG UI prototype (open in any browser to test layouts and states).

