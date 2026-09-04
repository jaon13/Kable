namespace Microsoft.Extensions.DependencyInjection;

using System;
using Kable.Codecs;
using Kable.Engine;
using Kable.Extensions;
using Kable.Observability;

public static class KableServiceExtensions
{
    public static IServiceCollection AddKable(this IServiceCollection services)
    {
        services.AddSingleton<ICommObserver, CommObserver>();
        return services;
    }

    public static IServiceCollection AddKableSession<TMessage>(
        this IServiceCollection services,
        Action<KableClientBuilder<TMessage>, IServiceProvider> configure)
    {
        services.AddSingleton<IDeviceSession<TMessage>>(sp =>
        {
            var builder = new KableClientBuilder<TMessage>();
            var observer = sp.GetService<ICommObserver>();
            if (observer != null)
            {
                builder.UseObserver(observer);
            }

            configure(builder, sp);
            return builder.Build();
        });

        return services;
    }
}
