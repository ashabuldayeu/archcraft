namespace SynteticApi.Operations;

// STUB: simulates an HTTP call to another service. Real implementation (with target resolution) is a future task.
public sealed class HttpCallOperation : StubOperationBase
{
    public override string OperationType => "http-call";
    protected override TimeSpan SimulatedLatency => TimeSpan.FromMilliseconds(50);
}
