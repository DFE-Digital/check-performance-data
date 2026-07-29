using DfE.CheckPerformanceData.Application.CheckYourPupilData;
using DfE.CheckPerformanceData.Application.CheckYourPupilData.Columns;
using DfE.CheckPerformanceData.Domain.Enums;

namespace DfE.CheckPerformanceData.Application.UnitTests.CheckYourPupilData;

// The KS4 table and CSV must be byte-for-byte what they were before columns became data-driven.
// The Post16 sets are the agreed placeholder until LDS confirm the 16-19 column list.
public class PupilColumnSetsTests
{
    private static PupilRecord Ks4Pupil() => new()
    {
        Id = Guid.NewGuid(),
        Upn = "A860407000001B",
        Cypmd_Id = "000123",
        Surname = "Smith",
        Firstname = "Alice",
        Sex = "F",
        DateOfBirth = "2010-09-01",
        Age = 15,
        Pincl = 401,
        Laestab = "860/4070",
        Urn = 136309,
        EntryDate = "2021-09-01",
        SenF = "N",
        FirstLanguage = "ENG",
        Ethnicity = "WBRI",
        ActualYearGroup = "11",
        NewMobile = true
    };

    private static Post16PupilRecord Post16Pupil(bool included) => new()
    {
        Id = Guid.NewGuid(),
        Included = included,
        Cypmd_Id = "500123",
        Surname = "Jones",
        Firstname = "Bob",
        Sex = "M",
        DateOfBirth = "2007-09-01 00:00:00.0000000",
        Age = 18,
        Pincl = included ? 501 : null,
        Laestab = "860/4070",
        Urn = "136309",
        Ukprn = "10001234",
        Uln = "9900112233"
    };

    [Fact]
    public void Ks4_table_columns_are_unchanged()
    {
        var columns = PupilColumnSets.Table(CheckingWindowType.KS4June, included: true);

        Assert.Equal(
            ["Last name", "First name", "Sex", "Date of birth", "Age", "CYPMD ID"],
            columns.Select(c => c.Header));
    }

    [Fact]
    public void Ks4_csv_columns_are_unchanged()
    {
        var columns = PupilColumnSets.Csv(CheckingWindowType.KS4June, included: true);

        Assert.Equal(
        [
            "UPN", "CYPMD ID", "Surname", "Forename", "Sex", "Date of birth", "Age",
            "Pupil Inclusion Status Flag", "Pupil Inclusion description", "DfE Establishment Number",
            "School URN", "Admission date", "SEN", "Pupil's first language", "Pupil's ethnicity",
            "Actual Year Group", "Mobile"
        ], columns.Select(c => c.Header));
    }

    [Fact]
    public void Ks4_csv_row_renders_dates_and_the_pincl_description()
    {
        var table = PupilTable.Build(PupilColumnSets.Csv(CheckingWindowType.KS4June, included: true), [Ks4Pupil()]);

        var row = Assert.Single(table.Rows);
        Assert.Equal("A860407000001B", row[0]);
        Assert.Equal("01/09/2010", row[5]);   // formatted from the raw supplier string
        Assert.Equal("401", row[7]);
        Assert.StartsWith("Pupil on roll and included", row[8]);
        Assert.Equal("01/09/2021", row[11]);
        Assert.Equal("1", row[16]);           // NewMobile
    }

    [Fact]
    public void Post16_included_table_shows_uln_and_inclusion_status()
    {
        var columns = PupilColumnSets.Table(CheckingWindowType.Post16, included: true);

        Assert.Equal(
            ["Last name", "First name", "Sex", "Date of birth", "Age", "CYPMD ID", "ULN", "Inclusion status"],
            columns.Select(c => c.Header));
    }

    [Fact]
    public void Post16_non_included_table_omits_inclusion_status_because_the_file_has_no_pincl()
    {
        var columns = PupilColumnSets.Table(CheckingWindowType.Post16, included: false);

        Assert.Equal(
            ["Last name", "First name", "Sex", "Date of birth", "Age", "CYPMD ID", "ULN"],
            columns.Select(c => c.Header));
    }

    [Fact]
    public void Post16_csv_header_says_uln_not_upn()
    {
        var columns = PupilColumnSets.Csv(CheckingWindowType.Post16, included: true);

        Assert.Equal("ULN", columns[0].Header);
        Assert.DoesNotContain("UPN", columns.Select(c => c.Header));
    }

    [Fact]
    public void Post16_row_formats_the_supplier_timestamp_dob()
    {
        var table = PupilTable.Build(PupilColumnSets.Table(CheckingWindowType.Post16, included: false), [Post16Pupil(included: false)]);

        var row = Assert.Single(table.Rows);
        Assert.Equal("01/09/2007", row[3]);
        Assert.Equal("9900112233", row[6]);
    }

    [Fact]
    public void An_empty_population_still_carries_its_headers()
    {
        var table = PupilTable.Build(PupilColumnSets.Table(CheckingWindowType.KS4June, included: true), []);

        Assert.Equal(6, table.Headers.Count);
        Assert.Empty(table.Rows);
    }
}
