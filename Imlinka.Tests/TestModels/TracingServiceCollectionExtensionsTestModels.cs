namespace Imlinka.Tests.TestModels;

internal interface ICompatibleWorker
{
    int Calculate();
}

internal sealed class CompatibleWorker : ICompatibleWorker
{
    public int Calculate() => 42;
}

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
    byte GetFirstByte();
}

internal sealed class SpanSource : ISpanSource
{
    private static readonly byte[] Buffer = [1, 2, 3];

    public ReadOnlySpan<byte> Source() => Buffer;

    public byte GetFirstByte() => Source()[0];
}

internal interface IMixedSpanInterface
{
    string Ping();
    Guid Deserialize(ReadOnlySpan<byte> data, bool isNull);
}

internal sealed class MixedSpanImplementation : IMixedSpanInterface
{
    public string Ping() => "ok";

    public Guid Deserialize(ReadOnlySpan<byte> data, bool isNull)
    {
        if (isNull || data.Length == 0)
            return Guid.Empty;

        return new Guid(data);
    }
}

internal interface IPlainWorker
{
    int Calculate();
}

internal sealed class PlainWorker : IPlainWorker
{
    public int Calculate() => 7;
}

internal interface IInSpanConsumer
{
    int Count(in ReadOnlySpan<byte> bytes);
}

internal sealed class InSpanConsumer : IInSpanConsumer
{
    public int Count(in ReadOnlySpan<byte> bytes) => bytes.Length;
}

internal interface IMemoryConsumer
{
    int Count(ReadOnlyMemory<byte> bytes);
}

internal sealed class MemoryConsumer : IMemoryConsumer
{
    public int Count(ReadOnlyMemory<byte> bytes) => bytes.Length;
}

[Traced]
internal interface ITracedMixedSpanInterface
{
    string Ping();
    Guid Deserialize(ReadOnlySpan<byte> data, bool isNull);
}

internal sealed class TracedMixedSpanImplementation : ITracedMixedSpanInterface
{
    public string Ping() => "traced-ok";

    public Guid Deserialize(ReadOnlySpan<byte> data, bool isNull)
    {
        if (isNull || data.Length == 0)
            return Guid.Empty;

        return new Guid(data);
    }
}

[Traced]
internal interface ITracedCompatibleWorker
{
    int Calculate();
}

internal sealed class TracedCompatibleWorker : ITracedCompatibleWorker
{
    public int Calculate() => 100;
}

internal interface IEventArchive
{
    Task<int> PullChunkAsync(int cursor, int limit);
}

internal sealed class EventArchive : IEventArchive
{
    public async Task<int> PullChunkAsync(int cursor, int limit)
    {
        await Task.Delay(20);
        return cursor + limit;
    }
}

internal interface IRecordCatalog
{
    int ReadPage(int pageNumber, int pageSize);
}

internal sealed class RecordCatalog : IRecordCatalog
{
    public int ReadPage(int pageNumber, int pageSize) => pageNumber + pageSize;
}


