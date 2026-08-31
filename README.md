# Graphosec

Graphosec is a Windows desktop application for reliable, integrity-verified, resumable file copying between storage devices. It is built for large transfers, removable media (USB drives), unstable connections, and recovery after interruption, pause, or crash.

## Highlights

- **Resumable transfers** — pause, resume, and recover interrupted copies from durable session state
- **Integrity verified** — per-chunk SHA-256 verification and optional whole-file validation
- **Crash-safe** — SQLite-backed sessions survive application or system interruption
- **Device-aware** — monitors USB/fixed drives, free space, and destination readiness
- **Parallel copying** — bounded worker pool with adaptive chunk sizing and I/O tuning
- **Modern UI** — light/dark themes, transfer filtering tabs, settings panel, and cancel confirmation

## Features

### Copy engine

| Capability | Description |
|------------|-------------|
| Chunked copy | Large files split into configurable chunks with independent progress |
| Session persistence | Transfer state stored in SQLite on the destination volume |
| Atomic finalization | Staged `.part` files promoted safely to the final destination |
| Recovery | Detect and resume unfinished sessions after reconnecting storage |
| Path hardening | Validates paths and rejects unsafe reparse-point scenarios |
| Adaptive performance | Adjusts chunk size, workers, queue depth, and buffers by file size |

### Desktop application

| Capability | Description |
|------------|-------------|
| Source / destination picker | Browse files and select drives from a device panel |
| Transfer management | Pause, resume, cancel (with confirmation), recover, remove, clear history |
| Transfer tabs | Filter by **All**, **Completed**, **Paused**, and **Cancelled** |
| Progress details | Speed, ETA, bytes copied, and user-friendly status messages |
| Light / dark mode | Theme preference saved under `%LOCALAPPDATA%\Graphosec\ui-settings.json` |
| Overwrite option | Optional replacement of existing destination files |

## Requirements

- **OS:** Windows 10 or later (x64)
- **Installer:** Self-contained — no separate .NET runtime install required
- **Development:** [.NET 10 SDK](https://dotnet.microsoft.com/download)

## Quick start

### Run from source

```powershell
git clone https://github.com/Poojana-Fernando/Graphosec.git
cd Graphosec
dotnet restore
dotnet run --project src/ResumableCopy.App/ResumableCopy.App.csproj
```

### Build release executable

```powershell
dotnet build src/ResumableCopy.App/ResumableCopy.App.csproj -c Release
```

Output:

```text
src/ResumableCopy.App/bin/Release/net10.0-windows/Graphosec.exe
```

### Publish for distribution

```powershell
.\installer\publish-release.ps1
```

Published files:

```text
artifacts/publish/Graphosec/Graphosec.exe
```

If NuGet vulnerability audit fails offline, publish with:

```powershell
dotnet publish src/ResumableCopy.App/ResumableCopy.App.csproj `
  -c Release -r win-x64 --self-contained true `
  -o artifacts/publish/Graphosec `
  /p:PublishSingleFile=false `
  /p:NuGetAudit=false
```

See [installer/README.md](installer/README.md) for Inno Setup installer instructions.

## Using Graphosec

1. **Start a transfer** — choose source file and destination path (or click a ready drive), then click **Start**.
2. **Monitor progress** — view active transfers in the **Transfers** tab with live speed and ETA.
3. **Pause / resume** — pause an active copy; resume later even after unplugging and reconnecting the destination device (when recovery data is available).
4. **Cancel** — click **Cancel**; confirm in the dialog to stop and clean up partial session data.
5. **Recover** — use **Recover** to scan the destination for resumable sessions.
6. **Filter history** — use **Completed**, **Paused**, and **Cancelled** tabs to focus on specific transfer states.
7. **Settings** — open **Settings** (top right) to switch between light and dark mode.

## How resumable copy works

For a destination like `D:\backups\file.bin`, Graphosec stores session metadata under:

```text
D:\backups\.copycache\
  sessions.db
  {session-id}.part
```

Each chunk is written, flushed, hash-verified, and committed before being marked complete. On success, the staged file is finalized atomically and session metadata is removed.

Transfer states include: `Pending`, `Running`, `Paused`, `WaitingForSource`, `WaitingForDestination`, `WaitingForStorage`, `Verifying`, `Completed`, `Failed`, `Cancelled`, and `RecoveryRequired`.

## Configuration

Application settings: `src/ResumableCopy.App/appsettings.json`

| Setting | Purpose |
|---------|---------|
| `ResumableCopy:Logging:MinimumLevel` | Log verbosity |
| `ResumableCopy:Logging:LogDirectory` | File log directory |
| `ResumableCopy:Copy:*` | Default copy options (chunk size, workers, verification) |
| `ResumableCopy:Staging:CacheDirectoryName` | Staging folder name (default `.copycache`) |

Environment variables in paths (for example `%LOCALAPPDATA%`) are expanded at runtime.

## Data locations

| Data | Location |
|------|----------|
| UI settings (theme) | `%LOCALAPPDATA%\Graphosec\ui-settings.json` |
| Application logs | `%LOCALAPPDATA%\ResumableCopy\logs` (legacy path) |
| Transfer history | `%LOCALAPPDATA%\ResumableCopy\history.json` (legacy path) |
| Session database | `{destination}\.copycache\sessions.db` |

## Solution structure

```text
src/
  ResumableCopy.App/           WPF shell (Graphosec.exe)
  ResumableCopy.Application/   MVVM, orchestration, services
  ResumableCopy.Core/          Copy engine, SQLite persistence, security
tests/
  ResumableCopy.Core.Tests/
  ResumableCopy.Application.Tests/
installer/                     Publish script and Inno Setup definition
```

Architecture layers:

```text
Graphosec (WPF UI)
        ↓
ResumableCopy.Application (orchestration)
        ↓
ResumableCopy.Core (copy engine)
```

## Development

### Build and test

```powershell
dotnet restore
dotnet build -warnaserror
dotnet test
```

Release validation:

```powershell
dotnet build -c Release -warnaserror
dotnet test -c Release
```

### Regenerate application icon

```powershell
.\src\ResumableCopy.App\Assets\generate-app-icon.ps1
```

Default source: `Assets/logo-exe.png` (embedded into `Assets/app.ico` for `Graphosec.exe`).

## Documentation

- [ARCHITECTURE.md](ARCHITECTURE.md) — system design and core components
- [RECOVERY.md](RECOVERY.md) — recovery behavior and session reconciliation
- [TROUBLESHOOTING.md](TROUBLESHOOTING.md) — common issues and fixes
- [DEVELOPMENT.md](DEVELOPMENT.md) — contributor workflow and tuning notes
- [installer/README.md](installer/README.md) — publishing and Windows installer

## Version

Current version: **1.0.0**

## License

This project is licensed under the [MIT License](LICENSE).
