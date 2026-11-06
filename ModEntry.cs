using System.Runtime.CompilerServices;
using HarmonyLib;
using MegaCrit.Sts2.Core.Modding;

namespace Undo;

[ModInitializer(nameof(Initialize))]
public static class ModEntry
{
    public static void Initialize()
    {
        // Resolve the private game members up front so a game update fails at load, not mid-undo.
        RuntimeHelpers.RunClassConstructor(typeof(UndoService).TypeHandle);
        RuntimeHelpers.RunClassConstructor(typeof(RecordInitialStatePatch).TypeHandle);
        new Harmony("james.sts2.undo").PatchAll(typeof(ModEntry).Assembly);
    }
}
