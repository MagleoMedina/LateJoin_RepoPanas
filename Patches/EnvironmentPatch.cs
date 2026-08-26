using HarmonyLib;
using Photon.Pun;

namespace RepoPanas_mod.Patches;

[HarmonyPatch]
internal static class EnvironmentPatch
{
    private static bool _disconnected;

    [HarmonyPostfix]
    [HarmonyPatch(typeof(NetworkManager), "OnDisconnected")]
    private static void OnDisconnectedPostfix()
    {
        _disconnected = true;
    }

    [HarmonyPostfix]
    [HarmonyPatch(typeof(NetworkConnect), "OnJoinedRoom")]
    private static void OnJoinedRoomPostfix()
    {
        _disconnected = false;
    }

    [HarmonyPrefix]
    [HarmonyPatch(typeof(EnvironmentDirector), "AmbientLightLogic")]
    private static bool AmbientLightLogicPrefix(EnvironmentDirector __instance)
    {
        if (__instance == null || _disconnected) return false;
        return true;
    }
}
