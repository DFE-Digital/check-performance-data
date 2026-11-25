namespace DfE.CheckPerformanceData.Infrastructure.Ingress.SampleData;

/// <summary>
/// #939: every sample data generator. A schema names one of these in <c>x-ingress.sample</c>.
/// Add a new generator here. The web host and the tests both read this list.
/// </summary>
public static class SampleDataGeneratorCatalogue
{
    public static IReadOnlyList<ISampleDataGenerator> All { get; } =
    [
        new Ks2PupilsSampleGenerator(december: false),
        new Ks2PupilsSampleGenerator(december: true),
        new Ks2SummarySampleGenerator(),

        // Key Stage 4: pupils in Year 11 in 2025/26.
        new SchemaSampleGenerator("ks4-pupils", SampleRowKind.Pupils, 2009, SampleQualification.Gcses),
        new SchemaSampleGenerator("ks4-results", SampleRowKind.Results, 2009, SampleQualification.Gcses),
        new SchemaSampleGenerator("ks4-results-late", SampleRowKind.LateResults, 2009, SampleQualification.Gcses),
        new SchemaSampleGenerator("ks4-summary", SampleRowKind.Summary, 2009, SampleQualification.Gcses),

        // 16 to 19: students in Year 13 in 2025/26. Non-included students are other students.
        new SchemaSampleGenerator("post16-students", SampleRowKind.Pupils, 2007, SampleQualification.ALevels),
        new SchemaSampleGenerator("post16-students-non-included", SampleRowKind.Pupils, 2007, SampleQualification.ALevels, firstPupil: NonIncludedFirstStudent),
        new SchemaSampleGenerator("post16-results", SampleRowKind.Results, 2007, SampleQualification.ALevels),
        new SchemaSampleGenerator("post16-results-non-included", SampleRowKind.Results, 2007, SampleQualification.ALevels, firstPupil: NonIncludedFirstStudent),
        new SchemaSampleGenerator("post16-results-late", SampleRowKind.LateResults, 2007, SampleQualification.ALevels),
        new SchemaSampleGenerator("post16-summary", SampleRowKind.Summary, 2007, SampleQualification.ALevels)
    ];

    // The non-included students of 16-19 come after the included ones, so no student is in both files.
    private const int NonIncludedFirstStudent = 100;
}
