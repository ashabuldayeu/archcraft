namespace Archcraft.Contracts;

public sealed record ValidationResult
{
    public bool IsValid { get; init; }
    public IReadOnlyList<string> Errors { get; init; } = [];

    public static ValidationResult Success() => new() { IsValid = true };

    public static ValidationResult Failure(IReadOnlyList<string> errors) =>
        new() { IsValid = false, Errors = errors };

    public static ValidationResult Failure(string error) =>
        Failure([error]);
}
