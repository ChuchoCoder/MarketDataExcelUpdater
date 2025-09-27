namespace MarketDataExcelUpdater.Core.Configuration;

/// <summary>
/// Thrown when configuration loading or validation fails.
/// Aggregates one or more validation errors with context.
/// </summary>
public sealed class ConfigurationException : Exception
{
    public IReadOnlyList<string> Errors { get; }

    public ConfigurationException(string message, IEnumerable<string> errors) : base(message)
    {
        Errors = errors.ToList();
    }

    public override string ToString()
    {
        return base.ToString() + Environment.NewLine + string.Join(Environment.NewLine, Errors.Select(e => " - " + e));
    }
}