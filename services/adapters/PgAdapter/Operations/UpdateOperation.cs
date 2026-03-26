using Adapters.Contracts;

namespace PgAdapter.Operations;

// STUB: simulates a PostgreSQL UPDATE. Real implementation is a future task.
public sealed class UpdateOperation : StubAdapterOperationBase
{
    public override string OperationName => "update";
    protected override TimeSpan SimulatedLatency => TimeSpan.FromMilliseconds(22);
}
