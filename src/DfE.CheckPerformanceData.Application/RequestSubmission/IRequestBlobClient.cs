namespace DfE.CheckPerformanceData.Application.RequestSubmission;

/// <summary>
/// The <see cref="RequestDocument"/> saved when an amendment is submitted (#536). The close sweep
/// sends this saved copy to Zendesk rather than rebuilding it, so the ticket shows what the school
/// submitted, with the school name the sweep cannot know.
/// </summary>
public interface IRequestBlobClient
{
    Task SaveRequestAsync(Guid windowId, RequestDocument document);

    /// <summary>Null when no document was saved for the reference.</summary>
    Task<RequestDocument?> GetRequestAsync(Guid windowId, string referenceNumber);
}
