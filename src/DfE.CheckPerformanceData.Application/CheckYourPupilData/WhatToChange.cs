namespace DfE.CheckPerformanceData.Application.CheckYourPupilData;

public enum WhatToChange
{
    Merge,
    Include,
    Remove,
    // No Add journey exists yet (there is no Add_*.json flow config) — the member exists so
    // ChangeRequest.AmendmentType can already model it. Appended last so existing values are unmoved.
    Add
}
