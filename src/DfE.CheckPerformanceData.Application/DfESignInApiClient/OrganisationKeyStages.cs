using DfE.CheckPerformanceData.Domain.Enums;

namespace DfE.CheckPerformanceData.Application.DfESignInApiClient;

public static class OrganisationKeyStages
{
    public static readonly OrganisationKeyStageDto KS2 = new(1, "Key Stage 2", 3, 11, KeyStages.KS2);
    public static readonly OrganisationKeyStageDto KS4 = new(2, "Key Stage 4", 14, 16, KeyStages.KS4);
    public static readonly OrganisationKeyStageDto SixteenToEighteen = new(3, "16 to 18", 16, 18, KeyStages.Post16);

    public static readonly IReadOnlyList<OrganisationKeyStageDto> All = [KS2, KS4, SixteenToEighteen];
}