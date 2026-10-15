using ApexRacers.Core.Models;

namespace ApexRacers.Core.PercentileHistory;

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

    /// <summary>Frozen version-one local types for translated provider predicates. Unknown retains
    /// its existing inclusion rule; unrestricted does not acquire future enum members implicitly.</summary>
    public IReadOnlyList<LapSessionType> IncludedTypes => Array.AsReadOnly(new[]
    {
        LapSessionType.Unknown, LapSessionType.Practice, LapSessionType.Qualifying,
        LapSessionType.TimeTrial, LapSessionType.Race, LapSessionType.LoneQualify
    }.Where(Includes).ToArray());

    /// <summary>Only explicitly classified, reviewed policy keys can become new-policy input.
    /// Selection-less, noncanonical and future-policy rows require authorized recomputation.</summary>
    public static PercentileSelection FromStorage(int policyVersion, string key)
    {
        if (policyVersion != CurrentPolicyVersion || key is null)
            throw new ArgumentException("The percentile selection policy is not supported.");
        var parts = key.Split(':');
        if (parts.Length != 3 || parts[0] != $"v{CurrentPolicyVersion}"
            || !int.TryParse(parts[1], System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out var mode)
            || !int.TryParse(parts[2], System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out var mask))
            throw new ArgumentException("A canonical percentile selection key is required.");
        var valid = mode switch
        {
            (int)PercentileUploadSelection.Disabled => mask == 0,
            (int)PercentileUploadSelection.Unrestricted => mask == ReviewedMask,
            (int)PercentileUploadSelection.Restricted => (mask & 1) == 1 && (mask & ~ReviewedMask) == 0,
            _ => false,
        };
        var selection = new PercentileSelection((PercentileUploadSelection)mode, mask);
        if (!valid || selection.Key != key)
            throw new ArgumentException("A canonical percentile selection key is required.");
        return selection;
    }
}
