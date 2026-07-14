using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace DfE.CheckPerformanceData.Application.Notify;

public interface IRequestNotificationService
{
    Task NotifySubmissionConfirmedAsync(Guid windowId, DateTime deadlineDate, string referenceNumber);
    Task NotifyBulkSubmissionConfirmedAsync(Guid windowId, DateTime deadlineDate, IReadOnlyList<string> referenceNumbers);
    Task NotifyDataCheckConfirmedAsync(DateTime deadlineDate, string referenceNumber);

    Task NotifyAmendmentWithdrawnAsync(string referenceNumber, DateTime deadlineDate);

    Task NotifyDataCheckWithdrawnAsync(string referenceNumber, DateTime deadlineDate);
}
