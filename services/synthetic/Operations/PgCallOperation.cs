namespace SynteticApi.Operations;

// STUB: simulates a PostgreSQL database query. Real implementation is a future task.
public sealed class PgCallOperation : StubOperationBase
{
    public override string OperationType => "pg-call";
    protected override TimeSpan SimulatedLatency => TimeSpan.FromMilliseconds(20);
}
