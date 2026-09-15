namespace DfE.CheckPerformanceData.Application.WindowManagement;

public interface ICheckingDataReader
{
    Task<byte[]?> ReadAsync(CheckingDataExercise exercise, string laestab, CancellationToken cancellationToken);
}
