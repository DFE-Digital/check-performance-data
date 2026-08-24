using System.Text.Json;

namespace DfE.CheckPerformanceData.Application.UnitTests.ResultsEnquiry;

/// <summary>
/// Pins the checked-in qualification reference seed (AB#297848). The document is generated from the
/// supplier's QualList.xlsx by scripts/Convert-QualListToReference.ps1; these tests catch a bad
/// regeneration (truncated file, swapped columns, duplicated QANs) before it ships.
/// </summary>
public class QualificationReferenceSeedDocumentTests
{
    private static readonly Lazy<JsonDocument> Doc = new(() =>
        JsonDocument.Parse(File.ReadAllText(FindSeedPath())));

    private static string FindSeedPath()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            var candidate = Path.Combine(
                dir.FullName,
                "src", "DfE.CheckPerformanceData.Web", "Data", "QualificationReference", "qualification-reference.json");
            if (File.Exists(candidate)) return candidate;
            dir = dir.Parent;
        }
        throw new FileNotFoundException(
            "Could not locate qualification-reference.json from " + AppContext.BaseDirectory);
    }

    [Fact]
    public void Holds_every_qualification_from_the_supplier_export()
    {
        // 974 distinct QANs in the QualList.xlsx snapshot converted on 2026-08-24. A regeneration
        // that loses rows would silently narrow the AO/QAN dropdowns for every school.
        Assert.Equal(974, Doc.Value.RootElement.EnumerateObject().Count());
    }

    [Fact]
    public void The_e2e_qualification_carries_its_real_syllabus_codes()
    {
        // The E2E journey drives AQA GCSE Maths 60146084 — one of the 13 QANs the SyllabusCodes
        // export covers for 16-19. Losing its codes makes the details page unpassable and the
        // whole E2E suite red.
        var q = Doc.Value.RootElement.GetProperty("60146084");
        Assert.Equal("AQA", q.GetProperty("awardingOrganisation").GetString());
        var codes = q.GetProperty("syllabusCodes").EnumerateArray()
            .Select(c => c.GetProperty("code").GetString()).ToArray();
        Assert.Equal(new[] { "8300F", "8300H" }, codes);
        Assert.Equal("Mathematics Higher Tier",
            q.GetProperty("syllabusCodes")[1].GetProperty("title").GetString());
    }

    [Fact]
    public void Thirteen_qualifications_have_16_19_syllabus_coverage()
    {
        // The SyllabusCodes export's 1619-flagged rows cover exactly 13 QualList QANs (finding
        // FLAGGED to the BA — every other QAN dead-ends at the required syllabus field). A
        // regeneration that changes this number means new supplier data and deserves a look.
        var covered = Doc.Value.RootElement.EnumerateObject()
            .Count(e => e.Value.GetProperty("syllabusCodes").GetArrayLength() > 0);
        Assert.Equal(13, covered);
    }

    [Fact]
    public void Every_entry_names_its_qan_ao_and_at_least_one_grade_and_complete_syllabus_rows()
    {
        foreach (var entry in Doc.Value.RootElement.EnumerateObject())
        {
            Assert.Equal(entry.Name, entry.Value.GetProperty("qan").GetString());
            Assert.False(string.IsNullOrWhiteSpace(entry.Value.GetProperty("awardingOrganisation").GetString()));
            Assert.True(entry.Value.GetProperty("grades").GetArrayLength() > 0);
            foreach (var code in entry.Value.GetProperty("syllabusCodes").EnumerateArray())
            {
                Assert.False(string.IsNullOrWhiteSpace(code.GetProperty("code").GetString()));
                Assert.False(string.IsNullOrWhiteSpace(code.GetProperty("title").GetString()));
            }
        }
    }
}
