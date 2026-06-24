using System.IdentityModel.Tokens.Jwt;
using System.Net.Http.Headers;
using System.Security.Claims;
using System.Text;
using Azure.Storage.Blobs;
using DfE.CheckPerformanceData.Application.CheckYourPupilData;
using DfE.CheckPerformanceData.Application.ClaimsEnrichment;
using DfE.CheckPerformanceData.Application.DfESignInApiClient;
using DfE.CheckPerformanceData.Application.Notifications;
using DfE.CheckPerformanceData.Infrastructure.BlobStorage;
using DfE.CheckPerformanceData.Application.RulesConfig;
using DfE.CheckPerformanceData.Application.RulesEngine;
using DfE.CheckPerformanceData.Application.ZendeskClient;
using DfE.CheckPerformanceData.Infrastructure.DfeSignInApiClient;
using DfE.CheckPerformanceData.Infrastructure.Notifications;
using DfE.CheckPerformanceData.Infrastructure.RulesEngine;
using DfE.CheckPerformanceData.Infrastructure.ZendeskClient;
using DfE.CheckPerformanceData.Infrastructure.ZendeskClient.Services;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using Microsoft.IdentityModel.Tokens;
using Refit;

namespace DfE.CheckPerformanceData.Infrastructure;

public static class DependencyManager
{
    public static IServiceCollection AddInfrastructureDependencies(this IServiceCollection services, IConfiguration config)
    {
        var conn = config.GetConnectionString("AzureStorage");
        if (!string.IsNullOrEmpty(conn))
        {
            services.AddSingleton<BlobServiceClient>(new BlobServiceClient(conn));
        }

        services.Configure<NotifySettings>(config.GetSection(NotifySettings.SectionName));
        // GOV.UK Notify is delivered under a separate ticket; until then a no-op keeps the
        // dead-letter alerting pipeline wired without taking a dependency on the Notify SDK.
        services.AddSingleton<INotifyClient, NoOpNotifyClient>();

        // Pupil data lives in per-school JSON blobs. Registered here (rather than only in
        // the Web host) because the Persistence repositories that consume it are pulled in
        // by every host that calls AddPersistenceDependencies — including the worker.
        services.AddScoped<IPupilDataBlobClient, PupilDataBlobClient>();

        return services;
    }

    /// <summary>
    /// Registers <see cref="BlobRulesProvider"/> as both <see cref="IRulesProvider"/>
    /// and an <see cref="IHostedService"/> so it loads the rules JSON before the
    /// queue worker starts dequeuing messages. Also wires
    /// <see cref="RulesProviderHealthCheck"/> into the host's health-check pipeline.
    /// </summary>
    public static IServiceCollection AddRulesProvider(this IServiceCollection services, IConfiguration config)
    {
        services.Configure<BlobRulesProviderOptions>(config.GetSection(BlobRulesProviderOptions.SectionName));

        services.AddSingleton<IRulesBlobReader>(sp =>
        {
            var options = sp.GetRequiredService<IOptions<BlobRulesProviderOptions>>().Value;
            var connection = config.GetConnectionString("AzureStorage")
                ?? throw new InvalidOperationException(
                    "AzureStorage connection string is required for the rules provider.");
            var container = new BlobServiceClient(connection).GetBlobContainerClient(options.RulesBlobContainer);
            return new AzureRulesBlobReader(container);
        });

        services.AddSingleton(TimeProvider.System);

        // Self-seed the rules-config blobs from the image-bundled JSON when absent. Registered as a
        // hosted service *before* the provider below so it runs first and the provider's initial
        // synchronous load sees freshly-seeded rules (reporting Healthy instead of cold-fallback on a
        // fresh environment). Idempotent: existing blobs are never overwritten.
        services.TryAddSingleton<IRulesConfigStore, BlobRulesConfigStore>();
        services.AddHostedService<RulesConfigSeeder>();

        services.AddSingleton<BlobRulesProvider>();
        services.AddSingleton<IRulesProvider>(sp => sp.GetRequiredService<BlobRulesProvider>());
        services.AddHostedService(sp => sp.GetRequiredService<BlobRulesProvider>());

        services.AddHealthChecks()
            .AddCheck<RulesProviderHealthCheck>("rules-provider");

        return services;
    }

