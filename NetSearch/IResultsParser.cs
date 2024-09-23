namespace NetSearch
{
    public interface IResultsParser
    {
        bool TryParse(string response, List<SearchResult> results);
    }
}
