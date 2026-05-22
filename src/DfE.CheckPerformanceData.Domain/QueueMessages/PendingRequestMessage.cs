using System.Text.Json.Serialization;

namespace DfE.CheckPerformanceData.Domain.QueueMessages;

/// <summary>
/// The normalised on-the-wire shape produced by
/// <see cref="RequestMessageFactory.Parse"/>. Carries every field the rules
/// engine and the Zendesk-ticket handler need from a single message variant,
/// regardless of what <c>DecisionType</c> (if any) the Web side stamped on it.
///
/// <para>This supersedes <see cref="ApprovedRequestMessage"/>,
/// <see cref="RejectedRequestMessage"/>, and <see cref="ScrutinyMessage"/> at
/// the parse boundary. Those classes remain in the codebase for backward
/// compatibility during the transition; new code should consume
/// <see cref="PendingRequestMessage"/>.</para>
/// </summary>
public sealed class PendingRequestMessage : RequestMessage, IRequestMessageUploads
{
    /// <summary>
    /// Free-text reason supplied by the school alongside the request. May be empty.
    /// Surfaced in the Zendesk ticket description regardless of engine outcome.
    /// </summary>
    [JsonPropertyName("reason")]
    public string? Reason { get; set; }

    /// <summary>
    /// Evidence uploads associated with the request. Empty when the school
    /// did not attach files. The handler resolves each <see cref="UploadInfo"/>
    /// against the evidence-uploads blob container and attaches to the ticket.
    /// </summary>
    [JsonPropertyName("uploads")]
    public List<UploadInfo> Uploads { get; set; } = new();

    public override Task ProcessAsync(CancellationToken token) => Task.CompletedTask;
}
