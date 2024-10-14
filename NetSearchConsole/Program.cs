using Microsoft.Extensions.Configuration;
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

            var configuration = new ConfigurationBuilder()
                .AddJsonFile("appsettings.json", optional: true, reloadOnChange: true)
                .Build();

            services.AddOptions();

            services.AddLogging(options =>
            {
                options.AddConfiguration(configuration.GetSection("Logging"));
                options.AddConsole();
            });

            services.AddGoogleSearch(options =>
            {
                options.SetChromeUserAgent();
                options.AcceptLanguages("en");
            });

            var serviceProvider = services.BuildServiceProvider();

            var search = serviceProvider.GetRequiredService<ISearch>();
            var google = serviceProvider.GetRequiredKeyedService<ISearch>("Google");

            var result = await search.Search("ricardo peres");
        }
    }
}
