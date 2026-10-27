namespace DfE.CheckPerformanceData.Application.WindowManagement;

/// <summary>
/// Where one dataset slot stands against the release schools see, for the admin's Data tab.
/// <see cref="CheckingExerciseDto.StatusOf"/> decides it.
/// </summary>
public enum DatasetStatus
{
    /// <summary>The slot does not hold both its CSV and its schema.</summary>
    NotSupplied,

    /// <summary>The slot holds both files, but the live release did not read these files: they
    /// are new, or they replace the files it read. The next validation makes them live.</summary>
    NotValidated,

    /// <summary>The live release read the files the slot holds now.</summary>
    Live,

    /// <summary>Another slot's file replaced this one's, so no run reads it.</summary>
    Retired
}
