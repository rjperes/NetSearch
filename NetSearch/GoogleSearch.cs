using System.Net;
using System.Text;

namespace NetSearch
{
    public class GoogleSearch : ISearch
    {
        private readonly HttpClient _httpClient;

        public GoogleSearch(HttpClient httpClient)
        {
            _httpClient = httpClient;
        }

        public Task<List<SearchResults>> Search(string query, CancellationToken cancellationToken = default) => Search(query, new QueryOptions(), cancellationToken);

        public async Task<List<SearchResults>> Search(string query, QueryOptions options, CancellationToken cancellationToken = default)
        {
            var requestUrl = new StringBuilder($"?q={Uri.EscapeDataString(query)}");

            if (options.Page != null && options.Page.Value != 0)
            {
                requestUrl.Append($"&start={options.Page.Value * options.Size.GetValueOrDefault(10)}");
            }

            var escapedRequestUrl = requestUrl.ToString();

            var results = await _httpClient.GetStringAsync(escapedRequestUrl, cancellationToken);

            return null;
        }
    }
}
