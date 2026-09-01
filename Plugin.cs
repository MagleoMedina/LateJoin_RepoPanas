using System.Linq;
using BepInEx;
using BepInEx.Logging;
using HarmonyLib;
using RepoPanas_mod.Patches;

namespace RepoPanas_mod;

[BepInPlugin("repopanas.repomods", "RepoPanas_mod", "1.0.0")]
[BepInDependency("zichen.latejoinnow", BepInDependency.DependencyFlags.SoftDependency)]
public class Plugin : BaseUnityPlugin
{
    internal static Plugin Instance { get; private set; }
    internal new static ManualLogSource Logger => Instance._logger;
    private ManualLogSource _logger => base.Logger;
    internal Harmony Harmony { get; set; }

    private void Awake()
    {
        Instance = this;

        Logger.LogInfo("[RepoPanas_mod] Awake — Plugin starting.");

        StatsPersistence.Initialize(Paths.PluginPath);
        StatsPersistence.Log("PLUGIN", "=== Plugin.Awake ===");
        StatsPersistence.Log("PLUGIN", $"PluginPath={Paths.PluginPath}");
        StatsPersistence.Log("PLUGIN", $"GUID={Info.Metadata.GUID} Name={Info.Metadata.Name} Version={Info.Metadata.Version}");
        StatsPersistence.Log("PLUGIN", ">>> v3.0 — NetworkedEvent sync with runtime fields <<<");

        Patch();
        StatsSyncPatch.RegisterNetworkedEvents();
    }

    internal void Patch()
    {
        StatsPersistence.Log("PLUGIN", "=== Patching ===");

        Harmony ??= new Harmony(Info.Metadata.GUID);
        StatsPersistence.Log("PLUGIN", $"Harmony GUID={Info.Metadata.GUID}");

        Harmony.PatchAll();

        int patchCount = Harmony.GetPatchedMethods().Count();
        StatsPersistence.Log("PLUGIN", $"Harmony patches applied: {patchCount}");

        Logger.LogInfo($"[RepoPanas_mod] Patched {patchCount} methods.");
    }
}
