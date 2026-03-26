using Adapters.Contracts;

namespace RedisAdapter.Operations;

// STUB: simulates a Redis SET. Real implementation is a future task.
public sealed class SetOperation : StubAdapterOperationBase
{
    public override string OperationName => "set";
    protected override TimeSpan SimulatedLatency => TimeSpan.FromMilliseconds(3);
}
