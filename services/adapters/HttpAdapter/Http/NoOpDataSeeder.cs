using Adapters.Contracts;

namespace HttpAdapter.Http;

public sealed class NoOpDataSeeder : IDataSeeder
{
    public Task SeedAsync(int rows, CancellationToken cancellationToken = default) =>
        Task.CompletedTask;

    public Task ClearAsync(CancellationToken cancellationToken = default) =>
        Task.CompletedTask;
}
