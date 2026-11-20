using DfE.CheckPerformanceData.Application.CheckYourPupilData;

namespace DfE.CheckPerformanceData.Application.Journey;

/// <summary>The pupil-data change types that a window type offers, in display order.</summary>
public sealed record ChangeTypeChoice(IReadOnlyList<WhatToChange> Available)
{
    /// <summary>
    /// The change type to select without asking the user: the only one offered. Null when the
    /// window type offers more than one.
    /// </summary>
    public WhatToChange? OnlyChange => Available.Count == 1 ? Available[0] : null;
}
