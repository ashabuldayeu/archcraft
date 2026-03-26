using System.Diagnostics;

namespace SynteticApi.Operations;

public sealed class OperationContext
{
    public required string CorrelationId { get; init; }
    public required Activity? ParentActivity { get; init; }
}
