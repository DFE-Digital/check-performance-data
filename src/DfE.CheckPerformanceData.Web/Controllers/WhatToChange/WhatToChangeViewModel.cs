using DfE.CheckPerformanceData.Application.CheckYourPupilData;
using DfE.CheckPerformanceData.Application.WindowManagement;

namespace DfE.CheckPerformanceData.Web.Controllers;

public sealed class WhatToChangeViewModel
{
    public Guid WindowId { get; set; }
    public WhatToChange? SelectedWhatToChange { get; set; }

    /// <summary>
    /// The pupil-data changes this window offers, in display order: one radio each. A change is in
    /// the list only when its flow file exists for the window type (KS2 spec, decision 1). Empty on
    /// the POST binding target, where it is not supplied; every render path sets it.
    /// </summary>
    public IReadOnlyList<WhatToChange> AvailableChanges { get; set; } = [];

    /// <summary>
    /// The word this window uses for a learner — "student" on 16-19, "pupil" everywhere else.
    /// Nullable only because the model is also the POST binding target, where it is not supplied;
    /// every render path sets it. <see cref="Noun"/> is what the view reads.
    /// </summary>
    public LearnerNoun? LearnerNoun { get; set; }

    /// <summary>
    /// The noun to render. Falls back to "pupil" for the same reason the rest of the service does:
    /// it is the word every key stage but 16-19 uses, and the page must render rather than throw.
    /// </summary>
    public LearnerNoun Noun => LearnerNoun ?? Application.WindowManagement.LearnerNoun.Pupil;
}
