using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;
using DfE.CheckPerformanceData.Web.Controllers;
using DfE.CheckPerformanceData.Web.Extensions;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Routing;

namespace DfE.CheckPerformanceData.Application.UnitTests.Web;

// #615 (OWASP A04): every response that carries personal data is stamped with the one shared
// NoStore() helper. This guard inspects the compiled Web assembly rather than counting text, so a
// call written in a comment cannot satisfy it, a call on the wrong action cannot hide, and adding
// a download does not mean editing a tally:
//   - every method that builds a file response must carry [RequiresNoStore] or be listed below
//     with the reason it needs no header;
//   - every [RequiresNoStore] method must call NoStore() exactly once;
//   - every IActionResult implementation (the streamed CSV writers) must call it too;
//   - an action whose route says "download" must carry the marker;
//   - the "no-store" literal must be loaded by no method other than the helper.
public sealed class NoStoreDownloadSourceGuardTests
{
    private static readonly Assembly WebAssembly = typeof(HttpResponseExtensions).Assembly;
    private static readonly MethodInfo NoStoreMethod =
        typeof(HttpResponseExtensions).GetMethod(nameof(HttpResponseExtensions.NoStore))!;

    // File responses that deliberately do not carry personal data. The reason lives here so a
    // reader can tell an exclusion from an oversight.
    private static readonly Dictionary<string, string> FileResponsesWithoutNoStore = new()
    {
        [$"{typeof(ContentStagingController).FullName}.ExportFile"] =
            "exports a content bundle (pages, blocks, banners) for another environment; no personal data",
        [Describe<ObservabilityController>(nameof(ObservabilityController.Export))] =
            "pipeline throughput and latency metrics for the admin dashboard; no personal data",
    };

    // Personal data can leave in a page rather than a file. Those actions cannot be discovered
    // automatically, so they are named here and must keep the marker (and the helper call).
    private static readonly string[] PersonalDataWithoutFileResponse =
    [
        Describe<StorageAdminController>(nameof(StorageAdminController.Preview)),
    ];

    // Result types that write the response themselves are missed by the File(...) discovery.
    // None is exempt today; add one here with its reason if that changes.
    private static readonly Dictionary<string, string> ResultsWithoutNoStore = new();

    // Download routes on actions that do not call the helper themselves. Coverage must still
    // exist somewhere the other guard tests reach, and the reason says where.
    private static readonly Dictionary<string, string> DownloadRoutesWithoutNoStore = new()
    {
        [Describe<AppLogsController>(nameof(AppLogsController.Download))] =
            "returns a StreamedCsvResult, which calls the helper in ExecuteResultAsync; asserted by "
            + nameof(Every_streamed_result_type_calls_the_helper_exactly_once),
    };

    [Fact]
    public void Every_file_response_is_marked_or_deliberately_excluded()
    {
        var writers = WebMethods().Where(method => CallsFile(method).Any()).ToList();

        var unclassified = writers
            .Where(method => !IsMarked(method) && !FileResponsesWithoutNoStore.ContainsKey(Describe(method)))
            .Select(Describe)
            .Distinct()
            .Order(StringComparer.Ordinal)
            .ToList();

        Assert.True(unclassified.Count == 0,
            "A file response must carry [RequiresNoStore] or be listed in FileResponsesWithoutNoStore:"
            + Environment.NewLine + string.Join(Environment.NewLine, unclassified));

        var known = writers.Select(Describe).ToHashSet(StringComparer.Ordinal);
        var stale = FileResponsesWithoutNoStore.Keys.Where(name => !known.Contains(name)).ToList();
        Assert.True(stale.Count == 0,
            "Stale exclusion(s) — the method no longer produces a file response:"
            + Environment.NewLine + string.Join(Environment.NewLine, stale));
    }

    [Fact]
    public void Every_download_route_is_marked_or_deliberately_excluded()
    {
        var downloadActions = WebMethods()
            .Where(method => RouteTemplates(method).Any(route => route.Contains("download", StringComparison.OrdinalIgnoreCase)))
            .ToList();

        Assert.NotEmpty(downloadActions);

        var unmarked = downloadActions
            .Where(method => !IsMarked(method) && !DownloadRoutesWithoutNoStore.ContainsKey(Describe(method)))
            .Select(Describe)
            .Distinct()
            .Order(StringComparer.Ordinal)
            .ToList();

        Assert.True(unmarked.Count == 0,
            "A download route must carry [RequiresNoStore] or be listed in DownloadRoutesWithoutNoStore:"
            + Environment.NewLine + string.Join(Environment.NewLine, unmarked));
    }

