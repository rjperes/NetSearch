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

            return services;
        }
    }
}