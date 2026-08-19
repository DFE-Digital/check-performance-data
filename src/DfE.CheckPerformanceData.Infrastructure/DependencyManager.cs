using System.IdentityModel.Tokens.Jwt;
using System.Net.Http.Headers;
using System.Security.Claims;
using System.Text;
using Azure.Storage.Blobs;
using Dfe.Analytics;
using DfE.CheckPerformanceData.Application.Analytics;
using DfE.CheckPerformanceData.Application.CheckYourPupilData;
using DfE.CheckPerformanceData.Application.ClaimsEnrichment;
using DfE.CheckPerformanceData.Application.Dashboard;
using DfE.CheckPerformanceData.Infrastructure.Analytics;
using DfE.CheckPerformanceData.Application.DfESignInApiClient;
using DfE.CheckPerformanceData.Infrastructure.BlobStorage;
using DfE.CheckPerformanceData.Application.Notify;
using DfE.CheckPerformanceData.Application.ResultsEnquiry;
using DfE.CheckPerformanceData.Application.RulesConfig;
using DfE.CheckPerformanceData.Application.RulesEngine;
using DfE.CheckPerformanceData.Application.ZendeskClient;
using DfE.CheckPerformanceData.Infrastructure.DfeSignInApiClient;
using DfE.CheckPerformanceData.Infrastructure.Ingress;
using DfE.CheckPerformanceData.Infrastructure.Resilience;
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
using Polly;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using Microsoft.IdentityModel.Tokens;
using Notify.Client;
using Refit;
using DfE.CheckPerformanceData.Infrastructure.Notify;

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

        //services.Configure<NotifySettings>(config.GetSection(NotifySettings.SectionName));
        //// GOV.UK Notify is delivered under a separate ticket; until then a no-op keeps the
        //// dead-letter alerting pipeline wired without taking a dependency on the Notify SDK.
        //services.AddSingleton<INotifyService, NotifyService>();

        // Pupil data lives in per-school JSON blobs. Registered here (rather than only in
        // the Web host) because the Persistence repositories that consume it are pulled in
        // by every host that calls AddPersistenceDependencies — including the worker.
        services.AddScoped<IPupilDataBlobClient, PupilDataBlobClient>();

        // AB#296648: the 16-19 exam results a school can raise an enquiry against, held in the same
        // per-window container under the results-enquiry checking-exercise prefix.
        services.AddScoped<IStudentResultsClient, StudentResultsBlobClient>();

        // Analytics sink: the real dfe-analytics adapter when DfeAnalytics:DatasetId is
        // configured (deployed envs wire it via Terraform), else a no-op so dev/review/
        // tests boot without GCP. AddDfeAnalytics binds the DfeAnalytics:* section and
        // registers IEventSender; its BigQueryClient is built lazily (WIF or cred JSON)
        // and only dereferenced when an event is actually sent.
        if (!string.IsNullOrEmpty(config.GetSection("DfeAnalytics")["DatasetId"]))
        {
            services.AddDfeAnalytics();
            services.AddTransient<IAnalyticsService, DfeAnalyticsService>();
        }
        else
        {
            services.TryAddSingleton<IAnalyticsService, NullAnalyticsService>();
        }

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

                    // Engagement metrics: record the successful sign-in. Metrics must never block sign-in,
                    // so any failure here is logged and swallowed.
                    try
                    {
                        var loginRecorder = ctx.HttpContext.RequestServices
                            .GetRequiredService<IOrganisationLoginRecorder>();
                        var loginUserId = ctx.Principal!.FindFirst(ClaimTypes.NameIdentifier)?.Value ?? string.Empty;
                        await loginRecorder.RecordLoginAsync(loginUserId, rolesIdentity);
                    }
                    catch (Exception ex)
                    {
                        ctx.HttpContext.RequestServices.GetRequiredService<ILoggerFactory>()
                            .CreateLogger("DfeSignInEvents")
                            .LogWarning(ex, "Failed to record organisation login for dashboard metrics.");
                    }

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

    // Host used when the settings are incomplete and the real client is not the one that will
    // be used. .invalid is reserved by RFC 2606 and can never resolve, so a call that somehow
    // reached it fails immediately and obviously rather than reaching a real host.
    private const string UnconfiguredZendeskHost = "https://zendesk.invalid";

    // Named in the error rather than referenced from Application, which Infrastructure does not
    // depend on. Kept in step with SettingKeys.ZendeskUseFake.
    private const string ZendeskUseFakeKey = "Zendesk:UseFake";

    // Only the two that form the hostname are structural — a blank Email or ApiToken produces a
    // client that authenticates badly rather than one that cannot be constructed — but all four
    // are required for the real client to work, so all four are reported together.
    internal static List<string> MissingZendeskSettings(ZendeskSettings settings)
    {
        var missing = new List<string>();
        if (string.IsNullOrWhiteSpace(settings.Subdomain)) missing.Add(nameof(settings.Subdomain));
        if (string.IsNullOrWhiteSpace(settings.Domain)) missing.Add(nameof(settings.Domain));
        if (string.IsNullOrWhiteSpace(settings.Email)) missing.Add(nameof(settings.Email));
        if (string.IsNullOrWhiteSpace(settings.ApiToken)) missing.Add(nameof(settings.ApiToken));
        return missing;
    }

    /// <param name="requireRealClient">
    /// Whether the real Zendesk client is the one that will actually be used. When false — the
    /// fake service is selected, which is the default for a fresh dev or test environment —
    /// incomplete settings are tolerated and the client is pointed at an unresolvable host,
    /// because nothing will call it. When true, incomplete settings are a misconfiguration and
    /// are reported as one.
    /// </param>
    public static IServiceCollection AddZendeskApiClient(
        this IServiceCollection services, IConfiguration config, bool requireRealClient = true)
    {
        services.AddTransient<RefitLoggingHandler>();

        var settings = config.GetSection(ZendeskSettings.SectionName).Get<ZendeskSettings>();

        if (settings == null)
        {
            throw new InvalidOperationException("ZendeskSettings section is missing in the configuration.");
        }

        // The base address is built by interpolating Subdomain and Domain, so blank values
        // produce "https://..com" — not a parseable hostname. Left unchecked that surfaces as a
        // UriFormatException thrown while the host resolves its hosted services, which kills the
        // process before anything starts and says nothing about which setting is missing. An
        // unset variable in a compose file or a deployment slot is the likeliest way to get
        // here, so it is worth naming the culprits.
        var missing = MissingZendeskSettings(settings);
        if (missing.Count > 0 && requireRealClient)
        {
            throw new InvalidOperationException(
                $"ZendeskSettings is incomplete: {string.Join(", ", missing)} " +
                $"{(missing.Count == 1 ? "is" : "are")} blank. Set the corresponding " +
                $"ZendeskSettings__* configuration values, or set {ZendeskUseFakeKey}=true to run " +
                "against the fake Zendesk service instead.");
        }

        services.Configure<ZendeskSettings>(s => s = settings);

        services.AddRefitClient<IZendeskApi>(new RefitSettings
        {
            ContentSerializer = new NewtonsoftJsonContentSerializer()
        })
           .ConfigureHttpClient(c =>
           {
               c.BaseAddress = missing.Count == 0
                   ? new Uri($"https://{settings.Subdomain}.{settings.Domain}.com")
                   : new Uri(UnconfiguredZendeskHost);
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

        // Same reasoning as the client settings above: this names a view that exists in a real
        // Zendesk instance, so it is only required when a real Zendesk is the one being talked
        // to. Validated on start, so demanding it regardless is another way for one unset
        // variable to stop the worker — and with it the queue consumers and every retention job
        // — over an integration none of them is using.
        var checkingExercise = services.AddOptions<SchoolCheckingExerciseSettings>()
            .Bind(config.GetSection(SchoolCheckingExerciseSettings.SectionName));
        if (requireRealClient)
        {
            checkingExercise
                .Validate(s => !string.IsNullOrEmpty(s.TargetViewTitle), "TargetViewTitle is required")
                .ValidateOnStart();
        }

        return services;
    }

    public static IServiceCollection AddNotifyService(this IServiceCollection services, IConfiguration config)
    {
        if (NotifyServiceRegistration.ShouldUseFake(config))
        {
            services.AddSingleton<INotifyService, DevConsoleNotifyService>();
            // Bind settings without validation so bulk-email threshold/config resolves in dev/fake mode.
            services.AddOptions<NotifySettings>()
                .Bind(config.GetSection(NotifySettings.SectionName));
            return services;
        }

        services.AddOptions<NotifySettings>()
            .Bind(config.GetSection(NotifySettings.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();

        services.AddOptions<PollySettings>()
            .Bind(config.GetSection("NotifyPollySettings"))
            .ValidateDataAnnotations()
            .ValidateOnStart();

        services.AddSingleton<NotificationClient>(sp =>
        {
            var notifySettings = sp.GetRequiredService<IOptions<NotifySettings>>().Value;
            return new NotificationClient(notifySettings.ApiKey);
        });

        services.AddSingleton<INotifyEmailClient>(sp =>
        {
            var client = sp.GetRequiredService<NotificationClient>();
            return new NotifyEmailClient(client);
        });

        services.AddSingleton<ResiliencePipeline>(sp =>
        {
            var settings = sp.GetRequiredService<IOptions<PollySettings>>().Value;
            var logger = sp.GetRequiredService<ILogger<NotifyService>>();
            return ResiliencePipelineFactory.CreateNotifyRetryPipeline(settings, logger);
        });

        services.AddSingleton<INotifyService, NotifyService>();

        return services;
    }
}