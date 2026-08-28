namespace DfE.CheckPerformanceData.Application.Journey.Validators;

/// <summary>
/// AB#298201: the National Centre Number on a missing-qualification enquiry is at most 5
/// characters. A format validator rather than CharacterLimit because the ticket pins exact copy
/// that the generic "{title} must be N characters or less" message cannot produce. Emptiness is
/// not checked here — the field is optional, and the engine only runs validators on non-empty values.
/// </summary>
public sealed class NcnValidator : IFormatValidator
{
    public string Name => "Ncn";
    public string FailureMessage => "National Centre Number (NCN) must be 5 characters or less";
    public bool IsValid(string value) => value.Trim().Length <= 5;
}
