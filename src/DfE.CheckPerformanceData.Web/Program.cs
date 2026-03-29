using Azure.Storage.Queues;
using DfE.CheckPerformanceData.Data;
using DfE.CheckPerformanceData.Infrastructure.DfeSignIn;
using DfE.CheckPerformanceData.Infrastructure.DfeSignInApiClient;
using GovUk.Frontend.AspNetCore;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;

var builder = WebApplication.CreateBuilder(args);

builder.Services
    .AddDfeApiClient(builder.Configuration)
    .AddDfeSignInAuthentication(builder.Configuration)
    .AddGovUkFrontend(options => options.Rebrand = true);

builder.Services.AddDbContext<PortalDbContext>(options =>
    options.UseNpgsql(builder.Configuration.GetConnectionString("Postgres")));

builder.Services.AddSingleton(_ => new QueueServiceClient(builder.Configuration.GetConnectionString("AzureStorage"),
    new QueueClientOptions(QueueClientOptions.ServiceVersion.V2025_11_05)
    {
        MessageEncoding = QueueMessageEncoding.Base64
    }));

builder.Services.AddControllersWithViews();

builder.Services.AddHealthChecks();

var app = builder.Build();

app.UseGovUkFrontend();

app.UseHealthChecks("/healthcheck");

// Configure the HTTP request pipeline.
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Home/Error");
    // The default HSTS value is 30 days. You may want to change this for production scenarios, see https://aka.ms/aspnetcore-hsts.
    app.UseHsts();
}

app.UseHttpsRedirection();
app.UseRouting();

app.UseAuthentication();
app.UseAuthorization();

app.MapStaticAssets();

app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Home}/{action=Index}/{id?}")
    .WithStaticAssets();


app.Run();
