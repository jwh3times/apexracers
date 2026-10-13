using System.Reflection;
using System.Text.Json;

if (args.Length != 4) throw new ArgumentException("Usage: RealCacheWarm <diagnostic.dll> <order> <observations.json> <warm.json>");
var diagnostic = Assembly.LoadFrom(Path.GetFullPath(args[0]));
var api = Assembly.Load("ApexRacers.Api");
var service = api.GetType("ApexRacers.Api.Services.AdminService", throwOnError: true)!;
var cache = service.GetNestedType("<>O", BindingFlags.NonPublic)!;
var field = cache.GetField("<0>__ToDto", BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Static)!;
var mapper = service.GetMethod("ToDto", BindingFlags.NonPublic | BindingFlags.Static)!;
var wasNull = field.GetValue(null) is null;
if (!wasNull || field.MetadataToken != 0x040004F1) throw new InvalidOperationException("Canonical cache identity/state differs");
field.SetValue(null, mapper.CreateDelegate(field.FieldType));
File.WriteAllText(args[3], JsonSerializer.Serialize(new { fieldToken = field.MetadataToken, fieldType = field.FieldType.ToString(), fieldName = field.Name, declaringType = field.DeclaringType!.FullName, wasNull, nowNonNull = field.GetValue(null) is not null, mapperToken = mapper.MetadataToken }));
var result = diagnostic.EntryPoint!.Invoke(null, [new[] { "sqlite-strict", args[2], args[1] }]);
if (result is Task task) await task;
if (result is int code && code != 0) throw new InvalidOperationException($"Diagnostic exited {code}");
