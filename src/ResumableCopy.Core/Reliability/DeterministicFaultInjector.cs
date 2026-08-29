namespace ResumableCopy.Core.Reliability;

public sealed class DeterministicFaultInjector : IFaultInjector
{
    private readonly IReadOnlyList<FaultRule> _rules;
    private readonly Dictionary<string, int> _occurrences = new(StringComparer.Ordinal);

    public DeterministicFaultInjector(params FaultRule[] rules)
    {
        ArgumentNullException.ThrowIfNull(rules);
        _rules = rules;
    }

    public DeterministicFaultInjector(IEnumerable<FaultRule> rules)
    {
        ArgumentNullException.ThrowIfNull(rules);
        _rules = rules.ToArray();
    }

    public void Apply(FaultPoint point, FaultContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        foreach (var rule in _rules)
        {
            if (rule.Point != point)
            {
                continue;
            }

            if (rule.ChunkIndex is int chunkIndex && context.ChunkIndex != chunkIndex)
            {
                continue;
            }

            var key = BuildKey(rule);
            _occurrences.TryGetValue(key, out var count);
            count++;
            _occurrences[key] = count;

            if (count != rule.Occurrence)
            {
                continue;
            }

            ExecuteRule(rule, point, context);
        }
    }

    public int GetTriggerCount(FaultRule rule)
    {
        ArgumentNullException.ThrowIfNull(rule);
        return _occurrences.GetValueOrDefault(BuildKey(rule));
    }

    private static string BuildKey(FaultRule rule) =>
        $"{(int)rule.Point}:{rule.ChunkIndex}:{rule.Kind}:{rule.Occurrence}";

    private static void ExecuteRule(FaultRule rule, FaultPoint point, FaultContext context)
    {
        if (rule.Kind == FaultKind.SlowIo)
        {
            Thread.Sleep(rule.DelayMilliseconds);
            return;
        }

        if (rule.Kind == FaultKind.CorruptBytes)
        {
            if (context.Buffer is { } buffer && buffer.Length > rule.CorruptByteOffset)
            {
                buffer.Span[rule.CorruptByteOffset] = rule.CorruptByteValue;
            }

            return;
        }

        if (rule.Kind == FaultKind.HashMismatch)
        {
            throw FaultExceptionFactory.Create(FaultKind.HashMismatch, point, context);
        }

        throw FaultExceptionFactory.Create(rule.Kind, point, context);
    }
}
