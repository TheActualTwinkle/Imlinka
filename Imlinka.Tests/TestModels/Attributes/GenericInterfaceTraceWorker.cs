namespace Imlinka.Tests.TestModels.Attributes;

internal interface IGenericTraceWorker<T>
{
    [Trace("generic.interface.span")]
    void Handle(T value);

    [Trace("generic.interface.collection.span")]
    void HandleMany(IReadOnlyList<T> values);
}

internal interface IInheritedGenericTraceWorker<T> : IGenericTraceWorker<T>
{
}

internal sealed class GenericInterfaceTraceWorker : IInheritedGenericTraceWorker<string>
{
    public void Handle(string value)
    {
    }

    public void HandleMany(IReadOnlyList<string> values)
    {
    }
}