    [Fact]
    public void Every_marked_action_calls_the_helper_exactly_once()
    {
        var marked = WebMethods().Where(IsMarked).ToList();

        Assert.NotEmpty(marked);
        var failures = marked
            .Select(method => (Name: Describe(method), Calls: CountCalls(method, NoStoreMethod)))
            .Where(result => result.Calls != 1)
            .Select(result => $"{result.Name}: expected exactly one call to NoStore(), found {result.Calls}")
            .ToList();

        Assert.True(failures.Count == 0, string.Join(Environment.NewLine, failures));
    }

    [Fact]
    public void Every_personal_data_page_is_marked()
    {
        var missing = PersonalDataWithoutFileResponse
            .Where(name => !WebMethods().Any(method => IsMarked(method) && Describe(method) == name))
            .ToList();

        Assert.True(missing.Count == 0,
            "These responses carry personal data but lost [RequiresNoStore]:"
            + Environment.NewLine + string.Join(Environment.NewLine, missing));
    }

    [Fact]
    public void Every_streamed_result_type_calls_the_helper_exactly_once()
    {
        var resultTypes = WebAssembly.GetTypes()
            .Where(type => type is { IsClass: true, IsAbstract: false, ContainsGenericParameters: false })
            .Where(type => typeof(IActionResult).IsAssignableFrom(type))
            .Where(type => !type.Name.StartsWith('<'))
            .ToList();

        Assert.NotEmpty(resultTypes);
        var failures = new List<string>();
        foreach (var type in resultTypes)
        {
            var name = type.FullName!;
            if (ResultsWithoutNoStore.ContainsKey(name)) continue;

            var execute = type.GetMethod(nameof(IActionResult.ExecuteResultAsync),
                BindingFlags.Public | BindingFlags.Instance);
            Assert.NotNull(execute);
            var calls = CountCalls(execute!, NoStoreMethod);
            if (calls != 1) failures.Add($"{name}: expected exactly one call to NoStore(), found {calls}");
        }

        Assert.True(failures.Count == 0,
            "A result type that writes personal data must call NoStore() exactly once:"
            + Environment.NewLine + string.Join(Environment.NewLine, failures));
    }

    [Fact]
    public void The_no_store_literal_is_written_only_by_the_helper()
    {
        var writers = WebMethods()
            .Where(method => LoadedStrings(method).Contains(HttpResponseExtensions.NoStoreValue, StringComparer.Ordinal))
            .Select(Describe)
            .ToList();

        Assert.Equal(new[] { Describe(NoStoreMethod) }, writers);
    }

    private sealed record Instruction(OpCode OpCode, MethodBase? Method, string? String);

    private static readonly OpCode[] OneByteOpCodes = new OpCode[0x100];
    private static readonly OpCode[] TwoByteOpCodes = new OpCode[0x100];

    static NoStoreDownloadSourceGuardTests()
    {
        foreach (var field in typeof(OpCodes).GetFields(BindingFlags.Public | BindingFlags.Static))
        {
            if (field.GetValue(null) is not OpCode opCode) continue;
            var value = (ushort)opCode.Value;
            if (value < 0x100) OneByteOpCodes[value] = opCode;
            else if ((value & 0xff00) == 0xfe00) TwoByteOpCodes[value & 0xff] = opCode;
        }
    }

    private static string Describe<T>(string method) => $"{typeof(T).FullName}.{method}";

    private static string Describe(MethodBase method) =>
        $"{method.DeclaringType?.FullName}.{method.Name}";

    private static bool IsMarked(MethodBase method) =>
        method.IsDefined(typeof(RequiresNoStoreAttribute), inherit: false);

    private static IEnumerable<MethodBase> WebMethods() =>
        WebAssembly.GetTypes()
            .Where(type => !type.Name.StartsWith('<'))
            .SelectMany(type => type.GetMethods(
                BindingFlags.Public | BindingFlags.NonPublic
                | BindingFlags.Instance | BindingFlags.Static
                | BindingFlags.DeclaredOnly))
            .Where(method => method.DeclaringType is not null);

    private static IEnumerable<string> RouteTemplates(MethodBase method) =>
        method.GetCustomAttributes(inherit: false)
            .Select(attribute => attribute switch
            {
                HttpMethodAttribute http => http.Template,
                RouteAttribute route => route.Template,
                _ => null,
            })
            .Where(template => !string.IsNullOrEmpty(template))
            .Select(template => template!);

