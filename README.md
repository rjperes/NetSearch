# Search.NET
A .NET proxy for search engines with Google and Bing implementations.

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

var provider = services.BuildServiceProvider();

var google = provider.GetRequiredKeyedService<ISearch>("Google");
var bing = provider.GetRequiredKeyedService<ISearch>("Bing");
```

### Query options

```csharp
using NetSearch;

var googleResult = await google.Search("dotnet", new GoogleQueryOptions
{
    Page = 0,
    Site = "learn.microsoft.com",
    SearchType = GoogleSearchType.Web
});

var bingResult = await bing.Search("dotnet", new BingQueryOptions
{
    Page = 0,
    Site = "learn.microsoft.com",
    SearchType = BingSearchType.News
});
```
