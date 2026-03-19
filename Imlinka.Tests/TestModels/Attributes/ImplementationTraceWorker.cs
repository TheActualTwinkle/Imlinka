namespace Imlinka.Tests.TestModels.Attributes;

internal interface IImplementationTraceWorker
{
    int Important();
    
    int Plain();
}

internal sealed class ImplementationTraceWorker : IImplementationTraceWorker
{
    [Trace]
    public int Important() =>
        0;

    public int Plain() =>
        0;
}