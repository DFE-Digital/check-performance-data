namespace DfE.CheckPerformanceData.Application.CheckYourPupilData;

/// <summary>
/// The minimal pupil surface everything downstream of deserialisation programs against, so the
/// journey, search, session and request submission never learn there is a second key stage.
/// Per-window-type display concerns (table and CSV columns) deliberately live outside this
/// contract — they read the concrete record via <c>PupilColumnSets</c>.
/// </summary>
public interface IPupilRecord
{
    Guid Id { get; }
    string Firstname { get; }
    string Surname { get; }
    string Sex { get; }

    /// <summary>Raw supplier string; formatted for display by <see cref="PupilDateFormatter"/>.</summary>
    string DateOfBirth { get; }

    int Age { get; }
    string Cypmd_Id { get; }
    string Laestab { get; }

    /// <summary>LDS match reference (MATCHREF) from the pupil record. 0 = not supplied.</summary>
    int MatchRef { get; }

    /// <summary>School admission date (ENTRYDAT) from the pupil record. Raw supplier string;
    /// empty = not supplied (Post16 supplier files have no ENTRYDAT).</summary>
    string EntryDate { get; }

    /// <summary>UPN for KS4, ULN for Post16. Neither key stage has both.</summary>
    string Identifier { get; }

    /// <summary>
    /// Drives the Included / Non-included split. NOT a Pincl code — the Post16 non-included
    /// supplier file has no P_INCL column, so its inclusion comes from the file of origin.
    /// </summary>
    bool IsIncluded { get; }

    /// <summary>KS4 add-back semantics (see <c>PupilIsAddBackCondition</c>). Null for Post16.</summary>
    int? Pincl { get; }
}
