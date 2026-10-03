using System.Reflection;

namespace ApexRacers.Core;

/// <summary>Validates declared owned CLR contracts; never discovers an upstream JSON shape.</summary>
public static class MappedEvidenceContract
{
    public static void RequireOwned<T>()
    {
        if (ContainsSdkOrUntypedValue(typeof(T), []))
            throw new ArgumentException("Mapped evidence requires an owned typed contract, not a raw SDK payload.");
    }

    private static bool ContainsSdkOrUntypedValue(Type type, HashSet<Type> visited)
    {
        if (type == typeof(object) || type.Namespace?.StartsWith("Aydsko.iRacingData", StringComparison.Ordinal) == true)
            return true;
        if (!visited.Add(type)) return false;
        if (type.IsArray) return ContainsSdkOrUntypedValue(type.GetElementType()!, visited);
        if (type.IsGenericType && type.GetGenericArguments().Any(t => ContainsSdkOrUntypedValue(t, visited)))
            return true;
        return type.Namespace?.StartsWith("ApexRacers.", StringComparison.Ordinal) == true
            && type.GetProperties(BindingFlags.Instance | BindingFlags.Public)
                .Any(p => ContainsSdkOrUntypedValue(p.PropertyType, visited));
    }
}
