using ApexRacers.Core.Models;

namespace ApexRacers.Api.Services.PercentileHistory;

public enum PercentileUploadSelection { Disabled, Unrestricted, Restricted }

/// <summary>ADR 0007 selection identity. Version 1 freezes the reviewed local taxonomy; adding a
/// supported type requires a policy version, invalidation and authorized recomputation.</summary>
public sealed record PercentileSelection
{
    public const int CurrentPolicyVersion = 1;
    // Deliberately explicit: Enum.GetValues would silently reinterpret stored unrestricted keys.
    private const int ReviewedMask = (1 << (int)LapSessionType.Unknown) | (1 << (int)LapSessionType.Practice)
        | (1 << (int)LapSessionType.Qualifying) | (1 << (int)LapSessionType.TimeTrial)
        | (1 << (int)LapSessionType.Race) | (1 << (int)LapSessionType.LoneQualify);

    private PercentileSelection(PercentileUploadSelection uploads, int mask)
    { PolicyVersion = CurrentPolicyVersion; Uploads = uploads; TypeMask = mask; }

    public int PolicyVersion { get; }
    public PercentileUploadSelection Uploads { get; }
    private int TypeMask { get; }
    public string Key => FormattableString.Invariant($"v{PolicyVersion}:{(int)Uploads}:{TypeMask}");

    public static PercentileSelection FromRequest(bool includeUploads, IReadOnlyList<LapSessionType>? types)
    {
        if (!includeUploads) return new(PercentileUploadSelection.Disabled, 0);
        if (types is null || types.Count == 0) return new(PercentileUploadSelection.Unrestricted, ReviewedMask);
        var mask = 1 << (int)LapSessionType.Unknown;
        foreach (var type in types)
        {
            if ((int)type > (int)LapSessionType.LoneQualify || (ReviewedMask & (1 << (int)type)) == 0)
                throw new ArgumentException("The upload session type is not supported by this selection policy.", nameof(types));
            mask |= 1 << (int)type;
        }
        return new(PercentileUploadSelection.Restricted, mask);
    }

    public bool Includes(LapSessionType type) => (int)type <= (int)LapSessionType.LoneQualify
        && (TypeMask & (1 << (int)type)) != 0;
}
