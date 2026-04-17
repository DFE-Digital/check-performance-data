using DfE.CheckPerformanceData.Domain.Enums;

namespace DfE.CheckPerformanceData.Application.DfESignInApiClient;

public record OrganisationKeyStageDto(int Id, string Title, int LowAge, int HighAge, KeyStages KeyStage);