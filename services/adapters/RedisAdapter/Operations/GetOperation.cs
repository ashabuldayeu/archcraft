using Adapters.Contracts;

namespace RedisAdapter.Operations;

// STUB: simulates a Redis GET. Real implementation is a future task.
public sealed class GetOperation : StubAdapterOperationBase
{
    public override string OperationName => "get";
    protected override TimeSpan SimulatedLatency => TimeSpan.FromMilliseconds(3);
}
