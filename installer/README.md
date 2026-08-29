# Graphosec Installer

## Publish release binaries

From the repository root:

```powershell
.\installer\publish-release.ps1
```

This publishes a framework-dependent Release build to:

```text
artifacts/publish/Graphosec/
```

The main executable is `Graphosec.exe`.

## Create a Windows installer (Inno Setup)

1. Install [Inno Setup 6](https://jrsoftware.org/isinfo.php).
2. Publish the application using the script above.
3. Compile `installer/ResumableCopy.iss`.

The installer supports:

- Fresh installation
- Upgrade over an existing installation
- Uninstall
- Start menu shortcut
- Preserving `%LOCALAPPDATA%\Graphosec` data on uninstall (logs and user data)

## Manual installation

Copy the publish output folder to a permanent location and create a shortcut to `Graphosec.exe`.

Ensure the .NET 10 desktop runtime is installed on the target machine for framework-dependent deployments.
