using AngleSharp;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.Net.Http.Headers;
using System.Text;

namespace NetSearch
{
    public enum GoogleSearchType
    {
        Video,
        News,
        Images,
        Web
    }

    public class GoogleQueryOptions : QueryOptions
    {
        public GoogleSearchType? SearchType { get; init; }
    }

    public class GoogleSearch : ISearch
    {
        private class ChromeResultsParser : IResultsParser
        {
            public async Task<bool> TryParse(string response, List<SearchHit> results)
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

                    var result = new SearchHit
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
        private readonly ILogger<GoogleSearch> _logger;
        private static readonly List<IResultsParser> _parsers = [new ChromeResultsParser()];
        private const int ResultsPerPage = 9;
        private const string Name = "Google";

        public GoogleSearch(HttpClient httpClient, IOptions<SearchOptions> options, ILogger<GoogleSearch> logger, IEnumerable<IResultsParser> parsers)
        {
            ArgumentNullException.ThrowIfNull(httpClient, nameof(httpClient));

            _httpClient = httpClient;
            _logger = logger;
            
            if (parsers != null && parsers.Any())
            {
                _parsers.AddRange(parsers);
            }

            if (options?.Value != null)
            {
                if (!string.IsNullOrWhiteSpace(options.Value.UserAgent))
                {
                    _logger.LogDebug($"Setting user-agent to '{options.Value.UserAgent}'");
                    _httpClient.DefaultRequestHeaders.Add(HeaderNames.UserAgent, options.Value.UserAgent);
                }

                if (((bool?)options?.Value?.AcceptLanguages.Any()).GetValueOrDefault())
                {
                    _logger.LogDebug($"Setting accepted languages to '{string.Join(",", options!.Value.AcceptLanguages)}'");
                    _httpClient.DefaultRequestHeaders.Add(HeaderNames.AcceptLanguage, string.Join(",", options?.Value?.AcceptLanguages!));
                }
            }
        }

        public Task<SearchResult> Search(string query, CancellationToken cancellationToken = default) => Search(query, new QueryOptions(), cancellationToken);

        public async Task<SearchResult> Search(string query, QueryOptions options, CancellationToken cancellationToken = default)
        {
            var requestUrl = new StringBuilder($"?q={Uri.EscapeDataString(query)}");
            var result = new SearchResult();

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

                _logger.LogDebug($"Setting filtered site to '{site}'");
                requestUrl.Append($" site:{site}");
            }

            if (options is GoogleQueryOptions googleOptions)
            {
                if (googleOptions.SearchType != null)
                {
                    _logger.LogDebug($"Setting search type to '{googleOptions.SearchType.ToString()!.ToLower()}'");
                    requestUrl.Append($" {googleOptions.SearchType.ToString()!.ToLower()}");
                }
            }

            if (options.Size != null)
            {
                _logger.LogWarning("Size setting not supported by Google");
            }

            if (options.Page != null && options.Page.Value != 0)
            {
                var page = (options.Page.Value * ResultsPerPage) + options.Page.Value;
                _logger.LogDebug($"Setting results start to '{page}'");
                requestUrl.Append($"&start={page}");
            }

            var escapedRequestUrl = requestUrl.ToString();

            var response = await _httpClient.GetStringAsync(escapedRequestUrl, cancellationToken);

            foreach (var parser in _parsers)
            {
                try
                {
                    if (await parser.TryParse(response, result.Hits))
                    {
                        break;
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, $"An error occurred while parsing the response in {parser}");
                }
            }

            return result;
        }
    }
}
