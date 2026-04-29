using DfE.CheckPerformanceData.Application.ClaimsEnrichment;
using DfE.CheckPerformanceData.Application.DfESignInApiClient;
using DfE.CheckPerformanceData.Infrastructure.DfeSignInApiClient;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using Microsoft.IdentityModel.Tokens;
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

                    var rolesIdentity = await enrichmentService.EnrichAsync(ctx.Principal!);
                    if (rolesIdentity != null)
                        ctx.Principal!.AddIdentity(rolesIdentity);
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
}
