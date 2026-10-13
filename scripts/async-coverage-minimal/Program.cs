using System.Reflection;
using System.Reflection.Emit;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Runtime.CompilerServices;
using System.Text.Json;

if (args.Length != 3 || args[0] is not ("cold" or "warm" or "fresh" or "inspect") || args[1] is not ("role-first" or "user-first"))
    throw new ArgumentException("Usage: Minimal <cold|warm|fresh|inspect> <role-first|user-first> <observations.json>");
var mode = args[0];
var order = args[1];
var observations = new List<object>();
if (mode != "inspect")
{
    if (mode == "warm") Cached.Warm();
    var methods = order == "role-first" ? new[] { "role", "user" } : new[] { "user", "role" };
    for (var i = 0; i < 16; i++)
        foreach (var method in methods)
        {
            var task = mode == "fresh"
                ? method == "role" ? Fresh.RoleAsync("Standard") : Fresh.UserAsync(Guid.Empty)
                : method == "role" ? Cached.RoleAsync("Standard") : Cached.UserAsync(Guid.Empty);
            var completed = task.IsCompleted;
            var result = await task;
            if (result.Count != 1 || result[0].Key != "flag") throw new InvalidOperationException("Unexpected wrapper result");
            observations.Add(new { iteration = i, method, completedOnReturn = completed, count = result.Count });
        }
}
var mappings = new List<object>();
if (mode == "inspect")
{
    using var stream = File.OpenRead(Path.ChangeExtension(Assembly.GetExecutingAssembly().Location, ".pdb"));
    using var provider = MetadataReaderProvider.FromPortablePdbStream(stream);
    var reader = provider.GetMetadataReader();
    var codes = typeof(OpCodes).GetFields(BindingFlags.Public | BindingFlags.Static).Select(x => (OpCode)x.GetValue(null)!).ToDictionary(x => unchecked((ushort)x.Value));
    foreach (var type in new[] { typeof(Cached), typeof(Fresh) })
        foreach (var method in type.GetMethods(BindingFlags.Public | BindingFlags.Static))
        {
            var state = method.GetCustomAttribute<AsyncStateMachineAttribute>();
            var target = state?.StateMachineType.GetMethod("MoveNext", BindingFlags.NonPublic | BindingFlags.Instance) ?? method;
            var bytes = target.GetMethodBody()!.GetILAsByteArray()!;
            var instructions = new List<object>();
            for (var offset = 0; offset < bytes.Length;)
            {
                var start = offset;
                ushort value = bytes[offset++];
                if (value == 0xfe) value = (ushort)(0xfe00 | bytes[offset++]);
                var code = codes[value];
                var length = code.OperandType switch
                {
                    OperandType.InlineNone => 0,
                    OperandType.ShortInlineBrTarget or OperandType.ShortInlineI or OperandType.ShortInlineVar => 1,
                    OperandType.InlineVar => 2,
                    OperandType.InlineI8 or OperandType.InlineR => 8,
                    OperandType.InlineSwitch => 4 + 4 * BitConverter.ToInt32(bytes, offset),
                    _ => 4
                };
                string? resolved = null;
                if (code.OperandType is OperandType.InlineField or OperandType.InlineMethod or OperandType.InlineType or OperandType.InlineTok)
                {
                    var token = BitConverter.ToInt32(bytes, offset);
                    resolved = target.Module.ResolveMember(token)?.ToString();
                }
                instructions.Add(new { offset = start, opcode = code.Name, operandHex = Convert.ToHexString(bytes.AsSpan(offset, length)), resolved });
                offset += length;
            }
            var handle = MetadataTokens.MethodDebugInformationHandle(target.MetadataToken & 0xffffff);
            var points = reader.GetMethodDebugInformation(handle).GetSequencePoints().Select(p => new { p.Offset, p.StartLine, p.EndLine, p.StartColumn, p.EndColumn, p.IsHidden }).ToArray();
            mappings.Add(new { type = type.FullName, method = method.Name, token = target.MetadataToken, byteLength = bytes.Length, ilHex = Convert.ToHexString(bytes), instructions, sequencePoints = points });
        }
}
File.WriteAllText(args[2], JsonSerializer.Serialize(new { mode, order, runtime = Environment.Version.ToString(), observations, mappings }, new JsonSerializerOptions { WriteIndented = true }));

record Flag(string Key);
record Dto(string Key);

static class Cached
{
    static Dto ToDto(Flag flag) => new(flag.Key);
    public static void Warm() => new[] { new Flag("flag") }.Select(ToDto).ToList();
    public static async Task<List<Dto>> RoleAsync(string role)
    {
        var flags = await Task.FromResult(new[] { new Flag("flag") });
        return flags.Select(ToDto).ToList();
    }
    public static async Task<List<Dto>> UserAsync(Guid user)
    {
        var flags = await Task.FromResult(new[] { new Flag("flag") });
        return flags.Select(ToDto).ToList();
    }
}

static class Fresh
{
    static Dto ToDto(Flag flag) => new(flag.Key);
    public static async Task<List<Dto>> RoleAsync(string role)
    {
        var flags = await Task.FromResult(new[] { new Flag("flag") });
        return flags.Select(new Func<Flag, Dto>(ToDto)).ToList();
    }
    public static async Task<List<Dto>> UserAsync(Guid user)
    {
        var flags = await Task.FromResult(new[] { new Flag("flag") });
        return flags.Select(new Func<Flag, Dto>(ToDto)).ToList();
    }
}
