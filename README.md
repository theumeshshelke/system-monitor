# System Monitor Overlay

A tiny always-on-top widget for Windows that shows **live CPU, RAM, disk, and network usage**, updating every second.

## What it looks like

A small dark, rounded, semi-transparent card pinned to the top-right corner of your screen showing:
- CPU usage % with a bar
- RAM usage % (+ GB used/total) with a bar
- Disk activity (combined read+write MB/s) with a bar
- Live download / upload speed
- A mini scrolling graph of CPU (green) vs RAM (blue) over the last ~40 seconds

You can **drag it anywhere** by clicking and holding the card. It also has a **system tray icon**:
- Click the **✕** on the card to hide it to the tray (doesn't quit the app)
- **Double-click** the tray icon, or use its right-click menu's **Show/Hide**, to bring it back
- Use the tray icon's right-click **Exit** to actually close the app

## Requirements

- Windows 10/11
- [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0) installed

## How to build and run

1. Install the .NET 8 SDK if you don't have it (link above — just run the installer).
2. Open a terminal (PowerShell or Command Prompt) in this folder (`SystemMonitor/`).
3. Run:
   ```
   dotnet run
   ```
   This restores, builds, and launches the app in one step.

## Building the .exe without installing anything (GitHub Actions)

This project includes a GitHub Actions workflow (`.github/workflows/build.yml`) that builds the `.exe` for you in the cloud — no .NET SDK install needed on your machine. See the walkthrough your assistant gave you, or in short:

1. Create a new **public** repo on GitHub and upload this entire `SystemMonitor` folder to it (drag-and-drop works on github.com — no `git` command needed).
2. Go to the repo's **Actions** tab. The "Build Windows exe" workflow runs automatically on push (or click **Run workflow** to trigger it manually).
3. Once it finishes (green checkmark, ~1-2 minutes), open that run and download the **SystemMonitor-exe** artifact — it's a zip containing `SystemMonitor.exe`.

## Building it locally instead (requires the .NET 8 SDK)

```
dotnet publish -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true
```

The `.exe` will land in `bin\Release\net8.0-windows\win-x64\publish\SystemMonitor.exe`. You can copy that single file anywhere and double-click it — no .NET install needed on the target machine.

## Optional: run it automatically at startup

1. Press `Win + R`, type `shell:startup`, hit Enter.
2. Create a shortcut to `SystemMonitor.exe` (from the publish step above) inside that folder.
   It'll now launch automatically whenever you log in.

## How it works (in case you want to extend it)

- **CPU**: calls the Windows `GetSystemTimes` API once per second and compares idle/kernel/user time deltas — the same technique Task Manager uses under the hood.
- **RAM**: calls `GlobalMemoryStatusEx`, which returns total/available physical memory and Windows' own computed memory-load percentage.
- **Network**: sums `BytesReceived`/`BytesSent` across all "up" network interfaces (skipping loopback/tunnel adapters) once per second and computes the delta as a rate.
- **Disk**: uses the `PhysicalDisk\Disk Read Bytes/sec` and `Disk Write Bytes/sec` performance counters (this is the one piece that needs the `System.Diagnostics.PerformanceCounter` NuGet package, restored automatically on first build). The bar is scaled against a 200 MB/s visual reference, not a hard cap — actual speed always shown as text.
- **Graph**: keeps the last 40 CPU/RAM samples in memory and redraws two `Polyline` shapes each tick.
- **Tray icon**: a `System.Windows.Forms.NotifyIcon` (hence `UseWindowsForms` is enabled alongside WPF) with a right-click menu for Show/Hide and Exit.
- **UI**: WPF `DispatcherTimer` ticks every second and updates the bound `TextBlock`/`ProgressBar`/`Polyline` elements directly.

## Ideas for extending it further

- Add per-core CPU breakdown
- Add per-adapter network detail (e.g. Wi-Fi vs Ethernet separately)
- Save/restore window position and visibility between launches (e.g. to a small JSON settings file)
- Swap the tray icon's placeholder `SystemIcons.Application` for a custom `.ico`
- Add a settings panel (refresh interval, which meters to show, opacity)
