namespace SynteticApi.Operations;

public interface IOperation
{
    string OperationType { get; }
    Task<OperationResult> ExecuteAsync(OperationContext context, CancellationToken cancellationToken);
}
