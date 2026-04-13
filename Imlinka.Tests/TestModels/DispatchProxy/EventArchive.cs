namespace Imlinka.Tests.TestModels;

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