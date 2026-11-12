namespace GapMiner.Domain.ValueObjects;

/// <summary>
/// Severity of a feature gap, constrained to the inclusive range [1.0, 10.0].
/// Instances are created exclusively through the <see cref="Create"/> factory,
/// which enforces the range; the constructor is private.
/// </summary>
public readonly record struct SeverityScore
{
    /// <summary>Lowest valid severity.</summary>
    public const double Minimum = 1.0;

    /// <summary>Highest valid severity.</summary>
    public const double Maximum = 10.0;

    /// <summary>Gets the severity value; always within [1.0, 10.0].</summary>
    public double Value { get; }

    private SeverityScore(double value) => Value = value;

    /// <summary>
    /// Creates a <see cref="SeverityScore"/>, enforcing the inclusive range [1.0, 10.0].
    /// </summary>
    /// <param name="value">The severity; must be between 1.0 and 10.0.</param>
    /// <exception cref="ArgumentOutOfRangeException">
    /// <paramref name="value"/> is NaN or outside the inclusive range [1.0, 10.0].
    /// </exception>
    public static SeverityScore Create(double value)
    {
        if (double.IsNaN(value) || value < Minimum || value > Maximum)
        {
            throw new ArgumentOutOfRangeException(
                nameof(value),
                value,
                $"Severity score must be in the inclusive range [{Minimum}, {Maximum}].");
        }

        return new SeverityScore(value);
    }
}
