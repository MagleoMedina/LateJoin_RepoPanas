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
        StatsPersistence.Log("PATCH", "[OnDisconnected] _disconnected set to TRUE");
    }

    [HarmonyPostfix]
    [HarmonyPatch(typeof(NetworkConnect), "OnJoinedRoom")]
    private static void OnJoinedRoomPostfix()
    {
        _disconnected = false;
        StatsPersistence.Log("PATCH", $"[OnJoinedRoom] _disconnected set to FALSE. Room={PhotonNetwork.CurrentRoom?.Name} Players={PhotonNetwork.CurrentRoom?.PlayerCount}");
    }

    [HarmonyPrefix]
    [HarmonyPatch(typeof(EnvironmentDirector), "AmbientLightLogic")]
    private static bool AmbientLightLogicPrefix(EnvironmentDirector __instance)
    {
        if (__instance == null || _disconnected)
        {
            StatsPersistence.Log("PATCH", $"[AmbientLightLogic] BLOCKED. instance={__instance != null} disconnected={_disconnected}");
            return false;
        }

        StatsPersistence.Log("PATCH", $"[AmbientLightLogic] Allowed. __instance={__instance.gameObject.name}");
        return true;
    }
}
