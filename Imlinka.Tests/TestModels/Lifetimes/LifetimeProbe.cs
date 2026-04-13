namespace Imlinka.Tests.TestModels;

internal interface ILifetimeProbe
{
    Guid InstanceId();
}

internal sealed class LifetimeProbe : ILifetimeProbe
{
    private readonly Guid _instanceId = Guid.NewGuid();

    public Guid InstanceId() =>
        _instanceId;
}