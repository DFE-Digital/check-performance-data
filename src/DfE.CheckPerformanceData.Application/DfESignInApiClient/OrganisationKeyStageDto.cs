using DfE.CheckPerformanceData.Domain.Entities;

namespace DfE.CheckPerformanceData.Application.DfESignInApiClient;

public record OrganisationKeyStageDto(int Id, string Title, int LowAge, int HighAge, KeyStages KeyStage);