using HtmlAgilityPack;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.Net.Http.Headers;
using System.Text;

namespace NetSearch
{
    public enum YouTubeSearchType
    {
        Video,
        Channel,
        Playlist
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

                var resultNodes = doc.DocumentNode.SelectNodes("//ytd-video-renderer | //ytd-channel-renderer | //ytd-playlist-renderer");
                if (resultNodes == null)
                {
                    return Task.FromResult(false);
                }

                foreach (var resultNode in resultNodes)
                {
                    var linkNode = resultNode.SelectSingleNode(".//a[@id='video-title' and @href] | .//a[@id='main-link' and @href] | .//a[@href][.//*[@id='text']]");
                    if (linkNode == null)
                    {
                        continue;
                    }

                    var title = HtmlEntity.DeEntitize(linkNode.GetAttributeValue("title", linkNode.InnerText) ?? string.Empty).Trim();
                    var url = linkNode.GetAttributeValue("href", null);
                    if (string.IsNullOrWhiteSpace(title) || string.IsNullOrWhiteSpace(url))
                    {
                        continue;
                    }

                    url = NormalizeUrl(url);
                    var content = HtmlEntity.DeEntitize(resultNode.SelectSingleNode(".//*[@id='description-text' or @id='description-snippet']")?.InnerText ?? string.Empty).Trim();
                    var image = resultNode.SelectSingleNode(".//img[@src]")?.GetAttributeValue("src", default(string));
                    var date = HtmlEntity.DeEntitize(resultNode.SelectSingleNode(".//span[contains(@class,'inline-metadata-item')] | .//*[@id='subscribers'] | .//*[@id='video-count']")?.InnerText ?? string.Empty).Trim();

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

            private static string NormalizeUrl(string url)
                => Uri.TryCreate(url, UriKind.Relative, out _) ? $"{YouTubeUrlPrefix}{url}" : url;
        }

        private readonly HttpClient _httpClient;
        private readonly ILogger<YouTubeSearch> _logger;
        private readonly List<IResultsParser> _parsers = [new YouTubeResultsParser()];
        private const string VideoSearchFilter = "EgIQAQ==";
        private const string ChannelSearchFilter = "EgIQAg==";
        private const string PlaylistSearchFilter = "EgIQAw==";

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

            if (options is YouTubeQueryOptions youtubeOptions && youtubeOptions.SearchType != null)
            {
                var searchType = youtubeOptions.SearchType.Value;
                _logger.LogDebug($"Setting search type to '{searchType}'");
                requestUrl.Append($"&sp={Uri.EscapeDataString(GetSearchFilter(searchType))}");
            }

            requestUrl.Insert(0, $"?search_query={Uri.EscapeDataString(queryText.ToString())}");

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

        private static string GetSearchFilter(YouTubeSearchType searchType)
            => searchType switch
            {
                YouTubeSearchType.Video => VideoSearchFilter,
                YouTubeSearchType.Channel => ChannelSearchFilter,
                YouTubeSearchType.Playlist => PlaylistSearchFilter,
                _ => throw new ArgumentOutOfRangeException(nameof(searchType), searchType, null)
            };
    }
}
