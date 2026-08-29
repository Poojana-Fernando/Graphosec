# Development Guide

## Solution layout

```text
src/ResumableCopy.Core/          Engine, persistence, security
src/ResumableCopy.Application/   MVVM orchestration
src/ResumableCopy.App/           WPF shell
tests/ResumableCopy.Core.Tests/
tests/ResumableCopy.Application.Tests/
```

## Common commands

```bash
dotnet build -warnaserror
dotnet test
dotnet test --filter "Category=Benchmark"
dotnet run --project src/ResumableCopy.App/ResumableCopy.App.csproj
```

## Testing

Core tests cover copy, resume, concurrency, fault injection, monitoring, security, migrations, and benchmarks.

Application tests cover ViewModel/orchestrator behavior.

Production gate tests verify version metadata and scenario coverage entry points.

## Configuration

`src/ResumableCopy.App/appsettings.json`:

| Setting | Purpose |
|---------|---------|
| `ResumableCopy:Logging:MinimumLevel` | Log verbosity |
| `ResumableCopy:Logging:LogDirectory` | File log location |
| `ResumableCopy:Copy:*` | Default copy options |
| `ResumableCopy:Staging:CacheDirectoryName` | Staging folder name |

Environment variables in paths (for example `%LOCALAPPDATA%`) are expanded at runtime.

## Performance tuning

Adaptive performance is enabled by default. It adjusts chunk size, workers, queue depth, and I/O buffer size from file size.

Override in `appsettings.json`:

```json
{
  "ResumableCopy": {
    "Copy": {
      "ChunkSize": 4194304,
      "MaximumWorkers": 4,
      "MaximumQueuedChunks": 8,
      "IoBufferSize": 131072,
      "UseAdaptivePerformance": false
    }
  }
}
```

Benchmark tests are tagged `Category=Benchmark`.

## Database migrations

When changing SQLite schema:

1. Increment `SqliteSchema.CurrentVersion`.
2. Add a migration step in `SqliteMigrationRunner`.
3. Add a test in `DatabaseMigrationTests`.

Never drop transfer tables without an explicit migration strategy.

## Release process

1. Update version in `Directory.Build.props`.
2. Run the production gate:

```bash
dotnet clean
dotnet restore
dotnet build -warnaserror
dotnet test
dotnet build -c Release -warnaserror
dotnet test -c Release
```

3. Publish:

```powershell
.\installer\publish-release.ps1
```

4. Build the installer with Inno Setup using `installer/ResumableCopy.iss`.

## Dependency injection

- Core services: `AddResumableCopyCore()`
- Application services: `AddResumableCopyApplication(configuration)`

The WPF app wires logging, configuration, and global exception handling in `App.xaml.cs`.

## Coding guidelines

- Preserve integrity before performance.
- Keep UI free of filesystem/copy logic.
- Use bounded queues and explicit state transitions.
- Log state changes; never log secrets or file contents.
