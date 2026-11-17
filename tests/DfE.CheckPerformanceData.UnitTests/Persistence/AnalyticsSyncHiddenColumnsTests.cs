using Dfe.Analytics.EFCore.Configuration;
using DfE.CheckPerformanceData.Application.CurrentUser;
using DfE.CheckPerformanceData.Persistence.Contexts;
using DfE.CheckPerformanceData.Persistence.Entities;
using Microsoft.EntityFrameworkCore;
using NSubstitute;

namespace DfE.CheckPerformanceData.Application.UnitTests.Persistence;

public sealed class AnalyticsSyncHiddenColumnsTests
{
    private static Dictionary<string, ColumnSyncInfo> ChangeRequestColumns()
    {
        var options = new DbContextOptionsBuilder<PortalDbContext>()
            .UseNpgsql("Host=localhost;Database=model-only")
            .Options;

        using var context = new PortalDbContext(options, Substitute.For<ICurrentUserService>());
        var table = new AnalyticsConfigurationProvider().GetConfiguration(context).Tables
            .Single(t => t.Name == context.Model.FindEntityType(typeof(ChangeRequest))!.GetTableName());
        return table.Columns.ToDictionary(c => c.Name);
    }

    [Theory]
    [InlineData(nameof(ChangeRequest.PupilUpn))]
    [InlineData(nameof(ChangeRequest.EnquiryQan))]
    [InlineData(nameof(ChangeRequest.EnquirySession))]
    public void A_personal_or_enquiry_subject_column_syncs_as_hidden(string column) =>
        Assert.True(ChangeRequestColumns()[column].Hidden);

    [Fact]
    public void A_reference_number_syncs_as_a_plain_column() =>
        Assert.False(ChangeRequestColumns()[nameof(ChangeRequest.ReferenceNumber)].Hidden);
}
