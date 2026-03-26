namespace SynteticApi.Operations;

public sealed class OperationRegistry
{
    private readonly Dictionary<string, IOperation> _operations;

    public OperationRegistry(IEnumerable<IOperation> operations)
    {
        _operations = operations.ToDictionary(o => o.OperationType, StringComparer.OrdinalIgnoreCase);
    }

    public IOperation? Get(string operationType)
        => _operations.TryGetValue(operationType, out IOperation? op) ? op : null;
}
