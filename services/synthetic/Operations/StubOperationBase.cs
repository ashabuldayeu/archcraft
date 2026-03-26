using System.Diagnostics;

namespace SynteticApi.Operations;

// Base class for stub operations — simulates latency, does not connect to real systems.
public abstract class StubOperationBase : IOperation
{
    public abstract string OperationType { get; }
    protected abstract TimeSpan SimulatedLatency { get; }

    public async Task<OperationResult> ExecuteAsync(OperationContext context, CancellationToken cancellationToken)
    {
        Stopwatch sw = Stopwatch.StartNew();
        await Task.Delay(SimulatedLatency, cancellationToken);
        sw.Stop();
        return OperationResult.Success(sw.Elapsed);
    }
}
