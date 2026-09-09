using HtmlAgilityPack;
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
            public Task<bool> TryParse(string response, List<SearchHit> results)
            {
                var doc = new HtmlDocument();
                doc.LoadHtml(response);

                var resultsContainer = doc.DocumentNode.SelectSingleNode("//div[@id='search']");
                if (resultsContainer == null)
                {
                    return Task.FromResult(false);
                }

                var individualResults = resultsContainer.SelectNodes(".//div[@jscontroller]");
                if (individualResults == null)
                {
                    return Task.FromResult(false);
                }

                foreach (var individualResult in individualResults)
                {
                    var titleNode = individualResult.SelectSingleNode(".//h3");

                    if (titleNode == null)
                    {
                        continue;
                    }

                    var title = HtmlEntity.DeEntitize(titleNode.InnerText).Trim();
                    if (string.IsNullOrWhiteSpace(title))
                    {
                        continue;
                    }

                    var imageNode = individualResult.SelectSingleNode(".//img[@src]");
                    var image = imageNode?.GetAttributeValue("src", default(string));

                    var urlNode = individualResult.SelectSingleNode(".//a[@jsname and @href]");
                    var url = urlNode?.GetAttributeValue("href", null);
                    if (string.IsNullOrWhiteSpace(url))
                    {
                        continue;
                    }

                    var contentNodes = individualResult.SelectNodes(".//div[@data-snf and @data-sncf]//div//span");

                    string? date = string.Empty;
                    string content = string.Empty;

                    if (contentNodes is { Count: > 0 })
                    {
                        date = contentNodes.Count > 1 ? HtmlEntity.DeEntitize(contentNodes[1].InnerText).Trim() : string.Empty;
                        content = contentNodes.Count > 2 ? HtmlEntity.DeEntitize(contentNodes[2].InnerText).Trim() : string.Empty;
                    }

                    var result = new SearchHit
                    {
                        Title = title,
                        Url = url,
                        Content = content,
                        Image = image,
                        Date = date
                    };

                    results.Add(result);
                }

                return Task.FromResult(results.Any());
            }
        }

        private readonly HttpClient _httpClient;
        private readonly ILogger<GoogleSearch> _logger;
        private readonly List<IResultsParser> _parsers = [new ChromeResultsParser()];
        private const int ResultsPerPage = 9;
        private const string VideoSearchFilter = "vid";
        private const string NewsSearchFilter = "nws";
        private const string ImagesSearchFilter = "isch";

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
            var result = new SearchResult();
            var queryText = new StringBuilder(query);
            var requestUrl = new StringBuilder();

            if (!string.IsNullOrWhiteSpace(options.Site))
            {
                var site = options.Site;

                if (Uri.TryCreate(options.Site, UriKind.Absolute, out var url))
                {
                    site = url.Host;
                }
                else if (Uri.TryCreate(options.Site, UriKind.Relative, out url))
                {
                    throw new InvalidOperationException($"Invalid site '{options.Site}'");
                }

                _logger.LogDebug($"Setting filtered site to '{site}'");
                queryText.Append($" site:{site}");
            }

            if (options is GoogleQueryOptions googleOptions && googleOptions.SearchType != null)
            {
                var searchType = googleOptions.SearchType.Value;
                _logger.LogDebug($"Setting search type to '{searchType}'");
                var searchFilter = GetSearchFilter(searchType);
                if (!string.IsNullOrWhiteSpace(searchFilter))
                {
                    requestUrl.Append($"&tbm={searchFilter}");
                }
            }

            requestUrl.Insert(0, $"?q={Uri.EscapeDataString(queryText.ToString())}");

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

        private static string? GetSearchFilter(GoogleSearchType searchType)
            => searchType switch
            {
                GoogleSearchType.Web => null,
                GoogleSearchType.Video => VideoSearchFilter,
                GoogleSearchType.News => NewsSearchFilter,
                GoogleSearchType.Images => ImagesSearchFilter,
                _ => throw new ArgumentOutOfRangeException(nameof(searchType), searchType, null)
            };
    }
}
