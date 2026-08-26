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
        Patch();
    }

    internal void Patch()
    {
        Harmony ??= new Harmony(Info.Metadata.GUID);
        Harmony.PatchAll();
    }
}
