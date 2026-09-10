using HtmlAgilityPack;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.Net.Http.Headers;
using System.Text;
using System.Text.Json;

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
            private const string InitialDataMarker = "ytInitialData";

            public Task<bool> TryParse(string response, List<SearchHit> results)
            {
                var doc = new HtmlDocument();
                doc.LoadHtml(response);

                var resultNodes = doc.DocumentNode.SelectNodes("//ytd-video-renderer | //ytd-channel-renderer | //ytd-playlist-renderer");
                if (resultNodes != null)
                {
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
                }

                if (!results.Any())
                {
                    TryParseInitialData(response, results);
                }

                return Task.FromResult(results.Any());
            }

            private static void TryParseInitialData(string response, List<SearchHit> results)
            {
                if (!TryExtractInitialDataJson(response, out var initialDataJson))
                {
                    return;
                }

                using var document = JsonDocument.Parse(initialDataJson);
                TraverseNode(document.RootElement, results);
            }

            private static bool TryExtractInitialDataJson(string response, out string initialDataJson)
            {
                initialDataJson = string.Empty;

                var markerIndex = response.IndexOf(InitialDataMarker, StringComparison.Ordinal);
                if (markerIndex < 0)
                {
                    return false;
                }

                var assignmentIndex = response.IndexOf('=', markerIndex);
                if (assignmentIndex < 0)
                {
                    return false;
                }

                var startIndex = response.IndexOf('{', assignmentIndex);
                if (startIndex < 0)
                {
                    return false;
                }

                var depth = 0;
                var inString = false;
                var escaped = false;

                for (var i = startIndex; i < response.Length; i++)
                {
                    var currentChar = response[i];

                    if (inString)
                    {
                        if (escaped)
                        {
                            escaped = false;
                            continue;
                        }

                        if (currentChar == '\\')
                        {
                            escaped = true;
                            continue;
                        }

                        if (currentChar == '"')
                        {
                            inString = false;
                        }

                        continue;
                    }

                    if (currentChar == '"')
                    {
                        inString = true;
                        continue;
                    }

                    if (currentChar == '{')
                    {
                        depth++;
                        continue;
                    }

                    if (currentChar == '}')
                    {
                        depth--;
                        if (depth == 0)
                        {
                            initialDataJson = response[startIndex..(i + 1)];
                            return true;
                        }
                    }
                }

                return false;
            }

            private static void TraverseNode(JsonElement node, List<SearchHit> results)
            {
                switch (node.ValueKind)
                {
                    case JsonValueKind.Object:
                    {
                        foreach (var property in node.EnumerateObject())
                        {
                            if (property.NameEquals("videoRenderer"))
                            {
                                TryAddHit(property.Value, results, GetVideoDate);
                                continue;
                            }

                            if (property.NameEquals("channelRenderer"))
                            {
                                TryAddHit(property.Value, results, static renderer => GetText(renderer, "subscriberCountText"));
                                continue;
                            }

                            if (property.NameEquals("playlistRenderer"))
                            {
                                TryAddHit(property.Value, results, static renderer => GetText(renderer, "videoCountText"));
                                continue;
                            }

                            TraverseNode(property.Value, results);
                        }

                        break;
                    }
                    case JsonValueKind.Array:
                        foreach (var child in node.EnumerateArray())
                        {
                            TraverseNode(child, results);
                        }

                        break;
                }
            }

            private static void TryAddHit(JsonElement renderer, List<SearchHit> results, Func<JsonElement, string> dateResolver)
            {
                var title = GetText(renderer, "title");
                var url = NormalizeUrl(GetNavigationUrl(renderer));

                if (string.IsNullOrWhiteSpace(title) || string.IsNullOrWhiteSpace(url))
                {
                    return;
                }

                results.Add(new SearchHit
                {
                    Title = title,
                    Url = url,
                    Content = GetDescription(renderer),
                    Date = dateResolver(renderer),
                    Image = GetImageUrl(renderer)
                });
            }

            private static string GetVideoDate(JsonElement renderer)
            {
                var publishedTime = GetText(renderer, "publishedTimeText");
                return !string.IsNullOrWhiteSpace(publishedTime) ? publishedTime : GetText(renderer, "videoCountText");
            }

            private static string GetDescription(JsonElement renderer)
            {
                if (renderer.TryGetProperty("descriptionSnippet", out var descriptionSnippet))
                {
                    return ExtractText(descriptionSnippet);
                }

                if (renderer.TryGetProperty("detailedMetadataSnippets", out var detailedMetadataSnippets) &&
                    detailedMetadataSnippets.ValueKind == JsonValueKind.Array &&
                    detailedMetadataSnippets.GetArrayLength() > 0)
                {
                    var snippet = detailedMetadataSnippets[0];
                    if (snippet.TryGetProperty("snippetText", out var snippetText))
                    {
                        return ExtractText(snippetText);
                    }
                }

                return string.Empty;
            }

            private static string GetImageUrl(JsonElement renderer)
            {
                if (!renderer.TryGetProperty("thumbnail", out var thumbnail) ||
                    !thumbnail.TryGetProperty("thumbnails", out var thumbnails) ||
                    thumbnails.ValueKind != JsonValueKind.Array ||
                    thumbnails.GetArrayLength() == 0)
                {
                    return string.Empty;
                }

                var lastThumbnail = thumbnails[thumbnails.GetArrayLength() - 1];
                return lastThumbnail.TryGetProperty("url", out var url) ? url.GetString() ?? string.Empty : string.Empty;
            }

            private static string GetNavigationUrl(JsonElement renderer)
            {
                if (!renderer.TryGetProperty("navigationEndpoint", out var navigationEndpoint) ||
                    !navigationEndpoint.TryGetProperty("commandMetadata", out var commandMetadata) ||
                    !commandMetadata.TryGetProperty("webCommandMetadata", out var webCommandMetadata) ||
                    !webCommandMetadata.TryGetProperty("url", out var url))
                {
                    return string.Empty;
                }

                return url.GetString() ?? string.Empty;
            }

            private static string GetText(JsonElement node, string propertyName)
                => node.TryGetProperty(propertyName, out var property) ? ExtractText(property) : string.Empty;

            private static string ExtractText(JsonElement node)
            {
                if (node.ValueKind == JsonValueKind.String)
                {
                    return node.GetString() ?? string.Empty;
                }

                if (node.TryGetProperty("simpleText", out var simpleText))
                {
                    return simpleText.GetString() ?? string.Empty;
                }

                if (node.TryGetProperty("runs", out var runs) && runs.ValueKind == JsonValueKind.Array)
                {
                    var values = runs
                        .EnumerateArray()
                        .Where(static run => run.TryGetProperty("text", out _))
                        .Select(static run => run.GetProperty("text").GetString())
                        .Where(static value => !string.IsNullOrWhiteSpace(value));

                    return string.Join(string.Empty, values!);
                }

                return string.Empty;
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
