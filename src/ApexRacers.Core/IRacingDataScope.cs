namespace ApexRacers.Core;

/// <summary>Server-selected request/acquisition origin. Selection is immutable once established.</summary>
public sealed class IRacingDataScope
{
    public DataProvenance Provenance { get; private set; }
    public bool IsSelected { get; private set; }

    public IRacingDataScope() { }
    public IRacingDataScope(DataProvenance provenance) => Select(provenance);

    public void Select(DataProvenance provenance)
    {
        if (IsSelected)
            throw new InvalidOperationException("The acquisition scope is already selected.");
        if (!Enum.IsDefined(provenance))
            throw new ArgumentOutOfRangeException(nameof(provenance));
        Provenance = provenance;
        IsSelected = true;
    }
}

public interface IProvenancedData
{
    DataProvenance Provenance { get; set; }
}
