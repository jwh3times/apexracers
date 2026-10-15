namespace ApexRacers.Core.PercentileHistory;

public enum PercentileOwnerKind { DemoSubject, Personal }

/// <summary>Identity data, never a persistence or publication capability. No stored claim is used.</summary>
public sealed record PercentileOwnerIdentity(Guid UserId, long CustomerId, DataProvenance Provenance, PercentileOwnerKind Kind);
