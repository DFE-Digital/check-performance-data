using DfE.CheckPerformanceData.Application.CheckYourPupilData;

namespace DfE.CheckPerformanceData.Web.Common;

public static class DuplicateRequestMessages
{
    public static string FriendlyAction(WhatToChange whatToChange) => whatToChange switch
    {
        WhatToChange.Remove => "remove a pupil from data",
        WhatToChange.Include => "include a pupil in data",
        WhatToChange.Merge => "merge duplicate pupil records",
        _ => throw new ArgumentOutOfRangeException(nameof(whatToChange), whatToChange, null)
    };

    public static string PupilSelectionMessage(bool isSelf, bool reasonsMatch, WhatToChange whatToChange)
    {
        var action = FriendlyAction(whatToChange);

        if (isSelf && reasonsMatch)
            return $"You already have a pending request to {action} for this pupil.";

        if (!isSelf && reasonsMatch)
            return $"Another user at your school has already submitted a request to {action} for this pupil. "
                   + "Please coordinate with colleagues before submitting a new request.";

        if (isSelf && !reasonsMatch)
            return "You already have a pending request for this pupil. You can view your existing request.";

        return "Another user at your school has a pending request for this pupil. "
               + "Please coordinate with colleagues or contact support if this appears to be in error.";
    }

    public static string SummaryMessage(bool isSelf, bool reasonsMatch, WhatToChange whatToChange)
    {
        var action = FriendlyAction(whatToChange);

        if (isSelf && reasonsMatch)
            return $"You already have a pending request to {action} for this pupil. Select a different pupil.";

        if (!isSelf && reasonsMatch)
            return $"Another user at your school has already submitted a request to {action} for this pupil. "
                   + "Select a different pupil.";

        if (isSelf && !reasonsMatch)
            return "You already have a pending request for this pupil. Select a different pupil.";

        return "Another user at your school has a pending request for this pupil. Select a different pupil.";
    }

    public static bool ShowLink(bool isSelf) => isSelf;
}
