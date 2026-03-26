namespace Archcraft.Domain.ValueObjects;

public readonly record struct ServicePort
{
    public int Value { get; }

    public ServicePort(int value)
    {
        if (value < 1 || value > 65535)
            throw new ArgumentOutOfRangeException(nameof(value), $"Port must be between 1 and 65535, got {value}.");
        Value = value;
    }

    public static implicit operator int(ServicePort port) => port.Value;

    public override string ToString() => Value.ToString();
}
