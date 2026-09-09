using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace NetSearch
{
    public static class ServiceCollectionExtensions
    {
        public static IServiceCollection AddGoogleSearch(this IServiceCollection services, SearchOptions options)
        {
            ArgumentNullException.ThrowIfNull(services, nameof(services));
            ArgumentNullException.ThrowIfNull(options, nameof(options));

            services.AddSingleton(Options.Create(options));

            return AddGoogleSearch(services);
        }

        public static IServiceCollection AddGoogleSearch(this IServiceCollection services, Action<SearchOptions> options)
        {
            ArgumentNullException.ThrowIfNull(services, nameof(services));
            ArgumentNullException.ThrowIfNull(options, nameof(options));

            services.Configure(options);

            return AddGoogleSearch(services);
        }

        public static IServiceCollection AddGoogleSearch(this IServiceCollection services)
        {
            ArgumentNullException.ThrowIfNull(services, nameof(services));

            services.AddHttpClient<ISearch, GoogleSearch>("Google", static client =>
            {
                client.BaseAddress = new("https://google.com/search");
            }).RegisterKeyedService().AddDefaultLogger();

            services.AddKeyedTransient<ISearch>("Google", (sp, key) =>
            {
                return ActivatorUtilities.CreateInstance<GoogleSearch>(sp);
            });

            return services;
        }

        public static IServiceCollection AddBingSearch(this IServiceCollection services, SearchOptions options)
        {
            ArgumentNullException.ThrowIfNull(services, nameof(services));
            ArgumentNullException.ThrowIfNull(options, nameof(options));

            return AddBingSearch(services, Options.Create(options));
        }

        public static IServiceCollection AddBingSearch(this IServiceCollection services, Action<SearchOptions> options)
        {
            ArgumentNullException.ThrowIfNull(services, nameof(services));
            ArgumentNullException.ThrowIfNull(options, nameof(options));

            var configuredOptions = new SearchOptions();
            options(configuredOptions);

            return AddBingSearch(services, Options.Create(configuredOptions));
        }

        public static IServiceCollection AddBingSearch(this IServiceCollection services) => AddBingSearch(services, Options.Create(new SearchOptions()));

        private static IServiceCollection AddBingSearch(IServiceCollection services, IOptions<SearchOptions> options)
        {
            ArgumentNullException.ThrowIfNull(services, nameof(services));
            ArgumentNullException.ThrowIfNull(options, nameof(options));

            services.AddHttpClient<ISearch, BingSearch>("Bing", static client =>
            {
                client.BaseAddress = new("https://bing.com/search");
            }).RegisterKeyedService().AddDefaultLogger();

            services.AddKeyedTransient<ISearch>("Bing", (sp, key) =>
            {
                var httpClient = sp.GetRequiredService<IHttpClientFactory>().CreateClient("Bing");
                return ActivatorUtilities.CreateInstance<BingSearch>(sp, httpClient, options);
            });

            return services;
        }
    }
}