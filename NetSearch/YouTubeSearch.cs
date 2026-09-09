using HtmlAgilityPack;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.Net.Http.Headers;
using System.Text;

namespace NetSearch
{
    public enum YouTubeSearchType
    {
        Video
    }

    public class YouTubeQueryOptions : QueryOptions
    {
        public YouTubeSearchType? SearchType { get; init; }
    }

    public class YouTubeSearch : ISearch
    {
        private class YouTubeResultsParser : IResultsParser
        {
            private const string YouTubeUrlPrefix = "https://www.youtube.com";

            public Task<bool> TryParse(string response, List<SearchHit> results)
            {
                var doc = new HtmlDocument();
                doc.LoadHtml(response);

                var videoLinks = doc.DocumentNode.SelectNodes("//a[@id='video-title' and @href]");
                if (videoLinks == null)
                {
                    return Task.FromResult(false);
                }

                foreach (var videoLink in videoLinks)
                {
                    var title = HtmlEntity.DeEntitize(videoLink.GetAttributeValue("title", videoLink.InnerText) ?? string.Empty).Trim();
                    var url = videoLink.GetAttributeValue("href", null);
                    if (string.IsNullOrWhiteSpace(title) || string.IsNullOrWhiteSpace(url))
                    {
                        continue;
                    }

                    if (Uri.TryCreate(url, UriKind.Relative, out _))
                    {
                        url = $"{YouTubeUrlPrefix}{url}";
                    }

                    var resultContainer = videoLink.SelectSingleNode("ancestor::ytd-video-renderer[1]") ?? videoLink.ParentNode;
                    var content = HtmlEntity.DeEntitize(resultContainer?.SelectSingleNode(".//*[@id='description-text']")?.InnerText ?? string.Empty).Trim();
                    var image = resultContainer?.SelectSingleNode(".//img[@src]")?.GetAttributeValue("src", default(string));
                    var date = HtmlEntity.DeEntitize(resultContainer?.SelectSingleNode(".//span[contains(@class,'inline-metadata-item')]")?.InnerText ?? string.Empty).Trim();

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
        private readonly ILogger<YouTubeSearch> _logger;
        private readonly List<IResultsParser> _parsers = [new YouTubeResultsParser()];

        public YouTubeSearch(HttpClient httpClient, IOptions<SearchOptions> options, ILogger<YouTubeSearch> logger, IEnumerable<IResultsParser> parsers)
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

            if (options is YouTubeQueryOptions youtubeOptions && youtubeOptions.SearchType != null)
            {
                var searchType = youtubeOptions.SearchType.Value.ToString().ToLowerInvariant();
                _logger.LogDebug($"Setting search type to '{searchType}'");
                queryText.Append($" {searchType}");
            }

            var requestUrl = new StringBuilder($"?search_query={Uri.EscapeDataString(queryText.ToString())}");

            if (options.Size != null)
            {
                _logger.LogWarning("Size setting not supported by YouTube");
            }

            if (options.Page != null)
            {
                _logger.LogWarning("Page setting not supported by YouTube");
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
    }
}
