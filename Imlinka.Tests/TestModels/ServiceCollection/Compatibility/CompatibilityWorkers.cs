namespace Imlinka.Tests.TestModels;

internal interface ICompatibleWorker
{
    public string? Text { get; }
}

internal sealed class CompatibleWorker(string? text = null) : ICompatibleWorker
{
    public string? Text =>
        text;
}

internal interface IPlainWorker;

internal sealed class PlainWorker : IPlainWorker;

[Traced]
internal interface ITracedCompatibleWorker;

internal sealed class TracedCompatibleWorker : ITracedCompatibleWorker;