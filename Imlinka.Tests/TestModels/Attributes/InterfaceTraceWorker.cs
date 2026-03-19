namespace Imlinka.Tests.TestModels.Attributes;

internal interface IMethodTraceWorker
{
    [Trace("custom.interface.span")]
    int Work();
}

internal sealed class InterfaceTraceWorker : IMethodTraceWorker
{
    public int Work() =>
        0;
}