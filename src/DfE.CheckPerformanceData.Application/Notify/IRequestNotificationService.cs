using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace DfE.CheckPerformanceData.Application.Notify;

public interface IRequestNotificationService
{
    Task NotifySubmissionConfirmedAsync(Guid windowId, DateTime deadlineDate, string referenceNumber);
    Task NotifyBulkSubmissionConfirmedAsync(Guid windowId, DateTime deadlineDate, IReadOnlyList<string> referenceNumbers);
    Task NotifyDataCheckConfirmedAsync(DateTime deadlineDate, string referenceNumber);

    /// <summary>
    /// Confirms a submitted 16-19 results enquiry to the person who submitted it (AB#296648).
    ///
    /// Unlike an amendment confirmation this carries no deadline: an enquiry is not something the
    /// school must come back and finish before the window closes. It goes to the submitter only, not
    /// the whole organisation — the school has not been asked to do anything further.
    /// </summary>
    Task NotifyResultsEnquirySubmittedAsync(string referenceNumber);

    Task NotifyAmendmentWithdrawnAsync(string referenceNumber, DateTime deadlineDate);

    Task NotifyDataCheckWithdrawnAsync(string referenceNumber, DateTime deadlineDate);
}
