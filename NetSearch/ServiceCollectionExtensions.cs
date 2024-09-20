using Microsoft.Extensions.DependencyInjection;

namespace NetSearch
{
    public static class ServiceCollectionExtensions
    {
        public static IServiceCollection AddGoogleSearch(this IServiceCollection services)
        {
            services.AddHttpClient<ISearch, GoogleSearch>("Google", static client =>
            {
                client.BaseAddress = new("https://google.com/search");
            }).RegisterKeyedService().AddDefaultLogger();

            return services;
        }
    }
}
