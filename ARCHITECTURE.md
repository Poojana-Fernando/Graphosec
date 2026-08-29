# Architecture

## Layers

```text
ResumableCopy.App (WPF)
        ↓
ResumableCopy.Application (MVVM, orchestration)
        ↓
ResumableCopy.Core (copy engine, persistence, security)
```

The UI never performs copy logic directly. All transfers go through `ITransferOrchestrator` into `ICopyEngine`.

## Core workflow

```text
Validate paths
        ↓
Capture source identity
        ↓
Create/load session (SQLite)
        ↓
Copy pending chunks (sequential or parallel)
        ↓
Verify whole file (optional)
        ↓
Atomically replace destination
        ↓
Delete session metadata
```

## Key components

| Component | Responsibility |
|-----------|----------------|
| `CopyEngine` | Session lifecycle, finalization, verification |
| `ParallelChunkCopyExecutor` | Bounded chunk workers |
| `SqliteSessionRepository` | Durable session/chunk state |
| `TransferRecoveryService` | Startup-style recovery and chunk reconciliation |
| `TransferEnvironmentMonitor` | Source/destination/storage readiness |
| `PathValidator` | Unsafe path rejection |
| `CopyPerformanceAdvisor` | Adaptive chunk/worker/buffer tuning |

## Staging layout

For destination `D:\backups\file.bin`:

```text
D:\backups\.copycache\
  sessions.db
  {session-id}.part
```

## State model

Transfers use the `CopyState` enum (`Pending`, `Running`, `Paused`, `WaitingForSource`, `WaitingForDestination`, `WaitingForStorage`, `Verifying`, `Completed`, `Failed`, `Cancelled`, `RecoveryRequired`).

State transitions are persisted before reporting success.

## Database schema

SQLite schema is versioned through `schema_version` and migrated by `SqliteMigrationRunner`.

Current schema version: **2**.

## Integrity guarantees

- A chunk is complete only after write, flush, read-back hash verification, and DB commit.
- Finalization uses `File.Replace` / same-volume move semantics.
- Source identity (size, timestamps, optional file ID) is checked before completion.

## Threading

- Parallel workers use a bounded `Channel<ChunkRecord>`.
- Chunk ownership is coordinated through `ChunkWorkCoordinator`.
- UI progress is throttled in the Application layer.

## Logging and diagnostics

Structured logging uses `Microsoft.Extensions.Logging`.

Transfer diagnostics include application version, OS, paths, sizes, worker/chunk settings, elapsed time, and failure details.
