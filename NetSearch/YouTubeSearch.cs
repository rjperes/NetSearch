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
            private static readonly string[] InitialDataMarkers = ["var ytInitialData = ", "window['ytInitialData'] = ", "window[\"ytInitialData\"] = "];

            public Task<bool> TryParse(string response, List<SearchHit> results)
            {
                if (TryParseInitialData(response, results))
                {
                    return Task.FromResult(results.Any());
                }

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
                    var date = HtmlEntity.DeEntitize(resultNode.SelectSingleNode(".//span[contains(@class,'inline-metadata-item')] | .//*[@id='video-count']")?.InnerText ?? string.Empty).Trim();

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

            private static bool TryParseInitialData(string response, List<SearchHit> results)
            {
                var json = ExtractInitialDataJson(response);
                if (json == null)
                {
                    return false;
                }

                using var document = JsonDocument.Parse(json);

                foreach (var renderer in EnumerateSearchResultRenderers(document.RootElement))
                {
                    if (TryParseRenderer(renderer, out var result))
                    {
                        results.Add(result);
                    }
                }

                return results.Any();
            }

            private static string? ExtractInitialDataJson(string response)
            {
                foreach (var marker in InitialDataMarkers)
                {
                    var markerIndex = response.IndexOf(marker, StringComparison.Ordinal);
                    if (markerIndex < 0)
                    {
                        continue;
                    }

                    var jsonStart = response.IndexOf('{', markerIndex + marker.Length);
                    if (jsonStart < 0)
                    {
                        continue;
                    }

                    var depth = 0;
                    var inString = false;
                    var escaped = false;

                    for (var index = jsonStart; index < response.Length; index++)
                    {
                        var current = response[index];

                        if (escaped)
                        {
                            escaped = false;
                            continue;
                        }

                        if (current == '\\')
                        {
                            escaped = true;
                            continue;
                        }

                        if (current == '"')
                        {
                            inString = !inString;
                            continue;
                        }

                        if (inString)
                        {
                            continue;
                        }

                        if (current == '{')
                        {
                            depth++;
                        }
                        else if (current == '}')
                        {
                            depth--;

                            if (depth == 0)
                            {
                                return response[jsonStart..(index + 1)];
                            }
                        }
                    }
                }

                return null;
            }

            private static IEnumerable<JsonElement> EnumerateSearchResultRenderers(JsonElement element)
            {
                switch (element.ValueKind)
                {
                    case JsonValueKind.Object:
                        foreach (var property in element.EnumerateObject())
                        {
                            if (property.Name is "videoRenderer" or "channelRenderer" or "playlistRenderer")
                            {
                                yield return property.Value;
                            }

                            foreach (var nestedElement in EnumerateSearchResultRenderers(property.Value))
                            {
                                yield return nestedElement;
                            }
                        }
                        break;

                    case JsonValueKind.Array:
                        foreach (var item in element.EnumerateArray())
                        {
                            foreach (var nestedElement in EnumerateSearchResultRenderers(item))
                            {
                                yield return nestedElement;
                            }
                        }
                        break;
                }
            }

            private static bool TryParseRenderer(JsonElement renderer, out SearchHit result)
            {
                result = default!;

                var title = GetText(renderer, "title");
                var url = GetNavigationUrl(renderer);
                if (string.IsNullOrWhiteSpace(title) || string.IsNullOrWhiteSpace(url))
                {
                    return false;
                }

                result = new SearchHit
                {
                    Title = title,
                    Url = NormalizeUrl(url),
                    Content = GetContent(renderer),
                    Image = GetThumbnailUrl(renderer),
                    Date = GetDate(renderer)
                };

                return true;
            }

            private static string GetContent(JsonElement renderer)
            {
                foreach (var propertyName in new[] { "descriptionSnippet", "descriptionText", "headline", "shortBylineText", "longBylineText", "videoCountText", "videoCountShortText" })
                {
                    var value = GetText(renderer, propertyName);
                    if (!string.IsNullOrWhiteSpace(value))
                    {
                        return value;
                    }
                }

                return GetNestedText(renderer, "detailedMetadataSnippets", "snippetText");
            }

            private static string GetDate(JsonElement renderer)
            {
                foreach (var propertyName in new[] { "publishedTimeText", "videoCountText", "subscriberCountText" })
                {
                    var value = GetText(renderer, propertyName);
                    if (!string.IsNullOrWhiteSpace(value))
                    {
                        return value;
                    }
                }

                return string.Empty;
            }

            private static string? GetNavigationUrl(JsonElement renderer)
            {
                if (TryGetProperty(renderer, "navigationEndpoint", out var navigationEndpoint))
                {
                    var url = GetNavigationEndpointUrl(navigationEndpoint);
                    if (!string.IsNullOrWhiteSpace(url))
                    {
                        return url;
                    }
                }

                if (TryGetProperty(renderer, "title", out var titleElement)
                    && titleElement.ValueKind == JsonValueKind.Object
                    && titleElement.TryGetProperty("runs", out var runs)
                    && runs.ValueKind == JsonValueKind.Array)
                {
                    foreach (var run in runs.EnumerateArray())
                    {
                        if (TryGetProperty(run, "navigationEndpoint", out navigationEndpoint))
                        {
                            var url = GetNavigationEndpointUrl(navigationEndpoint);
                            if (!string.IsNullOrWhiteSpace(url))
                            {
                                return url;
                            }
                        }
                    }
                }

                return null;
            }

            private static string? GetNavigationEndpointUrl(JsonElement navigationEndpoint)
            {
                if (TryGetProperty(navigationEndpoint, "commandMetadata", out var commandMetadata)
                    && TryGetProperty(commandMetadata, "webCommandMetadata", out var webCommandMetadata)
                    && TryGetString(webCommandMetadata, "url", out var url)
                    && !string.IsNullOrWhiteSpace(url))
                {
                    return url;
                }

                return null;
            }

            private static string? GetThumbnailUrl(JsonElement renderer)
            {
                foreach (var propertyName in new[] { "thumbnail", "thumbnailRenderer" })
                {
                    if (TryGetProperty(renderer, propertyName, out var thumbnailElement))
                    {
                        var url = GetThumbnailUrlFromElement(thumbnailElement);
                        if (!string.IsNullOrWhiteSpace(url))
                        {
                            return url;
                        }
                    }
                }

                if (TryGetProperty(renderer, "channelThumbnailSupportedRenderers", out var channelThumbnailSupportedRenderers))
                {
                    var url = GetThumbnailUrlFromElement(channelThumbnailSupportedRenderers);
                    if (!string.IsNullOrWhiteSpace(url))
                    {
                        return url;
                    }
                }

                return null;
            }

            private static string? GetThumbnailUrlFromElement(JsonElement element)
            {
                if (element.ValueKind == JsonValueKind.Object)
                {
                    if (TryGetProperty(element, "thumbnails", out var thumbnails)
                        && thumbnails.ValueKind == JsonValueKind.Array)
                    {
                        string? url = null;

                        foreach (var thumbnail in thumbnails.EnumerateArray())
                        {
                            if (TryGetString(thumbnail, "url", out var thumbnailUrl) && !string.IsNullOrWhiteSpace(thumbnailUrl))
                            {
                                url = thumbnailUrl;
                            }
                        }

                        if (!string.IsNullOrWhiteSpace(url))
                        {
                            return url;
                        }
                    }

                    foreach (var property in element.EnumerateObject())
                    {
                        var url = GetThumbnailUrlFromElement(property.Value);
                        if (!string.IsNullOrWhiteSpace(url))
                        {
                            return url;
                        }
                    }
                }
                else if (element.ValueKind == JsonValueKind.Array)
                {
                    foreach (var item in element.EnumerateArray())
                    {
                        var url = GetThumbnailUrlFromElement(item);
                        if (!string.IsNullOrWhiteSpace(url))
                        {
                            return url;
                        }
                    }
                }

                return null;
            }

            private static string GetNestedText(JsonElement renderer, string propertyName, string nestedPropertyName)
            {
                if (!TryGetProperty(renderer, propertyName, out var element))
                {
                    return string.Empty;
                }

                if (element.ValueKind == JsonValueKind.Array)
                {
                    foreach (var item in element.EnumerateArray())
                    {
                        var value = GetText(item, nestedPropertyName);
                        if (!string.IsNullOrWhiteSpace(value))
                        {
                            return value;
                        }
                    }
                }

                return string.Empty;
            }

            private static string GetText(JsonElement element, string propertyName)
            {
                if (!TryGetProperty(element, propertyName, out var textElement))
                {
                    return string.Empty;
                }

                if (TryGetString(textElement, "simpleText", out var simpleText) && !string.IsNullOrWhiteSpace(simpleText))
                {
                    return simpleText;
                }

                if (textElement.ValueKind == JsonValueKind.Array)
                {
                    return string.Concat(textElement.EnumerateArray().Select(GetRunText)).Trim();
                }

                if (textElement.ValueKind == JsonValueKind.Object
                    && textElement.TryGetProperty("runs", out var runs)
                    && runs.ValueKind == JsonValueKind.Array)
                {
                    return string.Concat(runs.EnumerateArray().Select(GetRunText)).Trim();
                }

                return textElement.ValueKind == JsonValueKind.String ? textElement.GetString() ?? string.Empty : string.Empty;
            }

            private static string GetRunText(JsonElement run)
                => TryGetString(run, "text", out var text) ? text : string.Empty;

            private static bool TryGetProperty(JsonElement element, string propertyName, out JsonElement value)
            {
                if (element.ValueKind == JsonValueKind.Object && element.TryGetProperty(propertyName, out value))
                {
                    return true;
                }

                value = default;
                return false;
            }

            private static bool TryGetString(JsonElement element, string propertyName, out string value)
            {
                if (TryGetProperty(element, propertyName, out var valueElement) && valueElement.ValueKind == JsonValueKind.String)
                {
                    value = valueElement.GetString() ?? string.Empty;
                    return true;
                }

                value = string.Empty;
                return false;
            }

            private static string NormalizeUrl(string url)
                => Uri.TryCreate(url, UriKind.Relative, out _) ? $"{YouTubeUrlPrefix}{url}" : url;
        }

        private readonly HttpClient _httpClient;
        private readonly ILogger<YouTubeSearch> _logger;
        private readonly List<IResultsParser> _parsers = [new YouTubeResultsParser()];
        private const string VideoSearchFilter = "EgIQAQ%3D%3D";
        private const string ChannelSearchFilter = "EgIQAg%3D%3D";
        private const string PlaylistSearchFilter = "EgIQAw%3D%3D";

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
                requestUrl.Append($"&sp={GetSearchFilter(searchType)}");
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
