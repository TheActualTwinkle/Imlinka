namespace Imlinka.Tests.TestModels.Attributes;

internal interface ITracedAndMethodTraceWorker
{
    int Compute();
}

[Traced("class.prefix")]
internal sealed class TracedAndMethodTraceWorker : ITracedAndMethodTraceWorker
{
    [Trace("override.span")]
    public int Compute() =>
        0;
}