using System.Text.Json;
using DfE.CheckPerformanceData.Application.ResultsEnquiry;
using DfE.CheckPerformanceData.Web.Seeding;
using NSubstitute;

namespace DfE.CheckPerformanceData.Application.UnitTests.ResultsEnquiry;

/// <summary>
/// Pins the dev results seed. It is fixture data, but three things about it are load-bearing:
/// E2E drives student 500001 by name, the pupil search now lists only students who hold results
/// (so three students would make the picker useless to a manual tester), and the revised-grade
/// picker can only list grades for a QAN the 16-19 qualification reference knows (AB#301903).
/// </summary>
public sealed class SeedStudentResultsTests
{
    private static IReadOnlyList<StudentResultRecord> Seeded() => SeedStudentResults.All;

    // The ids SeedPupilData generates for Kingsmead in the Post16 window: 120 included from
    // 500001, then 120 non-included from 500201 (the NonIncludedIndexOffset of 200).
    private static bool IsASeededStudent(string cypmdId)
    {
        var n = int.Parse(cypmdId[1..]);
        return n is (>= 1 and <= 120) or (>= 201 and <= 320);
    }

    [Fact]
    public void The_three_figma_students_keep_exactly_the_results_the_screens_show()
    {
        // E2E enters the journey as Alice Smith (500001) and picks a result by its label, and the
        // two-sessions-of-one-QAN case is why the result key is not the QAN.
        var byStudent = Seeded().GroupBy(r => r.CypmdId).ToDictionary(g => g.Key, g => g.ToArray());

        Assert.Equal(4, byStudent["500001"].Length);
        Assert.Equal(2, byStudent["500001"].Count(r => r.Qan == "60146084"));
        Assert.Equal(["S2024", "S2023"], byStudent["500001"].Where(r => r.Qan == "60146084").Select(r => r.Session).ToArray());
        Assert.Equal(4, byStudent["500002"].Length);
        // Late results 1 amends B's Sport grade; the original row stays, so both show.
        Assert.Equal(
            [ResultsFileTags.Post16Included, ResultsFileTags.Post16LateResults1],
            byStudent["500002"].Where(r => r.Qan == "60172186").Select(r => r.SourceFile).ToArray());
        Assert.Single(byStudent["500003"]);
    }

    [Fact]
    public void Enough_students_hold_results_to_test_the_restricted_search_by_hand()
    {
        // The student search lists only students who hold results. With three, a manual tester
        // cannot exercise paging, sorting or a common-surname search at all.
        var students = Seeded().Select(r => r.CypmdId).Distinct().ToArray();

        Assert.True(students.Length >= 30, $"only {students.Length} students hold results");
    }

    [Fact]
    public void Plenty_of_students_still_hold_nothing()
    {
        // Seeding every student would hide both the search restriction and the result page's
        // empty state behind data that never exercises them.
        var students = Seeded().Select(r => r.CypmdId).Distinct().Count();

        Assert.True(students <= 120, $"{students} of 240 students hold results — too few hold none");
    }

    [Fact]
    public void Both_populations_hold_results()
    {
        // The pages search PupilFilter.All, so inclusion status and holding a result are
        // independent axes and the seed has to show both.
        var students = Seeded().Select(r => int.Parse(r.CypmdId[1..])).Distinct().ToArray();

        Assert.Contains(students, n => n <= 120);
        Assert.Contains(students, n => n >= 201);
    }

    [Fact]
    public void Every_result_belongs_to_a_student_the_pupil_seed_generates()
    {
        // A result for a student who does not exist is unreachable, and would quietly shrink the
        // list the search can offer.
        Assert.All(Seeded(), r => Assert.True(IsASeededStudent(r.CypmdId), $"no seeded student {r.CypmdId}"));
    }

    [Fact]
    public void Every_qualification_is_one_the_16_19_reference_can_list_grades_for()
    {
        // AB#301903: the revised-grade picker lists the 16-19 reference's scale. A seeded QAN the
        // reference does not hold would make the page say "We cannot list grades for this
        // qualification yet" and the journey could not be completed locally.
        using var doc = JsonDocument.Parse(File.ReadAllText(QualificationReferencePath));
        var known = doc.RootElement.EnumerateObject().Select(p => p.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);

        Assert.All(Seeded(), r => Assert.Contains(r.Qan, known));
    }

    [Fact]
    public void Every_seeded_grade_is_one_the_16_19_reference_lists_for_that_qan()
    {
        // The current grade is shown on the page and excluded from the picker; a grade the scale
        // does not hold would be a fixture lie that the picker silently tolerates.
        using var doc = JsonDocument.Parse(File.ReadAllText(QualificationReferencePath));
        var scales = doc.RootElement.EnumerateObject().ToDictionary(
            p => p.Name,
            p => p.Value.GetProperty("grades").EnumerateArray().Select(g => g.GetString()!).ToHashSet(StringComparer.Ordinal),
            StringComparer.OrdinalIgnoreCase);

        Assert.All(Seeded(), r => Assert.Contains(r.Grade, scales[r.Qan]));
    }

    [Fact]
    public void No_second_late_results_row_is_seeded()
    {
        // ILateResultsAvailability reads this: with an LR2 row present the "check your second late
        // results file" interstitial would drop off the local happy path.
        Assert.DoesNotContain(Seeded(), r => r.SourceFile == ResultsFileTags.Post16LateResults2);
    }

    [Fact]
    public void A_non_included_students_results_are_in_the_non_included_file()
    {
        // SeedPupilData generates the non-included students from 500201.
        Assert.All(Seeded().Where(r => r.SourceFile != ResultsFileTags.Post16LateResults1), r =>
            Assert.Equal(int.Parse(r.CypmdId[1..]) > 200 ? ResultsFileTags.Post16NonIncluded : ResultsFileTags.Post16Included, r.SourceFile));
        Assert.Contains(Seeded(), r => r.SourceFile == ResultsFileTags.Post16NonIncluded);
        Assert.Equal("860/4070", SeedStudentResults.Laestab);
    }

    private static string QualificationReferencePath => Path.Combine(
        RepoRoot, "src", "DfE.CheckPerformanceData.Web", "Data", "QualificationReference", "qualification-reference.json");

    private static string RepoRoot
    {
        get
        {
            var thisFile = ThisFilePath();
            return Path.GetFullPath(Path.Combine(Path.GetDirectoryName(thisFile)!, "..", "..", ".."));
        }
    }

    private static string ThisFilePath([System.Runtime.CompilerServices.CallerFilePath] string path = "") => path;
}
