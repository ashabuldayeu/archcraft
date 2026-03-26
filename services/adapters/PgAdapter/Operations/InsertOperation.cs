using Adapters.Contracts;

namespace PgAdapter.Operations;

// STUB: simulates a PostgreSQL INSERT. Real implementation is a future task.
public sealed class InsertOperation : StubAdapterOperationBase
{
    public override string OperationName => "insert";
    protected override TimeSpan SimulatedLatency => TimeSpan.FromMilliseconds(25);
}
