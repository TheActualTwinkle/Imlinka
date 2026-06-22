namespace Imlinka.Tests.TestModels.Proxied;

internal interface IWhitelistedWorker
{
    void Run();
}

internal sealed class WhitelistedWorker : IWhitelistedWorker
{
    public void Run()
    {
    }
}
