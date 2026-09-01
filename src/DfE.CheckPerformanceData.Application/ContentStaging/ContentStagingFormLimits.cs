namespace DfE.CheckPerformanceData.Application.ContentStaging;

/// <summary>
/// The two limits that together decide how large a bundle the confirm step can actually apply.
///
/// They live here, next to the validator's own ceilings, because they have to be reasoned about
/// as a set: the validator admits a bundle, the preview renders a decision control per item, and
/// the confirm post then has to survive both the form reader's value count and the model
/// binder's collection size. Leave any one of the three out of step and a bundle that validated
/// and previewed cleanly dies on confirm — which is exactly the class of failure that made the
/// bundle round-trip worth removing in the first place.
/// </summary>
public static class ContentStagingFormLimits
{
    /// <summary>
    /// How many per-item decisions the confirm step can bind. Covers the validator's ceiling of
    /// <see cref="ContentBundleValidator.MaxPageNodes"/> pages plus
    /// <see cref="ContentBundleValidator.MaxContentBlocks"/> blocks, so any bundle the validator
    /// accepts can have every one of its items decided individually.
    /// </summary>
    public const int MaxDecisions = ContentBundleValidator.MaxPageNodes + ContentBundleValidator.MaxContentBlocks;

    /// <summary>
    /// How many form values the confirm post may carry. Each decision contributes two (an id and
    /// an action), plus the antiforgery token and the two global mode radios.
    /// </summary>
    public const int MaxFormValues = (MaxDecisions * 2) + 64;

    /// <summary>
    /// Byte ceiling on the confirm post. The body is a session id, two radios and the decisions,
    /// so it is small per item but not negligible in bulk — roughly 70 bytes per decision. This
    /// is generous headroom over that, and it matters because Kestrel's own
    /// MaxRequestBodySize is disabled application-wide, leaving the endpoint otherwise unbounded.
    /// </summary>
    public const int MaxConfirmBodyBytes = 8 * 1024 * 1024;
}
