namespace ApexRacers.Core;

/// <summary>Origin established by an acquisition adapter, never by an identifier or TTL.</summary>
public enum DataProvenance
{
    Unknown = 0,
    Real = 1,
    Demo = 2,
}
