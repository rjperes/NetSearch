using System.Collections;
using System.Diagnostics;

namespace NetSearch
{
    [DebuggerDisplay("Hits={Count}")]
    public class SearchResult : IEnumerable<SearchHit>
    {
        public List<SearchHit> Hits { get; } = new List<SearchHit>();
        public int Count => Hits.Count;

        IEnumerator<SearchHit> IEnumerable<SearchHit>.GetEnumerator() => Hits.GetEnumerator();

        IEnumerator IEnumerable.GetEnumerator() => Hits.GetEnumerator();
    }

    public record SearchHit
    {
        public required string Title { get; init; }
        public required string Url { get; init; }
        public required string Content { get; init; }
        public string? Image { get; init; }
        public string? Date { get; init; }
    }

    public class QueryOptions
    {
        /// <summary>
        /// Zero-based index of the page to retrieve.
        /// </summary>
        public uint? Page { get; set; }
        /// <summary>
        /// Number of results per page.
        /// </summary>
        public uint? Size { get; set; }
        /// <summary>
        /// The site from which to retrieve results.
        /// </summary>
        public string? Site { get; set; }
    }

    public interface ISearch
    {
        Task<SearchResult> Search(string query, CancellationToken cancellationToken = default);
        Task<SearchResult> Search(string query, QueryOptions options, CancellationToken cancellationToken = default);
    }
}
