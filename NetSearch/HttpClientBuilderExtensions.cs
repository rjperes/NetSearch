using Microsoft.Extensions.DependencyInjection;

namespace NetSearch
{
    public static class HttpClientBuilderExtensions
    {
        public static IHttpClientBuilder RegisterKeyedService(this IHttpClientBuilder builder)
        {
            ArgumentNullException.ThrowIfNull(builder, nameof(builder));
            ArgumentException.ThrowIfNullOrWhiteSpace(builder.Name, nameof(builder.Name));

            builder.Services.AddKeyedTransient(builder.Name, (sp, key) => sp.GetRequiredService<IHttpClientFactory>().CreateClient((string)key));

            return builder;
        }
    }
}
