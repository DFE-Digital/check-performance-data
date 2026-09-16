using System.Text.RegularExpressions;

namespace DfE.CheckPerformanceData.Application.Egress;

/// <summary>
/// The "Validate against LDS spec" step. Required fields, permitted values and shapes, one failure
/// per offending field. Values and shapes are LDS_CYPMD_Data specification v2.4; nothing here
/// loosens as a work-around — a record that fails here fails the batch.
/// </summary>
public static partial class LdsSpecValidator
{
    public const string StepName = "Validate against LDS spec";

    private static readonly HashSet<string> Stages = ["KS2", "KS4", "16-19"];
    // v2.4: both sheets — "F" (female), "M" (male), "U" (unknown).
    private static readonly HashSet<string> Sexes = ["F", "M", "U"];

    public static IReadOnlyList<EgressRecordFailure> Validate(RemoveLearnerRow row)
    {
        var f = new Failures(row.TicketId, row.ReferenceNumber);
        f.Digits("Correction_ID", row.CorrectionId);
        f.Equals("Correction_Type", row.CorrectionType, "31");
        f.Digits("Correction_Reason", row.CorrectionReason);
        f.OneOf("Key_Stage", row.KeyStage, Stages);
        f.DigitsOfLength("Establishment_Number", row.EstablishmentNumber, 4);
        f.Required("Surname", row.Surname);
        f.Required("Forename", row.Forename);
        f.OneOf("Sex", row.Sex, Sexes);
        f.IsoDate("Date_of_Birth", row.DateOfBirth);
        f.DigitsOfLength("Cycle_Year", row.CycleYear, 4);
        f.Month("Cycle_Month", row.CycleMonth);
        f.DigitsOfLength("Local_Authority", row.LocalAuthority, 3);
        f.Digits("Learner_ID", row.LearnerId);
        return f.List;
    }

    public static IReadOnlyList<EgressRecordFailure> Validate(NewLearnerRow row)
    {
        var f = new Failures(row.TicketId, row.ReferenceNumber);
        f.Digits("Correction_ID", row.CorrectionId);
        f.Equals("Correction_Type", row.CorrectionType, "10");
        f.OneOf("Key_Stage", row.KeyStage, Stages);
        f.DigitsOfLength("Establishment_Number", row.EstablishmentNumber, 4);
        f.Required("Surname", row.Surname);
        f.Required("Forename", row.Forename);
        f.OneOf("Sex", row.Sex, Sexes);
        f.IsoDate("Date_of_Birth", row.DateOfBirth);
        f.IsoDate("Admission_Date", row.AdmissionDate);
        f.OptionalMaxLength("Post_Code", row.Postcode, 8);
        f.DigitsOfLength("Cycle_Year", row.CycleYear, 4);
        f.Month("Cycle_Month", row.CycleMonth);
        f.DigitsOfLength("Local_Authority", row.LocalAuthority, 3);
        f.Digits("URN", row.SchoolUrn);
        f.OptionalDigitsMaxLength("ULN", row.Uln, 11);
        f.OptionalMaxLength("UPN", row.Upn, 13);
        f.OptionalDigits("Learner_ID", row.LearnerId);
        f.OptionalYearGroup("Year_Group", row.YearGroup);
        return f.List;
    }

    [GeneratedRegex(@"^\d+$")]
    private static partial Regex DigitsOnly();

    [GeneratedRegex(@"^\d{4}-\d{2}-\d{2}$")]
    private static partial Regex IsoDateShape();

    private sealed class Failures(long? ticketId, string reference)
    {
        public List<EgressRecordFailure> List { get; } = [];

        private void Add(string field, string reason) =>
            List.Add(new EgressRecordFailure(StepName, ticketId, reference, field, reason));

        public void Required(string field, string value)
        {
            if (string.IsNullOrWhiteSpace(value)) Add(field, "is required");
        }

        public void Equals(string field, string value, string expected)
        {
            if (value != expected) Add(field, $"must be {expected}");
        }

        public void Digits(string field, string value)
        {
            if (string.IsNullOrWhiteSpace(value) || !DigitsOnly().IsMatch(value)) Add(field, "must be a number");
        }

        public void OptionalDigits(string field, string value)
        {
            if (value.Length > 0 && !DigitsOnly().IsMatch(value)) Add(field, "must be a number when supplied");
        }

        public void DigitsOfLength(string field, string value, int length)
        {
            if (value.Length != length || !DigitsOnly().IsMatch(value)) Add(field, $"must be {length} digits");
        }

        public void OptionalMaxLength(string field, string value, int max)
        {
            if (value.Length > max) Add(field, $"must be {max} characters or fewer");
        }

        public void OptionalDigitsMaxLength(string field, string value, int max)
        {
            if (value.Length == 0) return;
            if (!DigitsOnly().IsMatch(value)) Add(field, "must be a number when supplied");
            else if (value.Length > max) Add(field, $"must be {max} digits or fewer");
        }

        public void OptionalOneOf(string field, string value, IReadOnlySet<string> permitted)
        {
            if (value.Length > 0 && !permitted.Contains(value)) Add(field, $"must be blank or one of {string.Join(", ", permitted.Order())}");
        }

        // Spec: Year_Group "1-13", NULL allowed.
        public void OptionalYearGroup(string field, string value)
        {
            if (value.Length > 0 && (!int.TryParse(value, out var y) || y is < 1 or > 13)) Add(field, "must be blank or a year group 1 to 13");
        }

        public void OneOf(string field, string value, IReadOnlySet<string> permitted)
        {
            if (!permitted.Contains(value)) Add(field, $"must be one of {string.Join(", ", permitted.Order())}");
        }

        public void IsoDate(string field, string value)
        {
            if (!IsoDateShape().IsMatch(value) || !DateOnly.TryParseExact(value, "yyyy-MM-dd", out _))
                Add(field, "must be a real date in yyyy-MM-dd format");
        }

        public void Month(string field, string value)
        {
            if (!int.TryParse(value, out var m) || m is < 1 or > 12) Add(field, "must be a month number 1 to 12");
        }
    }
}
