namespace Archcraft.Domain.ValueObjects;

public readonly record struct RpsTarget
{
    public int Value { get; }

    public RpsTarget(int value)
    {
        if (value < 1)
            throw new ArgumentOutOfRangeException(nameof(value), $"RPS must be at least 1, got {value}.");
        Value = value;
    }

    public static implicit operator int(RpsTarget rps) => rps.Value;

    public override string ToString() => Value.ToString();
}
