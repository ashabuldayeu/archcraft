namespace Adapters.Contracts;

public interface IAdapterOperation
{
    string OperationName { get; }
    Task<ExecuteResponse> ExecuteAsync(ExecuteRequest request, CancellationToken cancellationToken);
}
