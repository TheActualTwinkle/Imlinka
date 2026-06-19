namespace Imlinka.Tests.TestModels.Attributes;

internal sealed class RefReturnWorker
{
    private int _value = 42;

    [Trace("ref.return.span")]
    public ref int GetValue()
    {
        return ref _value;
    }
}
