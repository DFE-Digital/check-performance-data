using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace DfE.CheckPerformanceData.Infrastructure.Authentication;

public static class AuthenticationTicketStoreExtensions
{
    // Registers the server-side ticket store and attaches it to the cookie scheme.
    //
    // Attached through the options pipeline rather than assigned inline in AddCookie, because the
    // store needs IDistributedCache and the container does not exist yet at the point AddCookie's
    // callback runs. Configuring named CookieAuthenticationOptions with the dependency resolves it
    // when the options are first materialised, by which time the cache is registered — and it
    // leaves the ordering of AddCpdSessionStore and AddDfeSignInAuthentication in Program.cs free.
    public static IServiceCollection AddCpdAuthenticationTicketStore(this IServiceCollection services)
    {
        services.TryAddSingleton<DistributedCacheTicketStore>();
        services.AddOptions<AuthenticationTicketStoreOptions>();

        services.AddOptions<CookieAuthenticationOptions>(
                CookieAuthenticationDefaults.AuthenticationScheme)
            .Configure<DistributedCacheTicketStore>((cookie, store) => cookie.SessionStore = store);

        return services;
    }
}
