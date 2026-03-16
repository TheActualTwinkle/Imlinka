namespace Imlinka;

/// <summary>
/// Marks a class/interface as traceable so all its public methods are wrapped in trace spans.
/// </summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Interface)]
public sealed class TracedAttribute(string? spanNamePrefix = null) : Attribute
{
    /// <summary>
    /// Optional custom span name prefix. If not provided, the span will be named after the method.
    /// </summary>
    public string? SpanNamePrefix { get; } = spanNamePrefix;
}