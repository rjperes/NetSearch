using AngleSharp;
using Microsoft.Extensions.Options;
using Microsoft.Net.Http.Headers;
using System.Text;

namespace NetSearch
{
    public class GoogleSearch : ISearch
    {
        private class Google1ResultsParser : IResultsParser
        {
            public async Task<bool> TryParse(string response, List<SearchResult> results)
            {
                using var context = BrowsingContext.New(Configuration.Default);
                using var doc = await context.OpenAsync(req => req.Content(response));

                var resultsContainer = doc.QuerySelector("div#search");

                var individualResults = resultsContainer!.QuerySelectorAll("div[jscontroller]");

                foreach (var individualResult in individualResults)
                {
                    var titleNode = individualResult.QuerySelector("h3");

                    if (titleNode == null)
                    {
                        continue;
                    }

                    var title = titleNode?.InnerHtml;

                    var imageNode = individualResult.QuerySelector("img[src]");
                    var image = imageNode?.Attributes["src"]?.Value;

                    var urlNode = individualResult.QuerySelector("a[jsname][href]");
                    var url = urlNode?.Attributes["href"]!.Value;

                    var contentNodes = individualResult.QuerySelectorAll("div[data-snf][data-sncf] div span");

                    string? date = string.Empty;
                    string content = string.Empty;

                    if (contentNodes != null)
                    {
                        date = contentNodes.Count() > 1 ? contentNodes.ElementAt(1).InnerHtml : string.Empty;
                        content = contentNodes.Count() > 2 ? contentNodes.ElementAt(2).InnerHtml : string.Empty;
                    }

                    var result = new SearchResult
                    {
                        Title = title!,
                        Url = url!,
                        Content = content,
                        Image = image,
                        Date = date
                    };

                    results.Add(result);
                }

                return results.Any();
            }
        }

        private readonly HttpClient _httpClient;
        private static readonly IEnumerable<IResultsParser> _parsers = [ new Google1ResultsParser() ];
        private const int ResultsPerPage = 9;

        public GoogleSearch(HttpClient httpClient, IOptions<SearchOptions> options)
        {
            ArgumentNullException.ThrowIfNull(httpClient, nameof(httpClient));

            _httpClient = httpClient;

            if (options?.Value != null)
            {
                if (!string.IsNullOrWhiteSpace(options.Value.UserAgent))
                {
                    _httpClient.DefaultRequestHeaders.Add(HeaderNames.UserAgent, options.Value.UserAgent);
                }

                if (((bool?)options?.Value?.AcceptLanguages.Any()).GetValueOrDefault())
                {
                    _httpClient.DefaultRequestHeaders.Add(HeaderNames.AcceptLanguage, string.Join(",", options?.Value?.AcceptLanguages!));
                }
            }
        }

        public Task<List<SearchResult>> Search(string query, CancellationToken cancellationToken = default) => Search(query, new QueryOptions(), cancellationToken);

        public async Task<List<SearchResult>> Search(string query, QueryOptions options, CancellationToken cancellationToken = default)
        {
            var requestUrl = new StringBuilder($"?q={Uri.EscapeDataString(query)}");

            if (!string.IsNullOrWhiteSpace(options.Site))
            {
                var site = options.Site;

                if (Uri.TryCreate(options.Site, UriKind.Absolute, out var url))
                {
                    site = url.Host;
                }
                else if (Uri.TryCreate(options.Site, UriKind.Relative, out url))
                {
                    throw new InvalidOperationException($"Invalid site '{options.Site}");
                }

                requestUrl.Append(Uri.EscapeDataString($" site={site}"));
            }

            if (options.Page != null && options.Page.Value != 0)
            {
                requestUrl.Append($"&start={options.Page.Value + ResultsPerPage}");
            }

            var escapedRequestUrl = requestUrl.ToString();

            var response = await _httpClient.GetStringAsync(escapedRequestUrl, cancellationToken);

            var results = new List<SearchResult>();

            foreach (var parser in _parsers)
            {
                if (await parser.TryParse(response, results))
                {
                    break;
                }
            }

            return results;
        }
    }
}
