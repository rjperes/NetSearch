namespace NetSearch
{
    public interface IResultsParser
    {
        Task<bool> TryParse(string response, List<SearchHit> results);
    }

    public sealed class EmptyResultsParser : IResultsParser
    {
        public Task<bool> TryParse(string response, List<SearchHit> results)
        {
            results.Clear();
            return Task.FromResult(false);
        }
    }
}
