using Microsoft.Extensions.Options;
using Microsoft.Net.Http.Headers;
using Microsoft.VisualBasic;
using System.Net.WebSockets;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml;

namespace NetSearch
{
    public class GoogleSearch : ISearch
    {
        private class Google1ResultsParser : IResultsParser
        {
            private static Regex _matches = new Regex("<a href=\"\\/url\\?q=([^\"]+)\"[^\\/>]+>(.|\\n)*?<\\/a>", RegexOptions.Multiline);

            public bool TryParse(string response, List<SearchResult> results)
            {
                var matches = _matches.Matches(response);

                foreach (Match match in matches)
                {
                    var xml = new XmlDocument();
                    xml.LoadXml(match.Value);

                    var url = xml.SelectSingleNode("//a[@href]").Attributes[0].Value.Replace("/url?q=", string.Empty);
                    var title = xml.SelectSingleNode("//h3/div").InnerText;

                }


                return results.Any();
            }
        }

        private readonly HttpClient _httpClient;
        private static readonly IEnumerable<IResultsParser> _parsers = new[] { new Google1ResultsParser() };

        public GoogleSearch(HttpClient httpClient, IOptions<SearchOptions> options)
        {
            ArgumentNullException.ThrowIfNull(httpClient, nameof(httpClient));

            _httpClient = httpClient;

            if (options?.Value != null)
            {
                if (!string.IsNullOrWhiteSpace(options.Value.UserAgent))
                {
                    _httpClient.DefaultRequestHeaders.Add(HeaderNames.UserAgent, options.Value.UserAgent);
                }

                if (((bool?)options?.Value?.AcceptLanguages.Any()).GetValueOrDefault())
                {
                    _httpClient.DefaultRequestHeaders.Add(HeaderNames.AcceptLanguage, string.Join(",", options?.Value?.AcceptLanguages!));
                }
            }
        }

        public Task<List<SearchResult>> Search(string query, CancellationToken cancellationToken = default) => Search(query, new QueryOptions(), cancellationToken);

        public async Task<List<SearchResult>> Search(string query, QueryOptions options, CancellationToken cancellationToken = default)
        {
            var requestUrl = new StringBuilder($"?q={Uri.EscapeDataString(query)}");

            if (options.Page != null && options.Page.Value != 0)
            {
                requestUrl.Append($"&start={options.Page.Value * options.Size.GetValueOrDefault(10)}");
            }

            if (!string.IsNullOrWhiteSpace(options.Site))
            {
                requestUrl.Append($"&site={options.Site}");
            }

            var escapedRequestUrl = requestUrl.ToString();

            var response = await _httpClient.GetStringAsync(escapedRequestUrl, cancellationToken);

            var results = new List<SearchResult>();

            foreach (var parser in _parsers)
            {
                if (parser.TryParse(response, results))
                {
                    break;
                }
            }

            return results;
        }

        private List<SearchResult> ParseMatches(MatchCollection matches)
        {
            var results = new List<SearchResult>();

            foreach (var match in matches.OfType<Match>())
            {
                var doc = new XmlDocument();
                doc.LoadXml(match.Value);

                var url = doc.SelectSingleNode("a[href]");

                var result = new SearchResult
                {
                    Url = new(match.Groups[1].Value),
                    Title = "",
                    Content = ""
                };
            }

            return results;
        }
    }
}
