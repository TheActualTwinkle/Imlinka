namespace Imlinka;

/// <summary>
/// Marks a specific method as traceable so the invocation is wrapped in a trace span.
/// </summary>
[AttributeUsage(AttributeTargets.Method)]
public sealed class TraceAttribute(string? spanName = null) : Attribute
{
    /// <summary>
    /// Optional custom span name. If not provided, the span will be named after the method.
    /// </summary>
    public string? SpanName { get; } = spanName;
}