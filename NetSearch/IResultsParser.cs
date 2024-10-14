namespace NetSearch
{
    public interface IResultsParser
    {
        Task<bool> TryParse(string response, List<SearchHit> results);
    }
}
