using HtmlAgilityPack;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.Net.Http.Headers;
using System.Text;

namespace NetSearch
{
    public enum BingSearchType
    {
        Video,
        News,
        Images,
        Web
    }

    public class BingQueryOptions : QueryOptions
    {
        public BingSearchType? SearchType { get; init; }
    }

    public class BingSearch : ISearch
    {
        private class BingResultsParser : IResultsParser
        {
            public Task<bool> TryParse(string response, List<SearchHit> results)
            {
                var doc = new HtmlDocument();
                doc.LoadHtml(response);

                var resultsContainer = doc.DocumentNode.SelectSingleNode("//ol[@id='b_results']") ?? doc.DocumentNode;
                var individualResults = resultsContainer.SelectNodes(".//li[contains(@class,'b_algo')] | .//li[.//h2/a[@href]]");
                if (individualResults == null)
                {
                    return Task.FromResult(false);
                }

                var urls = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

                foreach (var individualResult in individualResults)
                {
                    var linkNode = individualResult.SelectSingleNode(".//h2/a[@href]");
                    if (linkNode == null)
                    {
                        continue;
                    }

                    var title = HtmlEntity.DeEntitize(linkNode.InnerText).Trim();
                    var url = linkNode.GetAttributeValue("href", null);
                    if (string.IsNullOrWhiteSpace(title) || string.IsNullOrWhiteSpace(url))
                    {
                        continue;
                    }

                    if (!urls.Add(url))
                    {
                        continue;
                    }

                    var content = HtmlEntity.DeEntitize(individualResult.SelectSingleNode(".//div[contains(@class,'b_caption')]//p | .//p")?.InnerText ?? string.Empty).Trim();
                    var image = individualResult.SelectSingleNode(".//img[@src]")?.GetAttributeValue("src", default(string));
                    var date = HtmlEntity.DeEntitize(individualResult.SelectSingleNode(".//span[contains(@class,'news_dt')]")?.InnerText ?? string.Empty).Trim();

                    results.Add(new SearchHit
                    {
                        Title = title,
                        Url = url,
                        Content = content,
                        Image = image,
                        Date = date
                    });
                }

                return Task.FromResult(results.Any());
            }
        }

        private readonly HttpClient _httpClient;
        private readonly ILogger<BingSearch> _logger;
        private readonly List<IResultsParser> _parsers = [new BingResultsParser()];
        private const int ResultsPerPage = 10;
        private const string WebSearchPath = "/search";
        private const string VideoSearchPath = "/videos/search";
        private const string NewsSearchPath = "/news/search";
        private const string ImagesSearchPath = "/images/search";

        public BingSearch(HttpClient httpClient, IOptions<SearchOptions> options, ILogger<BingSearch> logger, IEnumerable<IResultsParser> parsers)
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
            var requestPath = WebSearchPath;

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

            if (options is BingQueryOptions bingOptions && bingOptions.SearchType != null)
            {
                var searchType = bingOptions.SearchType.Value;
                _logger.LogDebug($"Setting search type to '{searchType}'");
                requestPath = GetSearchPath(searchType);
            }

            var requestUrl = new StringBuilder($"{requestPath}?q={Uri.EscapeDataString(queryText.ToString())}");

            if (options.Size != null)
            {
                _logger.LogWarning("Size setting not supported by Bing");
            }

            if (options.Page != null && options.Page.Value != 0)
            {
                var page = (options.Page.Value * ResultsPerPage) + 1;
                _logger.LogDebug($"Setting results start to '{page}'");
                requestUrl.Append($"&first={page}");
            }

            var response = await _httpClient.GetStringAsync(requestUrl.ToString(), cancellationToken);

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

        private static string GetSearchPath(BingSearchType searchType)
            => searchType switch
            {
                BingSearchType.Web => WebSearchPath,
                BingSearchType.Video => VideoSearchPath,
                BingSearchType.News => NewsSearchPath,
                BingSearchType.Images => ImagesSearchPath,
                _ => throw new ArgumentOutOfRangeException(nameof(searchType), searchType, null)
            };
    }
}
