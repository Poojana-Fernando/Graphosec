namespace ResumableCopy.Core.Reliability;

public sealed class NullFaultInjector : IFaultInjector
{
    public static NullFaultInjector Instance { get; } = new();

    private NullFaultInjector()
    {
    }

    public void Apply(FaultPoint point, FaultContext context)
    {
    }
}
