using Azure.Storage.Blobs;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace DfE.CheckPerformanceData.Web.Startup;

public static class DataProtectionExtensions
{
    public static WebApplicationBuilder AddCpdDataProtection(this WebApplicationBuilder builder)
    {
        // ASP.NET Core Data Protection key ring. Production runs multiple web replicas, so the
        // key ring MUST be shared across all web replicas: the OIDC 'state' and correlation cookie are
        // protected when the user is redirected TO DfE Sign-in and unprotected on the /auth/callback
        // return. With the default in-memory keys, each pod has its own ring, so a callback that
        // load-balances to a different pod fails with "Unable to unprotect the message.State." and
        // the user lands on the no-access page. Persisting keys to the shared blob storage account
        // (and pinning the application name so all instances derive the same purpose strings) makes
        // every replica agree. When no storage connection string is configured the default
        // in-memory provider is used (single-instance local dev), which is fine.
        var dataProtection = builder.Services.AddDataProtection()
            .SetApplicationName("check-performance-data");
        var dataProtectionConn = builder.Configuration.GetConnectionString("AzureStorage");
        if (!string.IsNullOrEmpty(dataProtectionConn))
        {
            var keyRingContainer = new BlobServiceClient(dataProtectionConn)
                .GetBlobContainerClient("data-protection-keys");
            keyRingContainer.CreateIfNotExists();
            dataProtection.PersistKeysToAzureBlobStorage(keyRingContainer.GetBlobClient("keys.xml"));
        }

        return builder;
    }
}
