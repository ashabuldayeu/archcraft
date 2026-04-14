namespace Adapters.Contracts;

public interface IDataSeeder
{
    Task SeedAsync(int rows, CancellationToken cancellationToken = default);
    Task ClearAsync(CancellationToken cancellationToken = default);
}
