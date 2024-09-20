using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using NetSearch;

namespace NetSearchConsole
{
    internal class Program
    {
        static async Task Main()
        {
            var services = new ServiceCollection();

            services.AddOptions();

            services.AddLogging(static options =>
            {
                options.AddConsole();
            });

            services.Configure<SearchOptions>(static options =>
            {
                options.UserAgent = "Mozilla/5.0 (Windows) NetSearch NetSearch/1";
            });

            services.AddGoogleSearch();

            var serviceProvider = services.BuildServiceProvider();

            var search = serviceProvider.GetRequiredService<ISearch>();
            var google = serviceProvider.GetRequiredKeyedService<ISearch>("Google");

            var results = await search.Search("ricardo peres");
        }
    }
}
