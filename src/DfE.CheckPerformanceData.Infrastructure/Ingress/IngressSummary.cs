using Newtonsoft.Json;

namespace DfE.CheckPerformanceData.Infrastructure.Ingress;

public sealed class IngressSummaryDocument
{
    [JsonProperty("schemaVersion")] public int SchemaVersion { get; init; } = 1;
    [JsonProperty("attemptId")] public string AttemptId { get; init; } = string.Empty;
    [JsonProperty("checkingWindowId")] public string CheckingWindowId { get; init; } = string.Empty;
    [JsonProperty("checkingExerciseId")] public string CheckingExerciseId { get; init; } = string.Empty;
    [JsonProperty("releaseId")] public string? ReleaseId { get; init; }
    [JsonProperty("startedAtUtc")] public DateTime StartedAtUtc { get; init; }
    [JsonProperty("completedAtUtc")] public DateTime CompletedAtUtc { get; init; }
    [JsonProperty("outcome")] public string Outcome { get; init; } = "failed";
    [JsonProperty("failure")] public IngressSummaryFailure? Failure { get; init; }
    [JsonProperty("readComplete")] public bool ReadComplete { get; init; }
    [JsonProperty("totals")] public IngressSummaryCounts Totals { get; init; } = new();
    [JsonProperty("files")] public IReadOnlyList<IngressSummaryFile> Files { get; init; } = [];
}

public sealed class IngressSummaryFailure
{
    [JsonProperty("stage")] public string Stage { get; init; } = string.Empty;
    [JsonProperty("code")] public string Code { get; init; } = string.Empty;
}

public sealed class IngressSummaryCounts
{
    [JsonProperty("rowsRead")] public int RowsRead { get; init; }
    [JsonProperty("rowsImported")] public int RowsImported { get; init; }
}

public sealed class IngressSummaryFile
{
    [JsonProperty("slotIndex")] public int SlotIndex { get; init; }
    [JsonProperty("datasetId")] public string? DatasetId { get; init; }
    [JsonProperty("datasetName")] public string DatasetName { get; init; } = string.Empty;
    [JsonProperty("sourceFile")] public string SourceFile { get; init; } = string.Empty;
    [JsonProperty("inputFile")] public string InputFile { get; init; } = string.Empty;
    [JsonProperty("state")] public string State { get; set; } = "not_started";
    [JsonProperty("readComplete")] public bool ReadComplete { get; set; }
    [JsonProperty("totals")] public IngressSummaryCounts Totals { get; set; } = new();
    [JsonProperty("laestabs")] public IReadOnlyList<IngressSummaryLaestab> Laestabs { get; set; } = [];
    [JsonIgnore] public bool HasUrnColumn { get; set; }
}

public sealed class IngressSummaryLaestab
{
    [JsonProperty("laestab")] public string? Laestab { get; init; }
    [JsonProperty("totals")] public IngressSummaryCounts Totals { get; init; } = new();
    [JsonProperty("urns", NullValueHandling = NullValueHandling.Ignore)]
    public IReadOnlyList<IngressSummaryUrn>? Urns { get; init; }
}

public sealed class IngressSummaryUrn
{
    [JsonProperty("urn")] public string? Urn { get; init; }
    [JsonProperty("rowsRead")] public int RowsRead { get; init; }
    [JsonProperty("rowsImported")] public int RowsImported { get; init; }
}

/// <summary>Accumulates source rows before the processor transforms them for output.</summary>
public sealed class IngressSummaryCapture
{
    private readonly List<FileCapture> _files;

    public IngressSummaryCapture(Guid windowId, Guid exerciseId, IReadOnlyList<IngressDataset> datasets,
        DateTime startedAtUtc)
    {
        WindowId = windowId;
        ExerciseId = exerciseId;
        AttemptId = Guid.NewGuid();
        StartedAtUtc = DateTime.SpecifyKind(startedAtUtc, DateTimeKind.Utc);
        _files = datasets.Select((dataset, slot) => new FileCapture
        {
            File = new IngressSummaryFile
            {
                SlotIndex = slot,
                DatasetId = dataset.DatasetId == Guid.Empty ? null : dataset.DatasetId.ToString(),
                DatasetName = dataset.Name,
                SourceFile = Path.GetFileName(dataset.InputCsvFile.Replace('\\', '/')),
                InputFile = dataset.InputCsvFile
            }
        }).ToList();
    }