    public static IServiceCollection AddDfeSignInAuthentication(this IServiceCollection services, IConfiguration config)
    {
        var settings = config.GetSection(DfeSigninSettings.SectionName).Get<DfeSigninSettings>()
            ?? throw new InvalidOperationException(
                $"Configuration section '{DfeSigninSettings.SectionName}' is missing or empty. " +
                "Set DfeSignIn:MetadataAddress, DfeSignIn:ClientId, DfeSignIn:ClientSecret, " +
                "DfeSignIn:Audience and DfeSignIn:ApiClientSecret in appsettings.json " +
                "or via environment variables (e.g. DfeSignIn__MetadataAddress).");

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
            })
            .AddOpenIdConnect(options =>
            {
                options.SignInScheme = CookieAuthenticationDefaults.AuthenticationScheme;
                options.MetadataAddress = settings.MetadataAddress;
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

                if (!settings.RequireHttpsMetadata)
                {
                    options.TokenValidationParameters = new TokenValidationParameters
                    {
                        ValidateIssuer = false,
                        ValidateAudience = false,
                        ValidateLifetime = false,
                        ValidateIssuerSigningKey = false,
                        RequireSignedTokens = false,
                        SignatureValidator = delegate (string token, TokenValidationParameters _)
                        {
                            return new JsonWebToken(token);
                        }
                    };
                    options.ProtocolValidator.RequireNonce = false;
                    options.RequireHttpsMetadata = false;
                }
                
                options.Events.OnTokenResponseReceived = ctx
                    => Task.CompletedTask;

                options.Events.OnUserInformationReceived = ctx
                    => Task.CompletedTask;

                options.Events.OnTokenValidated = async ctx =>
                {
                    var enrichmentService = ctx.HttpContext.RequestServices
                        .GetRequiredService<IClaimsEnrichmentService>();

                    var rolesIdentity = await enrichmentService.EnrichAsync(ctx.Principal!);
                    if (rolesIdentity == null)
                    {
                        // The user authenticated but has no organisation or no role/access
                        // for this service. Abort the sign-in (no auth cookie is issued) and
                        // send them to a friendly page rather than letting them into the app
                        // with no permissions.
                        ctx.HttpContext.RequestServices
                            .GetRequiredService<ILoggerFactory>()
                            .CreateLogger("DfeSignIn")
                            .LogInformation("DfE Sign-in user has no usable organisation or role; redirecting to no-access page.");
                        ctx.HandleResponse();
                        ctx.Response.Redirect("/Account/NoAccess");
                        return;
                    }

                    ctx.Principal!.AddIdentity(rolesIdentity);

                    // Clear any prior dev-impersonation marker on a fresh real sign-in so
                    // the user's effective role reflects their true DfE claims, not a
                    // stale overlay from before. The impersonation header link can be
                    // clicked again any time after sign-in to re-impersonate.
                    ctx.HttpContext.Response.Cookies.Delete("cypd-dev-impersonation");
                };

                // Safety net: any exception thrown during the remote handshake (including
                // from claims enrichment) lands here. Without this the user sees a raw 500.
                options.Events.OnRemoteFailure = ctx =>
                {
                    ctx.HttpContext.RequestServices
                        .GetRequiredService<ILoggerFactory>()
                        .CreateLogger("DfeSignIn")
                        .LogError(ctx.Failure, "DfE Sign-in remote authentication failed.");
                    ctx.HandleResponse();
                    ctx.Response.Redirect("/Account/NoAccess");
                    return Task.CompletedTask;
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
        services.AddTransient<RefitLoggingHandler>();

        var settings = config.GetSection(ZendeskSettings.SectionName).Get<ZendeskSettings>();

        if (settings == null)
        {
            throw new InvalidOperationException("ZendeskSettings section is missing in the configuration.");
        }
        services.Configure<ZendeskSettings>(s => s = settings);

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

        // Register ZendeskTicketFieldSettings and IZendeskTicketFieldService
        services.Configure<ZendeskTicketFieldSettings>(config.GetSection(ZendeskTicketFieldSettings.SectionName));
        services.AddSingleton<ZendeskTicketFieldSettings>(
            sp => sp.GetRequiredService<IOptions<ZendeskTicketFieldSettings>>().Value);
        services.AddSingleton<IZendeskTicketFieldService, ZendeskTicketFieldService>();

        services.AddOptions<PollySettings>()
            .Bind(config.GetSection(PollySettings.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();

        services.AddOptions<SchoolCheckingExerciseSettings>()
            .Bind(config.GetSection(SchoolCheckingExerciseSettings.SectionName))
            .Validate(s => !string.IsNullOrEmpty(s.TargetViewTitle), "TargetViewTitle is required")
            .ValidateOnStart();

        return services;
    }
}