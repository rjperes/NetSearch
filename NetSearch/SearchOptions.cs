namespace NetSearch
{
    public class SearchOptions
    {
        public string? UserAgent { get; set; }
        public List<string> AcceptLanguages { get; } = new List<string>();
    }

    public static class SearchOptionsExtensions
    {
        public static SearchOptions ClearLanguages(this SearchOptions options)
        {
            ArgumentNullException.ThrowIfNull(options, nameof(options));
            options.AcceptLanguages.Clear();
            return options;
        }

        public static SearchOptions AcceptLanguages(this SearchOptions options, params string[] languages)
        {
            ArgumentNullException.ThrowIfNull(options, nameof(options));
            if (languages == null || languages.Length == 0)
            {
                return options.ClearLanguages();
            }
            options.AcceptLanguages.AddRange(languages);
            return options;
        }

        public static SearchOptions SetChromeUserAgent(this SearchOptions options)
        {
            ArgumentNullException.ThrowIfNull(options, nameof(options));
            options.UserAgent = "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/129.0.0.0 Safari/537.36";
            return options;
        }

        public static SearchOptions SetEdgeUserAgent(this SearchOptions options)
        {
            ArgumentNullException.ThrowIfNull(options, nameof(options));
            options.UserAgent = "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/129.0.0.0 Safari/537.36 Edg/128.0.2739.90";
            return options;
        }

        public static SearchOptions SetFirefoxUserAgent(this SearchOptions options)
        {
            ArgumentNullException.ThrowIfNull(options, nameof(options));
            options.UserAgent = "Mozilla/5.0 (Windows NT 10.0; Win64; x64; rv:130.0) Gecko/20100101 Firefox/130.0";
            return options;
        }

        public static SearchOptions SetSafariUserAgent(this SearchOptions options)
        {
            ArgumentNullException.ThrowIfNull(options, nameof(options));
            options.UserAgent = "Mozilla/5.0 (Macintosh; Intel Mac OS X 14_7) AppleWebKit/605.1.15 (KHTML, like Gecko) Version/18.0 Safari/605.1.15";
            return options;
        }
    }
}
