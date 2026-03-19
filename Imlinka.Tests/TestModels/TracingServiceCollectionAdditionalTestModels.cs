namespace Imlinka.Tests.TestModels
{
    internal interface ILifetimeProbe
    {
        Guid InstanceId();
    }

    internal sealed class LifetimeProbe : ILifetimeProbe
    {
        private readonly Guid _instanceId = Guid.NewGuid();

        public Guid InstanceId() => _instanceId;
    }
}

namespace Imlinka.Tests.TestModels.Proxied
{
    internal interface IWhitelistedWorker
    {
        int Calculate();
    }

    internal sealed class WhitelistedWorker : IWhitelistedWorker
    {
        public int Calculate() => 11;
    }
}

namespace Imlinka.Tests.TestModels.NotProxied
{
    internal interface INonWhitelistedWorker
    {
        int Calculate();
    }

    internal sealed class NonWhitelistedWorker : INonWhitelistedWorker
    {
        public int Calculate() => 22;
    }
}