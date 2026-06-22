namespace Imlinka.Tests.TestModels;

internal interface ICompatibleWorker
{
    public string? Text { get; }

    void Do();
}

internal sealed class CompatibleWorker(string? text = null) : ICompatibleWorker
{
    public string? Text =>
        text;

    public void Do()
    {
    }
}

internal interface IPlainWorker
{
    void Work();
}

internal sealed class PlainWorker : IPlainWorker
{
    public void Work()
    {
    }
}

[Traced]
internal interface ITracedCompatibleWorker;

internal sealed class TracedCompatibleWorker : ITracedCompatibleWorker;
