using DfE.CheckPerformanceData.Application.Notify;
using Microsoft.Extensions.DependencyInjection;

namespace DfE.CheckPerformanceData.Web.Startup;

public static class NotificationExtensions
{
    public static IServiceCollection AddCpdNotifications(this IServiceCollection services)
    {
        services.AddScoped<IEmailLinkGenerator, Notify.EmailLinkGenerator>();

        services.AddScoped<IRequestNotificationService, DfE.CheckPerformanceData.Infrastructure.Notify.RequestNotificationService>();

        // Email sending is fire-and-forget: the request thread enqueues onto an in-process channel
        // (ChannelNotificationDispatcher, a singleton shared with the background worker) and returns;
        // NotificationBackgroundService drains the channel and resolves recipients + sends off-thread
        // via NotificationSender. The INotificationDispatcher seam lets this become a durable queue later.
        services.AddScoped<INotificationSender, DfE.CheckPerformanceData.Infrastructure.Notify.NotificationSender>();
        services.AddSingleton<Notify.ChannelNotificationDispatcher>();
        services.AddSingleton<INotificationDispatcher>(sp =>
            sp.GetRequiredService<Notify.ChannelNotificationDispatcher>());
        services.AddHostedService<Notify.NotificationBackgroundService>();

        return services;
    }
}
