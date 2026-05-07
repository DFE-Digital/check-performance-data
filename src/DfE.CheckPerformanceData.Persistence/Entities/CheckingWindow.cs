using DfE.CheckPerformanceData.Domain.Enums;

namespace DfE.CheckPerformanceData.Persistence.Entities;

public sealed class CheckingWindow
{
    public int Id { get; set; }
    public DateTime StartDate { get; set; }
    public DateTime EndDate { get; set; }
    public OrganisationTypes OrganisationType { get; set; }
    public string Title { get; set; }
}
