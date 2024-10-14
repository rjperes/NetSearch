namespace NetSearch
{
    public interface IResultsParser
    {
        Task<bool> TryParse(string response, List<SearchResult> results);
    }
}
