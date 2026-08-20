using DfE.CheckPerformanceData.Application.ResultsEnquiry;

namespace DfE.CheckPerformanceData.Application.UnitTests.ResultsEnquiry;

// AB#297130: the AODC grade reference is what makes the revised-grade picker offer the right
// options for a qualification. It is parsed from a checked-in seed file until the full AODC export
// arrives, so these tests bind the SHIPPED file — a hand-edit that breaks the shape or reorders a
// grade scale fails here rather than silently changing what schools can pick.
public sealed class GradeReferenceTests
{
    private static GradeReferenceLookup ShippedSeed()
        => GradeReferenceLookup.Parse(File.ReadAllText(LocateSeedFile()));

    [Fact]
    public void Pass_grades_render_before_fail_grades()
    {
        // The BTEC example from the ticket, pinned in full: this exact sequence is what the
        // <select> renders, so a reordering in the seed file is a user-visible change.
        var reference = ShippedSeed().Find("60370683");

        Assert.NotNull(reference);
        Assert.Equal(
            ["*2", "P1", "P2", "M1", "M2", "D1", "D2", "F", "Q", "R", "U", "X"],
            reference.AllGrades.ToArray());
        Assert.Equal(["*2", "P1", "P2", "M1", "M2", "D1", "D2"], reference.PassGrades.ToArray());
        Assert.Equal(["F", "Q", "R", "U", "X"], reference.FailGrades.ToArray());
    }

    [Fact]
    public void Carries_the_qualification_title_and_awarding_organisation()
    {
        var reference = ShippedSeed().Find("10025480");

        Assert.NotNull(reference);
        Assert.Equal("OCR", reference.AwardingOrganisation);
        Assert.Equal(["A", "B", "C", "D", "E", "Q", "R", "U", "X"], reference.AllGrades.ToArray());
        Assert.NotEmpty(reference.QualificationTitle);
    }

    [Fact]
    public void An_unknown_qan_returns_null_rather_than_throwing()
        => Assert.Null(ShippedSeed().Find("00000000"));

    [Fact]
    public void A_null_or_blank_qan_returns_null()
    {
        var seed = ShippedSeed();

        Assert.Null(seed.Find(null));
        Assert.Null(seed.Find("   "));
    }

    [Fact]
    public void Lookup_is_case_insensitive_and_trimmed_because_qans_can_end_in_a_letter()
    {
        // 6037116X is a real QAN shape — a supplier file could carry it lower-cased or padded.
        var seed = ShippedSeed();

        Assert.NotNull(seed.Find("6037116x"));
        Assert.NotNull(seed.Find(" 6037116X "));
    }

    [Fact]
    public void The_gcse_nine_to_one_scale_is_seeded_for_the_dev_qans()
    {
        var seed = ShippedSeed();

        foreach (var qan in new[] { "6037116X", "60181576", "60180882" })
        {
            var reference = seed.Find(qan);
            Assert.NotNull(reference);
            Assert.Equal(["9", "8", "7", "6", "5", "4", "3", "2", "1"], reference.PassGrades.ToArray());
            Assert.Equal(["U", "X"], reference.FailGrades.ToArray());
        }
    }

    [Fact]
    public void Every_seed_entrys_key_agrees_with_its_own_qan_field()
    {
        // The file is keyed by QAN and each record repeats it. A disagreement would make a
        // qualification unreachable by the key the results blob actually carries.
        foreach (var (key, reference) in ShippedSeed().Entries)
            Assert.Equal(key, reference.Qan);
    }

    [Fact]
    public void No_seed_entry_has_an_empty_grade_scale()
    {
        // An empty scale renders a picker with nothing to choose, which validation can never pass.
        foreach (var (key, reference) in ShippedSeed().Entries)
            Assert.True(reference.AllGrades.Count > 0, $"QAN {key} has no grades.");
    }

    [Fact]
    public void No_grade_appears_in_both_the_pass_and_fail_list()
    {
        foreach (var (key, reference) in ShippedSeed().Entries)
            Assert.Empty(reference.PassGrades.Intersect(reference.FailGrades, StringComparer.Ordinal));
    }

    [Fact]
    public void Malformed_json_throws_so_a_broken_seed_file_surfaces()
        => Assert.Throws<System.Text.Json.JsonException>(() => GradeReferenceLookup.Parse("{not json"));

    [Fact]
    public void An_empty_document_parses_to_an_empty_lookup()
        => Assert.Empty(GradeReferenceLookup.Parse("{}").Entries);

    private static string LocateSeedFile()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            var candidate = Path.Combine(
                dir.FullName,
                "src", "DfE.CheckPerformanceData.Web", "Data", "GradeReference", "grade-reference.json");
            if (File.Exists(candidate)) return candidate;
            dir = dir.Parent;
        }
        throw new FileNotFoundException(
            "Could not locate src/DfE.CheckPerformanceData.Web/Data/GradeReference/grade-reference.json from " +
            AppContext.BaseDirectory);
    }
}
