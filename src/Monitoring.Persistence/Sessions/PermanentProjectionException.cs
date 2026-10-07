namespace Monitoring.Persistence.Sessions;

// A cause that no retry can fix (invalid contract, suppressed identity, conflicting projection). The projector quarantines the
// event instead of failing the worker. The cause is a minimal code, never a fragment of the event.
internal sealed class PermanentProjectionException(string cause) : Exception(cause)
{
    public string Cause { get; } = cause;
}
