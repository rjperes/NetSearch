using Microsoft.Extensions.DependencyInjection;

namespace NetSearch
{
    public static class HttpClientBuilderExtensions
    {
        public static IHttpClientBuilder RegisterKeyedService(this IHttpClientBuilder builder)
        {
            var sd = builder.Services.Last(x => x.ServiceType == typeof(ISearch) && x.Lifetime == ServiceLifetime.Transient && x.ServiceKey == null);

            if (sd?.ImplementationFactory != null)
            {
                builder.Services.AddKeyedScoped(sd.ServiceType, builder.Name, (sp, name) => sd.ImplementationFactory(sp));
            }
            else if (sd?.ImplementationInstance != null)
            {
                builder.Services.AddKeyedScoped(sd.ServiceType, builder.Name, (sp, name) => sd.ImplementationInstance);
            }
            else if (sd?.ImplementationType != null)
            {
                builder.Services.Add(new ServiceDescriptor(sd.ServiceType, builder.Name, sd.ImplementationType, ServiceLifetime.Transient));
            }
            else
            {
                throw new InvalidOperationException("Invalid service registration.");
            }

            return builder;
        }
    }
}
