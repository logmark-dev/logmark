namespace LogMark.Services.Search;

public interface IDocumentSource
{
    SearchFamily Family { get; }
    IEnumerable<SearchIndexRecord> GetRecords();
    DateTime NewestMtime();
}
