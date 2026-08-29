namespace ResumableCopy.Core.Reliability;

public interface IFaultInjector
{
    void Apply(FaultPoint point, FaultContext context);
}
