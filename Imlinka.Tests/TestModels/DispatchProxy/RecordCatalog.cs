namespace Imlinka.Tests.TestModels;

internal interface IRecordCatalog
{
    int ReadPage(int pageNumber, int pageSize);
}

internal sealed class RecordCatalog : IRecordCatalog
{
    public int ReadPage(int pageNumber, int pageSize) =>
        pageNumber + pageSize;
}