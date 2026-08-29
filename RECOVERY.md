# Recovery Model

## When recovery runs

Recovery is explicit — the application does not silently auto-resume every unfinished session.

1. On **Recover** from the UI, or when **Resume** is requested, `TransferRecoveryService` loads the session.
2. It validates source availability and identity.
3. It inspects the `.part` staging file.
4. It validates each DB-marked complete chunk against staging bytes.
5. Invalid or missing chunks are reset to **Pending**.
6. Safe sessions are marked **Paused** and ready to resume.

## What is preserved

- Completed chunk hashes in SQLite
- Staging bytes for verified chunks
- Session metadata (paths, chunk size, source identity)

## What causes recovery to fail

- Source file missing or changed incompatibly
- Staging file missing when required
- Destination unavailable
- Unrecoverable database corruption

## After application restart

1. Open the application.
2. Set the destination path used previously.
3. Use **Recover** to discover unfinished sessions.
4. Press **Resume** when the source and destination are available.

## Crash safety ordering

A chunk becomes durable only after:

1. Bytes written to staging
2. Optional flush-to-disk
3. Read-back verification
4. Hash match
5. SQLite transaction commit

If the process dies before step 5, the chunk remains pending or is reset during recovery.

## Manual cleanup

If a transfer is abandoned, you may delete:

```text
{destination-directory}\.copycache\
```

Only do this when you intentionally want to discard recoverable state.

## Database migration

Existing `sessions.db` files are upgraded in place. Transfer history is preserved across schema migrations when possible.
