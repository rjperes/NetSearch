using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NetSearch;
using System.Net;
using System.Net.Http;

namespace NetSearch.Tests;

public class SearchTests
{
    [Fact]
    public async Task GoogleSearch_ParsesResults_WithHtmlAgilityPack()
    {
        const string html = """
            <html><body>
              <div id="search">
                <div jscontroller="abc">
                  <h3>Result title</h3>
                  <a jsname="N" href="https://example.com/page"></a>
                  <img src="https://example.com/image.png" />
                  <div data-snf="1" data-sncf="1">
                    <div><span>ignored</span><span>2024-08-01</span><span>Result content</span></div>
                  </div>
                </div>
              </div>
            </body></html>
            """;

        var handler = new StubHttpMessageHandler(html);
        var client = new HttpClient(handler) { BaseAddress = new Uri("https://google.com/search") };
        var search = new GoogleSearch(client, Options.Create(new SearchOptions()), NullLogger<GoogleSearch>.Instance, []);

        var result = await search.Search("any");

        Assert.Single(result.Hits);
        Assert.Equal("Result title", result.Hits[0].Title);
        Assert.Equal("https://example.com/page", result.Hits[0].Url);
        Assert.Equal("Result content", result.Hits[0].Content);
        Assert.Equal("2024-08-01", result.Hits[0].Date);
        Assert.Equal("https://example.com/image.png", result.Hits[0].Image);
    }

    [Fact]
    public async Task BingSearch_ParsesResults_AndBuildsExpectedQuery()
    {
        const string html = """
            <html><body>
              <ol id="b_results">
                <li class="b_algo">
                  <h2><a href="https://bing.example.com/page">Bing title</a></h2>
                  <div class="b_caption"><p>Bing content</p></div>
                  <span class="news_dt">2024-09-01</span>
                  <img src="https://bing.example.com/image.png" />
                </li>
              </ol>
            </body></html>
            """;

        var handler = new StubHttpMessageHandler(html);
        var client = new HttpClient(handler) { BaseAddress = new Uri("https://bing.com/search") };
        var search = new BingSearch(client, Options.Create(new SearchOptions()), NullLogger<BingSearch>.Instance, []);
        var options = new BingQueryOptions
        {
            Page = 1,
            Site = "https://contoso.com",
            SearchType = BingSearchType.News
        };

        var result = await search.Search("dotnet", options);

        Assert.Single(result.Hits);
        Assert.Equal("Bing title", result.Hits[0].Title);
        Assert.Equal("https://bing.example.com/page", result.Hits[0].Url);
        Assert.Equal("Bing content", result.Hits[0].Content);
        Assert.Equal("2024-09-01", result.Hits[0].Date);
        Assert.Equal("https://bing.example.com/image.png", result.Hits[0].Image);
        Assert.NotNull(handler.LastRequestUri);
        Assert.Equal("?q=dotnet site:contoso.com news&first=11", Uri.UnescapeDataString(handler.LastRequestUri!.Query));
    }

    [Fact]
    public async Task YouTubeSearch_ParsesVideoResults_FromHtml_AndBuildsExpectedQuery()
    {
        const string html = """
            <html><body>
              <ytd-video-renderer class="style-scope ytd-item-section-renderer">
                <a id="video-title" href="/watch?v=123" title="YouTube title"></a>
                <img src="https://img.youtube.com/vi/123/default.jpg" />
                <span class="inline-metadata-item style-scope ytd-video-meta-block">1 day ago</span>
                <div id="description-text">YouTube content</div>
              </ytd-video-renderer>
            </body></html>
            """;

        var handler = new StubHttpMessageHandler(html);
        var client = new HttpClient(handler) { BaseAddress = new Uri("https://www.youtube.com/results") };
        var search = new YouTubeSearch(client, Options.Create(new SearchOptions()), NullLogger<YouTubeSearch>.Instance, []);
        var options = new YouTubeQueryOptions
        {
            Site = "https://contoso.com",
            SearchType = YouTubeSearchType.Video
        };

        var result = await search.Search("dotnet", options);

        Assert.Single(result.Hits);
        Assert.Equal("YouTube title", result.Hits[0].Title);
        Assert.Equal("https://www.youtube.com/watch?v=123", result.Hits[0].Url);
        Assert.Equal("YouTube content", result.Hits[0].Content);
        Assert.Equal("1 day ago", result.Hits[0].Date);
        Assert.Equal("https://img.youtube.com/vi/123/default.jpg", result.Hits[0].Image);
        Assert.NotNull(handler.LastRequestUri);
        Assert.Equal("?search_query=dotnet site:contoso.com&sp=EgIQAQ==", Uri.UnescapeDataString(handler.LastRequestUri!.Query));
    }

