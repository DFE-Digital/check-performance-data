namespace DfE.CheckPerformanceData.Application.Journey.NotOnRoll;

/// <summary>
/// The current <see cref="NotOnRollCollegeList"/>, held in memory. Journey conditions are
/// synchronous, so the list is loaded from storage on startup and refreshed in the background;
/// reading <see cref="Current"/> never touches storage.
/// </summary>
public interface INotOnRollCollegeListProvider
{
    NotOnRollCollegeList Current { get; }
}