    private static IEnumerable<MethodBase> CallsFile(MethodBase method) =>
        Body(method)
            .Where(instruction => instruction.OpCode == OpCodes.Call
                || instruction.OpCode == OpCodes.Callvirt
                || instruction.OpCode == OpCodes.Newobj)
            .Select(instruction => instruction.Method)
            .Where(target => target is not null && IsFileResponse(target))
            .Select(target => target!);

    private static bool IsFileResponse(MethodBase call)
    {
        var declaring = call.DeclaringType;
        if (declaring is null) return false;

        // new FileContentResult(...) / new FileStreamResult(...) and the like.
        if (typeof(FileResult).IsAssignableFrom(declaring)) return true;

        // return File(...) / return PhysicalFile(...) on a controller.
        return declaring == typeof(ControllerBase) && call.Name is nameof(ControllerBase.File) or "PhysicalFile";
    }

    private static int CountCalls(MethodBase method, MethodInfo target) =>
        Body(method).Count(instruction => CallsTarget(instruction, target));

    private static bool CallsTarget(Instruction instruction, MethodInfo target) =>
        (instruction.OpCode == OpCodes.Call || instruction.OpCode == OpCodes.Callvirt)
        && instruction.Method is not null
        && instruction.Method.MetadataToken == target.MetadataToken
        && instruction.Method.Module == target.Module;

    private static IEnumerable<string> LoadedStrings(MethodBase method) =>
        Body(method)
            .Where(instruction => instruction.OpCode == OpCodes.Ldstr && instruction.String is not null)
            .Select(instruction => instruction.String!);

    // An async action's real code lives in the state machine's MoveNext, not in the kickoff stub
    // that reflection returns. Iterators are the same shape.
    private static IEnumerable<Instruction> Body(MethodBase method)
    {
        var stateMachine = method.GetCustomAttribute<AsyncStateMachineAttribute>()?.StateMachineType
            ?? (Type?)method.GetCustomAttribute<IteratorStateMachineAttribute>()?.StateMachineType;

        var moveNext = stateMachine?.GetMethod("MoveNext",
            BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
        return moveNext is not null ? Disassemble(moveNext) : Disassemble(method);
    }

    private static IEnumerable<Instruction> Disassemble(MethodBase method)
    {
        var body = method.GetMethodBody();
        var il = body?.GetILAsByteArray();
        if (il is null) yield break;

        var module = method.Module;
        var offset = 0;
        while (offset < il.Length)
        {
            OpCode opCode;
            if (il[offset] == 0xFE)
            {
                opCode = TwoByteOpCodes[il[offset + 1]];
                offset += 2;
            }
            else
            {
                opCode = OneByteOpCodes[il[offset]];
                offset++;
            }

            MethodBase? called = null;
            string? literal = null;
            if (opCode == OpCodes.Call || opCode == OpCodes.Callvirt || opCode == OpCodes.Newobj)
            {
                try
                {
                    called = module.ResolveMethod(BitConverter.ToInt32(il, offset));
                }
                catch (ArgumentException)
                {
                    // A generic context the reflection API cannot resolve outside the method body;
                    // no in-scope call needs it.
                }
            }
            else if (opCode == OpCodes.Ldstr)
            {
                literal = module.ResolveString(BitConverter.ToInt32(il, offset));
            }

            yield return new Instruction(opCode, called, literal);
            offset += OperandSize(opCode, il, offset);
        }
    }

    private static int OperandSize(OpCode opCode, byte[] il, int at) => opCode.OperandType switch
    {
        OperandType.InlineNone => 0,
        OperandType.ShortInlineI or OperandType.ShortInlineVar
            or OperandType.ShortInlineBrTarget => 1,
        OperandType.InlineVar => 2,
        OperandType.InlineI or OperandType.InlineBrTarget or OperandType.InlineMethod
            or OperandType.InlineField or OperandType.InlineType or OperandType.InlineTok
            or OperandType.InlineString or OperandType.InlineSig
            or OperandType.ShortInlineR => 4,
        OperandType.InlineI8 or OperandType.InlineR => 8,
        OperandType.InlineSwitch => 4 + 4 * BitConverter.ToInt32(il, at),
        _ => throw new NotSupportedException(opCode.OperandType.ToString()),
    };
}