    [Fact]
    public async Task YouTubeSearch_ParsesChannelResults_FromHtml()
    {
        const string html = """
            <html><body>
              <ytd-channel-renderer>
                <a id="main-link" href="/@dotnet" title="DotNet"></a>
                <img src="https://example.com/channel.png" />
                <div id="description-snippet">Official channel</div>
                <span id="subscribers">1M subscribers</span>
              </ytd-channel-renderer>
            </body></html>
            """;

        var handler = new StubHttpMessageHandler(html);
        var client = new HttpClient(handler) { BaseAddress = new Uri("https://www.youtube.com/results") };
        var search = new YouTubeSearch(client, Options.Create(new SearchOptions()), NullLogger<YouTubeSearch>.Instance, []);

        var result = await search.Search("dotnet", new YouTubeQueryOptions { SearchType = YouTubeSearchType.Channel });

        Assert.Single(result.Hits);
        Assert.Equal("DotNet", result.Hits[0].Title);
        Assert.Equal("https://www.youtube.com/@dotnet", result.Hits[0].Url);
        Assert.Equal("Official channel", result.Hits[0].Content);
        Assert.Equal("1M subscribers", result.Hits[0].Date);
        Assert.Equal("https://example.com/channel.png", result.Hits[0].Image);
        Assert.NotNull(handler.LastRequestUri);
        Assert.Equal("?search_query=dotnet&sp=EgIQAg==", Uri.UnescapeDataString(handler.LastRequestUri!.Query));
    }

    [Fact]
    public async Task YouTubeSearch_ParsesPlaylistResults_FromHtml()
    {
        const string html = """
            <html><body>
              <ytd-playlist-renderer>
                <a href="/playlist?list=PL123"><span id="text">NetSearch playlist</span></a>
                <img src="https://example.com/playlist.png" />
                <div id="description-snippet">12 videos</div>
                <div id="video-count">12 videos</div>
              </ytd-playlist-renderer>
            </body></html>
            """;

        var handler = new StubHttpMessageHandler(html);
        var client = new HttpClient(handler) { BaseAddress = new Uri("https://www.youtube.com/results") };
        var search = new YouTubeSearch(client, Options.Create(new SearchOptions()), NullLogger<YouTubeSearch>.Instance, []);

        var result = await search.Search("dotnet", new YouTubeQueryOptions { SearchType = YouTubeSearchType.Playlist });

        Assert.Single(result.Hits);
        Assert.Equal("NetSearch playlist", result.Hits[0].Title);
        Assert.Equal("https://www.youtube.com/playlist?list=PL123", result.Hits[0].Url);
        Assert.Equal("12 videos", result.Hits[0].Content);
        Assert.Equal("12 videos", result.Hits[0].Date);
        Assert.Equal("https://example.com/playlist.png", result.Hits[0].Image);
        Assert.NotNull(handler.LastRequestUri);
        Assert.Equal("?search_query=dotnet&sp=EgIQAw==", Uri.UnescapeDataString(handler.LastRequestUri!.Query));
    }

    private sealed class StubHttpMessageHandler(string payload) : HttpMessageHandler
    {
        public Uri? LastRequestUri { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            LastRequestUri = request.RequestUri;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(payload)
            });
        }
    }
}
