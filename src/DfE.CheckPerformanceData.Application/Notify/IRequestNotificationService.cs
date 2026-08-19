using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace DfE.CheckPerformanceData.Application.Notify;

public interface IRequestNotificationService
{
    Task NotifySubmissionConfirmedAsync(Guid windowId, DateTime deadlineDate, string referenceNumber, EmailSubstitutions substitutions);
    Task NotifyBulkSubmissionConfirmedAsync(Guid windowId, DateTime deadlineDate, IReadOnlyList<string> referenceNumbers, EmailSubstitutions substitutions);
    Task NotifyDataCheckConfirmedAsync(DateTime deadlineDate, string referenceNumber, EmailSubstitutions substitutions);

    Task NotifyAmendmentWithdrawnAsync(string referenceNumber, DateTime deadlineDate, EmailSubstitutions substitutions);

    Task NotifyDataCheckWithdrawnAsync(string referenceNumber, DateTime deadlineDate, EmailSubstitutions substitutions);
}
