namespace DfE.CheckPerformanceData.Application.WindowManagement;

public interface ICheckingDataReader
{
    Task<byte[]?> ReadAsync(CheckingDataExercise exercise, string laestab, CancellationToken cancellationToken);
    Task<byte[]?> ReadSchemaAsync(Guid windowId, string schemaFile, CancellationToken cancellationToken);
}
