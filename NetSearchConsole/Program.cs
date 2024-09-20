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

            services.AddLogging(options =>
            {
                options.AddConsole();
            });

            services.AddGoogleSearch();

            var serviceProvider = services.BuildServiceProvider();

            var search = serviceProvider.GetRequiredService<ISearch>();
            var google = serviceProvider.GetRequiredKeyedService<ISearch>("Google");

            var results = await search.Search("ricardo peres");
        }
    }
}
