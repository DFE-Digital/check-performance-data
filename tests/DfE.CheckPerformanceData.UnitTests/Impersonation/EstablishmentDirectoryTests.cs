using DfE.CheckPerformanceData.Web.Impersonation;
using Microsoft.AspNetCore.Hosting;
using NSubstitute;

namespace DfE.CheckPerformanceData.Application.UnitTests.Impersonation;

public sealed class EstablishmentDirectoryTests
{
    [Fact]
    public void Search_requires_two_characters_orders_matches_and_limits_results_to_25()
    {
        var root = Path.Combine(Path.GetTempPath(), $"impersonation-directory-{Guid.NewGuid():N}");
        var directoryPath = Path.Combine(root, "Data", "Impersonation");
        Directory.CreateDirectory(directoryPath);
        try
        {
            var rows = Enumerable.Range(0, 30).Select(i => $"\"{100000+i}\",\"123\",\"{i:0000}\",\"Example School {i:00}\",3,19");
            File.WriteAllText(Path.Combine(directoryPath, "EstablishmentDetails.csv"),
                "\"URN\",\"LA (code)\",\"EstablishmentNumber\",\"EstablishmentName\",\"StatutoryLowAge\",\"StatutoryHighAge\"\n" + string.Join("\n", rows));
            var environment = Substitute.For<IWebHostEnvironment>();
            environment.ContentRootPath.Returns(root);
            var establishments = new EstablishmentDirectory(environment);

            Assert.Empty(establishments.Search("E"));
            var matches = establishments.Search("Example");
            Assert.Equal(25, matches.Count);
            Assert.Equal("1230000", matches[0].Laestab);
        }
        finally { Directory.Delete(root, recursive: true); }
    }
}
