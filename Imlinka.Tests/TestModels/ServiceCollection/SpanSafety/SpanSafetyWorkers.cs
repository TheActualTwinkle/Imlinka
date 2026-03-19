namespace Imlinka.Tests.TestModels;

internal interface ISpanDeserializer
{
    Guid Deserialize(ReadOnlySpan<byte> data, bool isNull);
}

internal sealed class SpanDeserializer : ISpanDeserializer
{
    public Guid Deserialize(ReadOnlySpan<byte> data, bool isNull)
    {
        if (isNull || data.Length == 0)
            return Guid.Empty;

        return new Guid(data);
    }
}

internal interface ISpanSource
{
    ReadOnlySpan<byte> Source();
}

internal sealed class SpanSource : ISpanSource
{
    private static readonly byte[] Buffer = [0, 0, 0];

    public ReadOnlySpan<byte> Source() => Buffer;
}

internal interface IMixedSpanWorker
{
    Guid Deserialize(ReadOnlySpan<byte> data, bool isNull);
}

internal sealed class MixedSpanImplementation : IMixedSpanWorker
{
    public Guid Deserialize(ReadOnlySpan<byte> data, bool isNull)
    {
        if (isNull || data.Length == 0)
            return Guid.Empty;

        return new Guid(data);
    }
}

internal interface IInSpanConsumer
{
    int Count(in ReadOnlySpan<byte> bytes);
}

internal sealed class InSpanConsumer : IInSpanConsumer
{
    public int Count(in ReadOnlySpan<byte> bytes) => 0;
}

internal interface IMemoryConsumer
{
    int Count(ReadOnlyMemory<byte> bytes);
}

internal sealed class MemoryConsumer : IMemoryConsumer
{
    public int Count(ReadOnlyMemory<byte> bytes) => 0;
}

[Traced]
internal interface ITracedMixedSpanWorker
{
    Guid Deserialize(ReadOnlySpan<byte> data, bool isNull);
}

internal sealed class TracedMixedSpanImplementation : ITracedMixedSpanWorker
{
    public Guid Deserialize(ReadOnlySpan<byte> data, bool isNull)
    {
        if (isNull || data.Length == 0)
            return Guid.Empty;

        return new Guid(data);
    }
}