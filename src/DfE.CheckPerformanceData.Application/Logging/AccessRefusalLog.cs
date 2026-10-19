using Microsoft.Extensions.Logging;

namespace DfE.CheckPerformanceData.Application.Logging;

// Every access-control refusal the app logs goes through here, so there is one event to search
// for (OWASP A01 and A09, #612). Two shapes: a signed-in user refused an admin section, and a
// school asking for a request reference that is not theirs. Both messages start "Access refused"
// because the admin App logs page searches rendered message text and has no event-id filter;
// Logit filters on EventId.Name. Nothing here takes a name, an email or pupil data — the user id
// is the only personal identifier, and the tests pin that negative.
public static class AccessRefusalLog
{
    // The first named event in the code base. Later security events belong beside it, in the
    // 4000s, so one range can be alerted on.
    public static readonly EventId AccessRefused = new(4001, "AccessRefused");

    // Written in place of a missing user id so the message never reads "user  lacks".
    public const string NoUserId = "(no id)";

    // The /admin landing page is gated on "any grant at all", so a refusal there has no single
    // section key; this marker stands in for it.
    public const string AnyGrantedSection = "any-granted-section";

    public static void AdminSectionRefused(this ILogger logger, string? userId, string sectionKey, string path) =>
        logger.LogWarning(AccessRefused,
            "Access refused: user {UserId} lacks admin section {SectionKey} for {Path}",
            userId ?? NoUserId, sectionKey, path);

    // The reference number is the project's accepted log identifier (never pupil data); the URN
    // says which school asked, and the window id scopes the reference.
    public static void ScopedReadMissed(this ILogger logger, string userId, string organisationUrn, Guid windowId, string referenceNumber) =>
        logger.LogWarning(AccessRefused,
            "Access refused: user {UserId} of organisation {OrganisationUrn} asked for reference {ReferenceNumber} in window {WindowId}, which is not theirs",
            userId, organisationUrn, referenceNumber, windowId);
}
