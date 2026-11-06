using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.Loader;

if (args.Length != 2)
{
    throw new ArgumentException("Usage: Compatibility <game-data-directory> <Undo.dll>");
}

var gameData = Path.GetFullPath(args[0]);
AssemblyLoadContext.Default.Resolving += (_, name) =>
{
    var path = Path.Combine(gameData, name.Name + ".dll");
    return File.Exists(path) ? AssemblyLoadContext.Default.LoadFromAssemblyPath(path) : null;
};

// Resolves the mod's private game members, and fails if the game has renamed any of them.
var mod = AssemblyLoadContext.Default.LoadFromAssemblyPath(Path.GetFullPath(args[1]));
RuntimeHelpers.RunClassConstructor(mod.GetType("Undo.UndoService", throwOnError: true)!.TypeHandle);
RuntimeHelpers.RunClassConstructor(mod.GetType("Undo.RecordInitialStatePatch", throwOnError: true)!.TypeHandle);

var harmonyAssembly = AssemblyLoadContext.Default.LoadFromAssemblyPath(Path.Combine(gameData, "0Harmony.dll"));
var harmonyType = harmonyAssembly.GetType("HarmonyLib.Harmony", throwOnError: true)!;
var harmony = Activator.CreateInstance(harmonyType, "undo.compatibility")!;
harmonyType.GetMethod("PatchAll", [typeof(Assembly)])!.Invoke(harmony, [mod]);

var game = AssemblyLoadContext.Default.LoadFromAssemblyPath(Path.Combine(gameData, "sts2.dll"));
var patched = harmonyType.GetMethod("GetAllPatchedMethods")!.Invoke(null, null) as IEnumerable<MethodBase>;
var names = patched!.Select(method => $"{method.DeclaringType!.Name}.{method.Name}").ToHashSet();
foreach (var expected in new[]
{
    "RunSaveManager.SaveRun",
    "CombatReplayWriter.RecordInitialState",
    "ActionQueueSet.EnqueueWithoutSynchronizing",
    "SaveManager.DeleteCurrentRun",
    "NHotkeyManager._UnhandledInput",
    "NTopBar.Initialize"
})
{
    if (!names.Contains(expected))
    {
        throw new InvalidOperationException($"Harmony did not patch {expected}.");
    }
    Console.WriteLine($"ok: {expected}");
}

// The replay service must satisfy the game's interface, so it can't have drifted.
var serviceInterface = game.GetType("MegaCrit.Sts2.Core.Multiplayer.Game.INetGameService", throwOnError: true)!;
if (!serviceInterface.IsAssignableFrom(mod.GetType("Undo.UndoNetService", throwOnError: true)))
{
    throw new InvalidOperationException("UndoNetService does not implement INetGameService.");
}

Console.WriteLine("All compatibility checks passed.");
