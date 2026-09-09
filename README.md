# Search.NET
A .NET proxy for search engines with Google, Bing, and YouTube implementations.

## Usage

### Register a single implementation

```csharp
using Microsoft.Extensions.DependencyInjection;
using NetSearch;

var services = new ServiceCollection();

services.AddGoogleSearch(options =>
{
    options.SetChromeUserAgent();
    options.AcceptLanguages("en");
});

var provider = services.BuildServiceProvider();
var search = provider.GetRequiredService<ISearch>();
var result = await search.Search("dotnet");
```

### Register multiple implementations

```csharp
using Microsoft.Extensions.DependencyInjection;
using NetSearch;

var services = new ServiceCollection();

services.AddGoogleSearch(options =>
{
    options.SetChromeUserAgent();
    options.AcceptLanguages("en");
});

services.AddBingSearch(options =>
{
    options.SetEdgeUserAgent();
    options.AcceptLanguages("en");
});

services.AddYouTubeSearch(options =>
{
    options.SetChromeUserAgent();
    options.AcceptLanguages("en");
});

var provider = services.BuildServiceProvider();

var google = provider.GetRequiredKeyedService<ISearch>("Google");
var bing = provider.GetRequiredKeyedService<ISearch>("Bing");
var youtube = provider.GetRequiredKeyedService<ISearch>("YouTube");
```

### Query options

```csharp
using NetSearch;

var googleResult = await google.Search("dotnet", new GoogleQueryOptions
{
    Page = 0,
    Site = "https://learn.microsoft.com",
    SearchType = GoogleSearchType.Web
});

var bingResult = await bing.Search("dotnet", new BingQueryOptions
{
    Page = 0,
    Site = "https://learn.microsoft.com",
    SearchType = BingSearchType.News
});

var youtubeResult = await youtube.Search("dotnet", new YouTubeQueryOptions
{
    Site = "https://learn.microsoft.com",
    SearchType = YouTubeSearchType.Video
});
```
