using System.Runtime.InteropServices;
using Microsoft.Data.Sqlite;

namespace ResumableCopy.Core.Errors;

public static class TransientErrorClassifier
{
    private const int HResultSharingViolation = unchecked((int)0x80070020);
    private const int HResultLockViolation = unchecked((int)0x80070021);
    private const int HResultDeviceNotReady = unchecked((int)0x80070015);
    private const int HResultDiskFull = unchecked((int)0x80070027);
    private const int HResultHandleDiskFull = unchecked((int)0x80070070);

    public static CopyException Classify(Exception exception, string context)
    {
        ArgumentNullException.ThrowIfNull(exception);

        return exception switch
        {
            SessionPersistenceException sessionPersistenceException
                when sessionPersistenceException.InnerException is SqliteException sqliteException
                    && IsSqliteDestinationUnavailable(sqliteException) =>
                new DestinationUnavailableException(
                    "Destination storage is unavailable.",
                    sessionPersistenceException),
            CopyException copyException => copyException,
            UnauthorizedAccessException unauthorizedAccessException =>
                new PermissionDeniedException($"{context}: access denied.", unauthorizedAccessException),
            IOException ioException when IsInsufficientStorage(ioException) =>
                new InsufficientStorageException($"{context}: insufficient storage.", ioException),
            IOException ioException when IsRecoverableIo(ioException) =>
                new DestinationUnavailableException($"{context}: temporary I/O failure.", ioException),
            _ => new CopyException(CopyFailureKind.Permanent, $"{context}: {exception.Message}", exception)
        };
    }

    public static bool IsRecoverableIo(IOException exception)
    {
        ArgumentNullException.ThrowIfNull(exception);

        var hResult = exception.HResult;
        return hResult is HResultSharingViolation
            or HResultLockViolation
            or HResultDeviceNotReady;
    }

    public static bool IsInsufficientStorage(IOException exception)
    {
        ArgumentNullException.ThrowIfNull(exception);

        return exception.HResult is HResultDiskFull or HResultHandleDiskFull;
    }

    public static bool IsDeviceNotReady(IOException exception)
    {
        ArgumentNullException.ThrowIfNull(exception);
        return exception.HResult == HResultDeviceNotReady;
    }

    private static bool IsSqliteDestinationUnavailable(SqliteException exception) =>
        exception.SqliteErrorCode is 10 or 14;
}
