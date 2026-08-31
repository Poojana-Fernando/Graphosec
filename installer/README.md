# Graphosec Installer

## Publish release binaries

From the repository root:

```powershell
.\installer\publish-release.ps1
```

This publishes a **self-contained** Release build (includes the .NET runtime) to:

```text
artifacts/publish/Graphosec/
```

The main executable is `Graphosec.exe`. End users do **not** need to install .NET separately.

## Create a Windows installer (Inno Setup)

1. Install [Inno Setup 6](https://jrsoftware.org/isinfo.php).
2. Publish the application using the script above.
3. Compile `installer/ResumableCopy.iss`.

Output:

```text
artifacts/installer/Graphosec-Setup-1.0.0.exe
```

The installer supports:

- Fresh installation
- Upgrade over an existing installation
- Uninstall
- Start menu shortcut
- Preserving `%LOCALAPPDATA%\Graphosec` data on uninstall (logs and user data)

## Manual installation

Copy the publish output folder to a permanent location and create a shortcut to `Graphosec.exe`.

No separate .NET runtime install is required for self-contained publish output.

## End-user requirements

| Requirement | Details |
|-------------|---------|
| **OS** | Windows 10 or later (64-bit) |
| **Architecture** | x64 |
| **.NET runtime** | Bundled with the installer |
| **Permissions** | Standard user (per-user install) |
