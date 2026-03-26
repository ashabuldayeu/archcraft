using System.Diagnostics;

namespace Adapters.Contracts;

// Base class for stub adapter operations — simulates latency, no real I/O.
public abstract class StubAdapterOperationBase : IAdapterOperation
{
    public abstract string OperationName { get; }
    protected abstract TimeSpan SimulatedLatency { get; }

    public async Task<ExecuteResponse> ExecuteAsync(ExecuteRequest request, CancellationToken cancellationToken)
    {
        Stopwatch sw = Stopwatch.StartNew();
        await Task.Delay(SimulatedLatency, cancellationToken);
        sw.Stop();
        return ExecuteResponse.Success(sw.ElapsedMilliseconds);
    }
}
