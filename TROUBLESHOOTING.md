# Troubleshooting

## Transfer stuck in "Waiting for device"

The destination drive was disconnected. Reconnect the drive and press **Resume**.

## Transfer stuck in "Waiting for source"

The source file is unavailable. Reconnect the source media or restore the file, then press **Resume**.

## "Insufficient space"

Free space on the destination, then press **Resume**.

## "Source file changed"

The source file changed after the transfer started. Review whether continuing is safe. Recovery may mark the session as `RecoveryRequired`.

## Invalid path / reserved device name

Destination or source paths must be absolute, must not overlap, and must not use reserved Windows device names such as `CON` or `COM1`.

## Symlink or junction source rejected

Reparse points are not supported as copy sources. Copy the target file directly.

## Destination already exists

Enable **Overwrite existing** or choose a different destination path.

## Logs

Check:

```text
%LOCALAPPDATA%\ResumableCopy\logs\
```

Increase verbosity in `appsettings.json`:

```json
{
  "ResumableCopy": {
    "Logging": {
      "MinimumLevel": "Debug"
    }
  }
}
```

## Database issues

If SQLite metadata is corrupt but staging bytes are intact, recovery may reset invalid chunks. Do not delete `.copycache` until you understand what will be lost.

## Performance tuning

See [DEVELOPMENT.md](DEVELOPMENT.md#performance-tuning).

## Getting diagnostics

Completed and failed transfers log a diagnostic report containing session ID, paths, sizes, worker count, chunk size, and elapsed time.
