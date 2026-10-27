namespace DfE.CheckPerformanceData.Application.WindowManagement;

/// <summary>
/// Whether one exercise has data for schools, for the windows list. An exercise can be visible to
/// schools and still hold nothing, so the list shows this beside <see cref="ExerciseSchoolStatus"/>.
/// <see cref="CheckingExerciseDto.DataStatus"/> decides it.
/// </summary>
public enum ExerciseDataStatus
{
    /// <summary>No slot in use holds both its files, and no release is live.</summary>
    NoFiles,

    /// <summary>Files are uploaded, but no release is live: the exercise was never validated, or
    /// no release has been made live yet.</summary>
    NotValidated,

    /// <summary>A release is live and it read the files every complete slot holds now.</summary>
    Live,

    /// <summary>A release is live, but a slot holds a file that it did not read. The next
    /// validation makes the new file live.</summary>
    LiveWithNewFiles
}
