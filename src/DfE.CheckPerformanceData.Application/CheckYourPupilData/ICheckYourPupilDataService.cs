using System.Text.Json.Serialization;
using DfE.CheckPerformanceData.Application.CheckYourPupilData.Columns;
using DfE.CheckPerformanceData.Application.Journey;
using DfE.CheckPerformanceData.Application.LandingPage;

namespace DfE.CheckPerformanceData.Application.CheckYourPupilData;

public interface ICheckYourPupilDataService
{
    /// <summary>One page of a population, already projected to the window type's column set.</summary>
    Task<(PupilTable Table, int TotalCount)> GetPupilTableAsync(Guid windowId, bool included, string? search, int page, int pageSize);

    /// <summary>Every pupil in a population, projected to the window type's CSV column set.</summary>
    Task<PupilTable> GetPupilCsvAsync(Guid windowId, bool included);

    Task<CheckingWindowDto> GetCheckingWindowAsync(Guid windowId);
    Task<IReadOnlyList<PupilSuggestionDto>> GetPupilSuggestionsAsync(Guid windowId, string query,
        PupilFilter filter, Guid? excludeId = null);

    Task<PupilDto> GetPupilAsync(Guid windowId, Guid pupilId);
}

// public sealed class GetCheckYourPupilDataResult
// {
//     public required CheckingWindowDto Window { get; init; }
//     public required List<PupilDto> IncludedPupils { get; init; }
//     public required List<PupilDto> NonIncludedPupils { get; init; }
// }

public sealed class PupilDto
{
    public required string Firstname { get; init; }
    public required string Surname { get; init; }
    public required Guid Id { get; init; }
    public required string Sex { get; init; }
    public required string DateOfBirth { get; init; }
    public required int Age { get; init; }
    public required string Cypmd_Id { get; init; }

    /// <summary>UPN for KS4, ULN for Post16. The serialised name stays "Upn" so session state,
    /// the requests blob, ChangeRequest.PupilUpn and RequestDocument.PupilDetails.Upn are
    /// unaffected by the model-side rename.</summary>
    [JsonPropertyName("Upn")]
    public required string Identifier { get; init; }

    /// <summary>Inclusion status code from the pupil record (e.g. 401). Not required so
    /// sessions serialised before this field existed still deserialise; 0 = not supplied.</summary>
    public int Pincl { get; set; }

    /// <summary>LDS match reference (MATCHREF) from the pupil record. Not required so sessions
    /// serialised before this field existed still deserialise; 0 = not supplied.</summary>
    public int MatchRef { get; set; }

    /// <summary>DfE establishment number (LAESTAB) from the pupil record. Not required so
    /// sessions serialised before this field existed still deserialise; empty = not supplied.</summary>
    public string Laestab { get; set; } = string.Empty;

    /// <summary>School admission date (ENTRYDAT) from the pupil record, the source of the
    /// Admission date ticket field (FR-012). Raw string whose format is supplier-defined;
    /// empty = not supplied.</summary>
    public string EntryDate { get; set; } = string.Empty;
}

public record PupilSuggestionDto(Guid Id, string Label);