    public Guid WindowId { get; }
    public Guid ExerciseId { get; }
    public Guid AttemptId { get; }
    public DateTime StartedAtUtc { get; }
    public int RowsRead => _files.Sum(file => file.RowsRead);
    public bool ReadComplete => _files.All(file => file.File.ReadComplete);
    public string? FailureStage { get; private set; }
    public string? FailureCode { get; private set; }

    public void SetUrnColumn(int slotIndex, bool exists) => _files[slotIndex].File.HasUrnColumn = exists;

    public void MarkFileStarted(int slotIndex)
    {
        if (!_files[slotIndex].File.ReadComplete)
            _files[slotIndex].File.State = "partially_read";
    }

    public void AddRow(int slotIndex, string? laestab, string? urn)
    {
        var file = _files[slotIndex];
        file.RowsRead++;
        file.File.State = "partially_read";
        var laestabGroup = file.Laestabs.FirstOrDefault(group => string.Equals(group.Value, laestab, StringComparison.Ordinal));
        if (laestabGroup is null)
        {
            laestabGroup = new GroupCapture(laestab);
            file.Laestabs.Add(laestabGroup);
        }

        laestabGroup.RowsRead++;
        if (!file.File.HasUrnColumn)
            return;

        var urnGroup = laestabGroup.Urns.FirstOrDefault(group => string.Equals(group.Value, urn, StringComparison.Ordinal));
        if (urnGroup is null)
        {
            urnGroup = new GroupCapture(urn);
            laestabGroup.Urns.Add(urnGroup);
        }

        urnGroup.RowsRead++;
    }

    public void MarkFileRead(int slotIndex)
    {
        var file = _files[slotIndex];
        file.File.State = "read";
        file.File.ReadComplete = true;
    }

    public void SetFailure(string stage, string code)
    {
        FailureStage = stage;
        FailureCode = code;
    }

    public IngressSummaryDocument Build(string outcome, Guid? releaseId, DateTime completedAtUtc)
    {
        var imported = outcome == "succeeded";
        var files = _files.Select(capture =>
        {
            var file = capture.File;
            file.Totals = new IngressSummaryCounts
            {
                RowsRead = capture.RowsRead,
                RowsImported = imported ? capture.RowsRead : 0
            };
            file.Laestabs = capture.Laestabs
                .OrderBy(group => group.Value is null ? 0 : 1)
                .ThenBy(group => group.Value, StringComparer.Ordinal)
                .Select(group => new IngressSummaryLaestab
                {
                    Laestab = group.Value,
                    Totals = new IngressSummaryCounts
                    {
                        RowsRead = group.RowsRead,
                        RowsImported = imported ? group.RowsRead : 0
                    },
                    Urns = file.HasUrnColumn
                        ? group.Urns
                            .OrderBy(urn => urn.Value is null ? 0 : 1)
                            .ThenBy(urn => urn.Value, StringComparer.Ordinal)
                            .Select(urn => new IngressSummaryUrn
                            {
                                Urn = urn.Value,
                                RowsRead = urn.RowsRead,
                                RowsImported = imported ? urn.RowsRead : 0
                            }).ToList()
                        : null
                }).ToList();
            return file;
        }).ToList();

        return new IngressSummaryDocument
        {
            AttemptId = AttemptId.ToString(),
            CheckingWindowId = WindowId.ToString(),
            CheckingExerciseId = ExerciseId.ToString(),
            ReleaseId = imported && releaseId is not null ? releaseId.Value.ToString() : null,
            StartedAtUtc = StartedAtUtc,
            CompletedAtUtc = DateTime.SpecifyKind(completedAtUtc, DateTimeKind.Utc),
            Outcome = outcome,
            Failure = outcome == "succeeded" || FailureStage is null || FailureCode is null
                ? null
                : new IngressSummaryFailure { Stage = FailureStage, Code = FailureCode },
            ReadComplete = ReadComplete,
            Totals = new IngressSummaryCounts
            {
                RowsRead = RowsRead,
                RowsImported = imported ? RowsRead : 0
            },
            Files = files
        };
    }

    private sealed class FileCapture
    {
        public required IngressSummaryFile File { get; init; }
        public int RowsRead { get; set; }
        public List<GroupCapture> Laestabs { get; } = [];
    }

    private sealed class GroupCapture(string? value)
    {
        public string? Value { get; } = value;
        public int RowsRead { get; set; }
        public List<GroupCapture> Urns { get; } = [];
    }
}
