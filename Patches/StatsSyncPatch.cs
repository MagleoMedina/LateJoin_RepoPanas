using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using Photon.Pun;
using Photon.Realtime;
using UnityEngine;

namespace RepoPanas_mod.Patches;

[HarmonyPatch]
internal static class StatsSyncPatch
{
    private static readonly Dictionary<string, PlayerStats> _savedStats = new Dictionary<string, PlayerStats>();
    private static FieldInfo _pcEnergyField;

    private struct PlayerStats
    {
        public int Health;
        public float Energy;
    }

    static StatsSyncPatch()
    {
        ResolveEnergyField();
    }

    private static void ResolveEnergyField()
    {
        var candidates = new[] { "energy", "playerEnergy", "Energy", "stamina", "currentEnergy" };
        foreach (var name in candidates)
        {
            try
            {
                var f = AccessTools.Field(typeof(PlayerController), name);
                if (f != null)
                {
                    _pcEnergyField = f;
                    Plugin.Logger.LogInfo($"PlayerController energy field resolved: {name}");
                    return;
                }
            }
            catch { }
        }
        Plugin.Logger.LogWarning("Could not resolve PlayerController energy field");
    }

    internal static string GetSteamId(Player player)
    {
        if (player?.CustomProperties == null) return null;
        var keys = new[] { "steamID", "SteamID", "steamId", "SteamId", "STEAMID", "SteamIDLong", "steam_id", "steamid" };
        foreach (var key in keys)
        {
            if (player.CustomProperties.TryGetValue(key, out var prop))
            {
                if (prop is ulong u) return u.ToString();
                if (prop is long l) return l.ToString();
                if (prop is string s && !string.IsNullOrEmpty(s)) return s;
            }
        }
        return string.IsNullOrEmpty(player.UserId) ? null : player.UserId;
    }

    private static string GetLocalSteamId()
    {
        var player = PhotonNetwork.LocalPlayer;
        if (player == null) return null;
        return GetSteamId(player);
    }

    [HarmonyPatch(typeof(NetworkManager), "OnPlayerLeftRoom")]
    [HarmonyPostfix]
    private static void OnPlayerLeftRoom(Player otherPlayer)
    {
        if (!PhotonNetwork.IsMasterClient) return;

        var steamId = GetSteamId(otherPlayer);
        if (string.IsNullOrEmpty(steamId)) return;

        try
        {
            var health = StatsManager.instance.GetPlayerHealth(steamId);
            float energy = 0;

            if (_pcEnergyField != null && PlayerController.instance != null)
                energy = (float)_pcEnergyField.GetValue(PlayerController.instance);

            _savedStats[steamId] = new PlayerStats { Health = health, Energy = energy };
        }
        catch (Exception ex)
        {
            Plugin.Logger.LogWarning($"StatsSync: Failed to save stats for {steamId}: {ex.Message}");
        }
    }

    [HarmonyPatch(typeof(PlayerAvatar), "Start")]
    [HarmonyPostfix]
    private static void OnPlayerAvatarStart(PlayerAvatar __instance)
    {
        if (!PhotonNetwork.InRoom) return;
        if (__instance == null || __instance.photonView == null) return;

        if (__instance.photonView.IsMine)
        {
            var mySteamId = GetLocalSteamId();
            if (string.IsNullOrEmpty(mySteamId)) return;

            if (_savedStats.TryGetValue(mySteamId, out var savedData))
            {
                Plugin.Instance.StartCoroutine(RestoreStatsDelayed(__instance, mySteamId, savedData));
            }
        }
    }

    private static IEnumerator RestoreStatsDelayed(PlayerAvatar avatar, string steamId, PlayerStats data)
    {
        yield return new WaitForSeconds(0.5f);

        try
        {
            StatsManager.instance.SetPlayerHealth(steamId, data.Health, true);
            Plugin.Logger.LogInfo($"StatsSync: Restored health={data.Health} for {steamId}");
        }
        catch (Exception ex)
        {
            Plugin.Logger.LogWarning($"StatsSync: Failed to restore health: {ex.Message}");
        }

        if (_pcEnergyField != null)
        {
            try
            {
                var pc = PlayerController.instance;
                if (pc != null && data.Energy > 0)
                {
                    _pcEnergyField.SetValue(pc, data.Energy);
                    Plugin.Logger.LogInfo($"StatsSync: Restored energy={data.Energy} for {steamId}");
                }
            }
            catch (Exception ex)
            {
                Plugin.Logger.LogWarning($"StatsSync: Failed to restore energy: {ex.Message}");
            }
        }

        _savedStats.Remove(steamId);
    }
}
