namespace Imlinka.Tests.TestModels.Attributes;

[Traced("base.prefix")]
internal abstract class BaseTracedWorker;

internal interface IInheritedTracedWorker
{
    int Ping();
}

internal sealed class InheritedTracedWorker : BaseTracedWorker, IInheritedTracedWorker
{
    public int Ping() =>
        0;
}