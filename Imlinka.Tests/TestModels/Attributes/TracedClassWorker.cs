namespace Imlinka.Tests.TestModels.Attributes;

internal interface ITracedClassWorker
{
    int Execute();
}

[Traced("class.prefix")]
internal sealed class TracedClassWorker : ITracedClassWorker
{
    public int Execute() =>
        0;
}