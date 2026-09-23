using DfE.CheckPerformanceData.Application.Audit;
using DfE.CheckPerformanceData.Domain.Enums;
using DfE.CheckPerformanceData.Web.Controllers.AuditLog;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Routing;

namespace DfE.CheckPerformanceData.Application.UnitTests.Web.Controllers;

// The export (AB#294592) is the filtered log, every page, one line per row, no payload columns.
public sealed class AuditLogCsvTests
{
    private static readonly DateTime At = new(2026, 6, 8, 14, 38, 2, DateTimeKind.Utc);

    private static AuditLogRow Egress() => new(1, At, "sub-1", "Ops One", "EgressRun", "run-1", "Transfer",
        Guid.Parse("11111111-1111-1111-1111-111111111111"), "KS4 June 2026", ["NewLearners", "RemoveLearners"], AuditOutcome.Success);

    [Fact]
    public void The_header_names_every_column_and_no_payload()
    {
        Assert.Equal("Timestamp (UTC),User,Activity,Action,Entity type,Entity id,Checking window,Output types,Status", AuditLogCsv.Header);
        Assert.DoesNotContain("Values", AuditLogCsv.Header);
    }

    [Fact]
    public void An_egress_row_is_one_line()
        => Assert.Equal("2026-06-08T14:38:02Z,Ops One,Data egress,Transfer,EgressRun,run-1,KS4 June 2026,NewLearners; RemoveLearners,Success", AuditLogCsv.Line(Egress()));

    [Fact]
    public void A_generic_row_leaves_the_egress_columns_empty_and_falls_back_to_the_subject_id()
    {
        var row = new AuditLogRow(2, At, "sub-2", null, "Setting", "CMS:PageLength", "Update", null, null, [], null);
        Assert.Equal("2026-06-08T14:38:02Z,sub-2,System setting,Update,Setting,CMS:PageLength,,,", AuditLogCsv.Line(row));
    }

    [Theory]
    [InlineData("plain", "plain")]
    [InlineData("", "")]
    [InlineData(null, "")]
    [InlineData("Smith, Jo", "\"Smith, Jo\"")]
    [InlineData("say \"hi\"", "\"say \"\"hi\"\"\"")]
    [InlineData("two\nlines", "\"two\nlines\"")]
    public void Fields_are_quoted_only_when_they_need_to_be(string? field, string expected)
        => Assert.Equal(expected, AuditLogCsv.Field(field));

    private static async IAsyncEnumerable<AuditLogRow> Rows(params AuditLogRow[] rows)
    {
        foreach (var row in rows)
        {
            yield return row;
            await Task.Yield();
        }
    }

    [Fact]
    public async Task The_result_streams_the_header_and_rows_as_a_csv_attachment()
    {
        var http = new DefaultHttpContext();
        http.Response.Body = new MemoryStream();
        var context = new ActionContext(http, new RouteData(), new ActionDescriptor());

        await new AuditLogCsvResult(Rows(Egress()), "audit-log-20260608-143802.csv").ExecuteResultAsync(context);

        Assert.Equal("text/csv; charset=utf-8", http.Response.ContentType);
        Assert.Equal("attachment; filename=\"audit-log-20260608-143802.csv\"", http.Response.Headers["Content-Disposition"].ToString());
        http.Response.Body.Position = 0;
        var body = await new StreamReader(http.Response.Body).ReadToEndAsync();
        // Encoding.UTF8 writes a BOM (Excel-friendly, same as the app-logs download), so Contains, not StartsWith.
        Assert.Contains(AuditLogCsv.Header + Environment.NewLine, body);
        Assert.Contains(AuditLogCsv.Line(Egress()) + Environment.NewLine, body);
    }
}
