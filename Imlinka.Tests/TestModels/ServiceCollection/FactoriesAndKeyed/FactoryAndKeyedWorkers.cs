namespace Imlinka.Tests.TestModels;

internal interface IFactoryJumper
{
    public string Text { get; }
}

internal sealed class FactoryJumper(string text) : IFactoryJumper
{
    public string Text =>
        text;
}

internal interface IKeyedWorker;

internal sealed class KeyedWorker : IKeyedWorker;

internal interface IKeyedFactoryWorker
{
    public string Text { get; }
}

internal sealed class KeyedFactoryWorker(string text) : IKeyedFactoryWorker
{
    public string Text =>
        text;
}