using Adapters.Contracts;

namespace HttpAdapter.Operations;

// STUB: simulates an outbound HTTP request. Real implementation is a future task.
public sealed class RequestOperation : StubAdapterOperationBase
{
    public override string OperationName => "request";
    protected override TimeSpan SimulatedLatency => TimeSpan.FromMilliseconds(50);
}
