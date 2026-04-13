namespace Imlinka.Tests.TestModels.Attributes;

[Traced("iface.prefix")]
internal interface ITracedContractWorker
{
    int Run();
}

internal sealed class TracedInterfaceWorker : ITracedContractWorker
{
    public int Run() =>
        0;
}