namespace DfE.CheckPerformanceData.Application.Media.Serving;

/// <summary>
/// The secret key that signs media URLs. Every instance of the service must return the same key,
/// or a URL made by one instance is refused by another. The key is read synchronously because
/// pages build URLs while they render.
/// </summary>
public interface IMediaUrlSigningKey
{
    byte[] GetKey();
}
