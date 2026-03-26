namespace Archcraft.Domain.ValueObjects;

public readonly record struct Duration
{
    public TimeSpan Value { get; }

    public Duration(TimeSpan value)
    {
        if (value <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(value), "Duration must be positive.");
        Value = value;
    }

    public static Duration Parse(string input)
    {
        if (string.IsNullOrWhiteSpace(input))
            throw new FormatException("Duration string cannot be empty.");

        var total = TimeSpan.Zero;
        int i = 0;

        while (i < input.Length)
        {
            int start = i;
            while (i < input.Length && char.IsDigit(input[i])) i++;

            if (i == start)
                throw new FormatException($"Expected digit at position {i} in duration '{input}'.");

            int number = int.Parse(input[start..i]);

            if (i >= input.Length)
                throw new FormatException($"Expected unit (s, m, h) after number in duration '{input}'.");

            char unit = input[i++];
            total += unit switch
            {
                's' => TimeSpan.FromSeconds(number),
                'm' => TimeSpan.FromMinutes(number),
                'h' => TimeSpan.FromHours(number),
                _ => throw new FormatException($"Unknown duration unit '{unit}' in '{input}'. Supported: s, m, h.")
            };
        }

        return new Duration(total);
    }

    public static implicit operator TimeSpan(Duration d) => d.Value;

    public override string ToString() => Value.ToString();
}
