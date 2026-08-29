using ResumableCopy.Core.Domain;



namespace ResumableCopy.Core.Errors;



public static class CopyStateMapper

{

    public static CopyState ResolveWaitingState(CopyException exception)

    {

        ArgumentNullException.ThrowIfNull(exception);



        return exception switch

        {

            SourceUnavailableException => CopyState.WaitingForSource,

            DestinationUnavailableException => CopyState.WaitingForDestination,

            InsufficientStorageException => CopyState.WaitingForStorage,

            SourceChangedException => CopyState.RecoveryRequired,

            _ when exception.FailureKind == CopyFailureKind.Recoverable => CopyState.WaitingForDestination,

            _ => CopyState.Failed

        };

    }



    public static CopyState ResolveWaitingState(Exception exception)

    {

        ArgumentNullException.ThrowIfNull(exception);



        if (exception is CopyException copyException)

        {

            return ResolveWaitingState(copyException);

        }



        return ResolveWaitingState(TransientErrorClassifier.Classify(exception, "Transfer operation failed"));

    }

}


