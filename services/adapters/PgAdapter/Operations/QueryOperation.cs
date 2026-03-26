using Adapters.Contracts;

namespace PgAdapter.Operations;

// STUB: simulates a PostgreSQL SELECT query. Real implementation is a future task.
public sealed class QueryOperation : StubAdapterOperationBase
{
    public override string OperationName => "query";
    protected override TimeSpan SimulatedLatency => TimeSpan.FromMilliseconds(20);
}
