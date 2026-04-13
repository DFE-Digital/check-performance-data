namespace DfE.CheckPerformanceData.Domain.Entities;

public class CheckingWindow
{
    public int Id { get; set; }
    public DateTime StartDate { get; set; }
    public DateTime EndDate { get; set; }
    public KeyStages KeyStage { get; set; }
    public string Title { get; set; }
}

public enum KeyStages
{
    KS2, //3-11
    KS4, //11-16
    Post16 //16-18
}