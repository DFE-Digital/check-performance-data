namespace DfE.CheckPerformanceData.Application.Journey;

/// <summary>
/// Which pupil fields a <c>PupilSearch</c> page's query is compared against.
/// <para>
/// <see cref="All"/> is the historical behaviour and stays the zero value so a page that does not
/// configure a field is unaffected: a case-insensitive prefix match on first name, surname, UPN/ULN
/// and a <c>First Surname</c> split query, plus date of birth on 16-19 windows only.
/// </para>
/// <para>
/// <see cref="CypmdId"/> matches the CYPMD ID alone. The KS4 merge journey sets it on its
/// second-record page, whose label asks for a CYPMD ID and whose hint says to start typing one, so
/// on that page anything else must not match. A page that narrows the search this way has to say so
/// in its <c>subheading</c> — the restriction hides records silently.
/// </para>
/// </summary>
public enum PupilSearchField
{
    All = 0,
    CypmdId = 1
}