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
