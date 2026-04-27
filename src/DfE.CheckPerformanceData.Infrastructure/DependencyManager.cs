using DfE.CheckPerformanceData.Application.ClaimsEnrichment;
using DfE.CheckPerformanceData.Application.DfESignInApiClient;
using DfE.CheckPerformanceData.Application.ZendeskClient;
using DfE.CheckPerformanceData.Infrastructure.DfeSignInApiClient;
using DfE.CheckPerformanceData.Infrastructure.ZendeskClient;
using DfE.CheckPerformanceData.Infrastructure.ZendeskClient.Services;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using Microsoft.IdentityModel.Tokens;
using Refit;
using System.IdentityModel.Tokens.Jwt;
using System.Net.Http.Headers;
using System.Security.Claims;
using System.Text;

namespace DfE.CheckPerformanceData.Infrastructure;

public static class DependencyManager
{
    public static IServiceCollection AddDfeSignInAuthentication(this IServiceCollection services, IConfiguration config)
    {
        var settings = config.GetSection(DfeSigninSettings.SectionName).Get<DfeSigninSettings>();

        services.AddAuthentication(options =>
        {
            options.DefaultSignInScheme = CookieAuthenticationDefaults.AuthenticationScheme;
            options.DefaultAuthenticateScheme = CookieAuthenticationDefaults.AuthenticationScheme;
            options.DefaultChallengeScheme = OpenIdConnectDefaults.AuthenticationScheme;
        })
            .AddCookie(o =>
            {
                o.ExpireTimeSpan = TimeSpan.FromMinutes(30);
                o.SlidingExpiration = true;
                // o.LogoutPath = "/auth/logout";
                //
                // o.Events.OnRedirectToAccessDenied = ctx =>
                // {
                //     ctx.Response.StatusCode = 403;
                //     ctx.Response.Redirect("/user-with-no-role");
                //     return Task.CompletedTask;
                // };
            }).AddOpenIdConnect(options =>
            {
                options.SignInScheme = CookieAuthenticationDefaults.AuthenticationScheme;
                options.MetadataAddress = settings!.MetadataAddress;
                options.ClientId = settings.ClientId;
                options.ClientSecret = settings.ClientSecret;
                options.ResponseType = OpenIdConnectResponseType.Code;
                options.RequireHttpsMetadata = true;
                options.GetClaimsFromUserInfoEndpoint = true;
                options.SaveTokens = true;
                options.CallbackPath = "/auth/callback";
                options.SignedOutCallbackPath = "/auth/signout-callback";

                options.Scope.Clear();
                options.Scope.Add("email");
                options.Scope.Add("sub");
                options.Scope.Add("openid");
                options.Scope.Add("profile");
                options.Scope.Add("organisationid");

                options.Events.OnTokenResponseReceived = ctx 
                    => Task.CompletedTask;

                options.Events.OnUserInformationReceived = ctx 
                    => Task.CompletedTask;

                options.Events.OnTokenValidated = async ctx =>
                {
                    var enrichmentService = ctx.HttpContext.RequestServices
                        .GetRequiredService<IClaimsEnrichmentService>();

                    await enrichmentService.EnrichAsync((ClaimsIdentity)ctx.Principal!.Identity!);
                };
            });

        return services;
    }

    public static IServiceCollection AddDfeApiClient(this IServiceCollection services, IConfiguration config)
    {
        services.Configure<DfeSigninSettings>(config.GetSection(DfeSigninSettings.SectionName));

        services.AddHttpClient<IDfESignInApiClient, DfeSignInApiClient.DfeSignInApiClient>((serviceProvider, client) =>
        {
            var settings = serviceProvider.GetRequiredService<IOptions<DfeSigninSettings>>().Value;

            var tokenHandler = new JwtSecurityTokenHandler();
            var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(settings.ApiClientSecret));
            var descriptor = new SecurityTokenDescriptor()
            {
                Issuer = settings.ClientId,
                Audience = settings.Audience,
                Expires = DateTime.UtcNow.AddMinutes(5),
                SigningCredentials = new SigningCredentials(key, SecurityAlgorithms.HmacSha256Signature)
            };

            var token = tokenHandler.CreateEncodedJwt(descriptor);

            client.BaseAddress = new Uri(settings.BaseUrl);
            client.DefaultRequestHeaders.Accept.Clear();
            client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        });

        return services;
    }

    public static IServiceCollection AddZendeskApiClient(this IServiceCollection services, IConfiguration config)
    {
        // optional logging setup.
        services.AddTransient<RefitLoggingHandler>();

        var settings = config.GetSection(ZendeskSettings.SectionName).Get<ZendeskSettings>();

        if (settings == null)
        {
            throw new InvalidOperationException("ZendeskSettings section is missing in the configuration.");
        }
        services.Configure<ZendeskSettings>(s => s = settings);

        services.AddTransient<RefitLoggingHandler>();


        services.AddRefitClient<IZendeskApi>(new RefitSettings
        {
            ContentSerializer = new NewtonsoftJsonContentSerializer()
        })

           .ConfigureHttpClient(c =>
           {
               c.BaseAddress = new Uri($"https://{settings.Subdomain}.{settings.Domain}.com");
               var auth = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{settings.Email}/token:{settings.ApiToken}"));
               c.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic", auth);
               c.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
           })
           .AddHttpMessageHandler<RefitLoggingHandler>();

        services.AddScoped<IZendeskService, ZendeskService>();
        services.AddScoped<IZendeskAttachmentService, ZendeskAttachmentService>();
        return services;
    }
}
