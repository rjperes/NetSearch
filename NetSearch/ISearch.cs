namespace NetSearch
{
    public class SearchResults
    {
        public required string Title { get; set; }
        public required Uri Url { get; set; }
        public required string Content { get; set; }
    }

    public class QueryOptions
    {
        public uint? Page { get; set; }
        public uint? Size { get; set; }
        public string? Site { get; set; }
    }

    public interface ISearch
    {
        Task<List<SearchResults>> Search(string query, CancellationToken cancellationToken = default);
        Task<List<SearchResults>> Search(string query, QueryOptions options, CancellationToken cancellationToken = default);
    }
}