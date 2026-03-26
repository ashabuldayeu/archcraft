namespace SynteticApi.Operations;

// STUB: simulates a Redis cache lookup. Real implementation is a future task.
public sealed class RedisCallOperation : StubOperationBase
{
    public override string OperationType => "redis-call";
    protected override TimeSpan SimulatedLatency => TimeSpan.FromMilliseconds(5);
}
