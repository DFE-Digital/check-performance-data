namespace DfE.CheckPerformanceData.Web.Controllers.ViewModels;

// Backs the consolidated Messages badge in the admin chrome. The badge shows one number —
// the combined backlog — but the two sources behind it are not equally urgent, so the
// counts are carried separately rather than pre-summed.
//
// The dead-letter queue is an alarm: messages are failing and nobody has dealt with them.
// The feedback inbox is a to-do list. The badge that this one replaced rendered the
// dead-letter count in red, and folding both into a single blue tag meant a production
// queue that was dead-lettering looked exactly like a calm inbox.
public sealed record MessagesBadgeViewModel(int UnreadMessages, int DeadLetterCount)
{
    public int Total => UnreadMessages + DeadLetterCount;

    // Red outranks blue: whatever the inbox is doing, a queue that is dead-lettering is
    // the thing an operator needs to notice first.
    public string TagClass =>
        DeadLetterCount > 0 ? "govuk-tag--red"
        : Total > 0 ? "govuk-tag--blue"
        : "govuk-tag--grey";
}
