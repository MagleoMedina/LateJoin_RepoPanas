#nullable enable
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
    private static FieldInfo? _pcEnergyField;
    private static bool _energyFieldResolved;

    static StatsSyncPatch()
    {
        ResolveEnergyField();
    }

    private static void ResolveEnergyField()
    {
        if (_energyFieldResolved) return;
        _energyFieldResolved = true;

        var candidates = new[] { "energy", "playerEnergy", "Energy", "stamina", "currentEnergy", "staminaEnergy" };
        foreach (var name in candidates)
        {
            try
            {
                var f = AccessTools.Field(typeof(PlayerController), name);
                if (f != null)
                {
                    _pcEnergyField = f;
                    StatsPersistence.Log("INIT", $"Energy field resolved: {name} (type={f.FieldType.Name})");
                    Plugin.Logger.LogInfo($"[STATS_SYNC] Energy field resolved: {name}");
                    return;
                }
            }
            catch { }
        }
        StatsPersistence.Log("INIT", "WARNING: Could not resolve any PlayerController energy field.");
        Plugin.Logger.LogWarning("[STATS_SYNC] Could not resolve PlayerController energy field");
    }

    internal static string? GetSteamId(Player player)
    {
        if (player == null)
        {
            StatsPersistence.Log("STEAM_ID", "ERROR: player is null");
            return null;
        }

        if (player.CustomProperties == null)
        {
            StatsPersistence.Log("STEAM_ID", $"ERROR: CustomProperties is null for player={player.NickName}");
            return null;
        }

        var keys = new[] { "steamID", "SteamID", "steamId", "SteamId", "STEAMID", "SteamIDLong", "steam_id", "steamid" };
        foreach (var key in keys)
        {
            if (player.CustomProperties.TryGetValue(key, out var prop))
            {
                StatsPersistence.Log("STEAM_ID", $"Found key={key} value={prop} type={prop?.GetType().Name}");
                if (prop is ulong u) return u.ToString();
                if (prop is long l) return l.ToString();
                if (prop is int i) return i.ToString();
                if (prop is string s && !string.IsNullOrEmpty(s)) return s;
            }
        }

        if (!string.IsNullOrEmpty(player.UserId))
        {
            StatsPersistence.Log("STEAM_ID", $"Fallback to UserId={player.UserId}");
            return player.UserId;
        }

        StatsPersistence.Log("STEAM_ID", $"ERROR: No SteamID found for player={player.NickName}. Props: {DebugPlayerProps(player)}");
        return null;
    }

    internal static string? GetLocalSteamId()
    {
        var player = PhotonNetwork.LocalPlayer;
        if (player == null)
        {
            StatsPersistence.Log("STEAM_ID", "ERROR: LocalPlayer is null");
            return null;
        }
        return GetSteamId(player);
    }

    private static string DebugPlayerProps(Player player)
    {
        if (player?.CustomProperties == null) return "{}";
        var parts = new List<string>();
        foreach (var key in player.CustomProperties.Keys)
        {
            var val = player.CustomProperties[key];
            parts.Add($"{key}={val}({val?.GetType().Name})");
        }
        return $"{{{string.Join(", ", parts)}}}";
    }

    private static void SaveCurrentStats(string steamId, PlayerAvatar avatar)
    {
        try
        {
            if (StatsManager.instance == null)
            {
                StatsPersistence.Log("PATCH", "[SaveCurrentStats] StatsManager.instance is null, skipping save.");
                return;
            }

            int health = StatsManager.instance.GetPlayerHealth(steamId);
            StatsPersistence.Log("PATCH", $"[SaveCurrentStats] Health read: {health}");

            float energy = 0;
            if (_pcEnergyField != null && PlayerController.instance != null)
            {
                try
                {
                    energy = (float)_pcEnergyField.GetValue(PlayerController.instance);
                    StatsPersistence.Log("PATCH", $"[SaveCurrentStats] Energy read: {energy:F1}");
                }
                catch (Exception ex)
                {
                    StatsPersistence.Log("PATCH", $"[SaveCurrentStats] ERROR reading energy: {ex.Message}");
                }
            }
            else
            {
                StatsPersistence.Log("PATCH", $"[SaveCurrentStats] Energy field null={_pcEnergyField == null} PC.instance null={PlayerController.instance == null}");
            }

            var upgrades = new Dictionary<string, int>();
            if (StatsManager.instance.dictionaryOfDictionaries != null)
            {
                foreach (string key in PlayerStatsData.UpgradeKeys)
                {
                    try
                    {
                        if (StatsManager.instance.dictionaryOfDictionaries.TryGetValue(key, out var dict)
                            && dict.TryGetValue(steamId, out int val))
                        {
                            upgrades[key] = val;
                        }
                        else
                        {
                            upgrades[key] = 0;
                        }
                    }
                    catch
                    {
                        upgrades[key] = 0;
                    }
                }
                StatsPersistence.Log("PATCH", $"[SaveCurrentStats] Upgrades collected: {upgrades.Count}");
            }
            else
            {
                StatsPersistence.Log("PATCH", "[SaveCurrentStats] WARNING: dictionaryOfDictionaries is null");
            }

            var data = new PlayerStatsData
            {
                SteamId = steamId,
                PlayerName = PhotonNetwork.LocalPlayer?.NickName ?? "Unknown",
                Health = health,
                Energy = energy,
                Upgrades = upgrades,
                Timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
            };

            StatsPersistence.Save(data);
            Plugin.Logger.LogInfo($"[STATS_SYNC] Saved checkpoint for {steamId}: Health={health} Energy={energy:F1} Upgrades={upgrades.Count}");
        }
        catch (Exception ex)
        {
            StatsPersistence.Log("PATCH", $"[SaveCurrentStats] FATAL ERROR: {ex.Message}\n{ex.StackTrace}");
            Plugin.Logger.LogWarning($"[STATS_SYNC] Failed to save checkpoint for {steamId}: {ex.Message}");
        }
    }

    [HarmonyPatch(typeof(PlayerAvatar), "Start")]
    [HarmonyPostfix]
    private static void OnPlayerAvatarStart(PlayerAvatar __instance)
    {
        if (!PhotonNetwork.InRoom)
        {
            StatsPersistence.Log("PATCH", "[OnPlayerAvatarStart] Not in room, skipping.");
            return;
        }

        if (__instance == null || __instance.photonView == null)
        {
            StatsPersistence.Log("PATCH", "[OnPlayerAvatarStart] ERROR: __instance or photonView is null");
            return;
        }

        bool isMine = __instance.photonView.IsMine;
        StatsPersistence.Log("PATCH", $"[OnPlayerAvatarStart] IsMine={isMine} Spawned={__instance.spawned}");

        if (!isMine) return;

        string? mySteamId = GetLocalSteamId();
        if (string.IsNullOrEmpty(mySteamId))
        {
            StatsPersistence.Log("PATCH", "[OnPlayerAvatarStart] ERROR: Could not get local SteamId");
            return;
        }

        StatsPersistence.Log("PATCH", $"[OnPlayerAvatarStart] Local SteamId={mySteamId}");

        PlayerStatsData? savedData = StatsPersistence.Load(mySteamId);
        if (savedData != null)
        {
            StatsPersistence.Log("PATCH", $"[OnPlayerAvatarStart] Found saved checkpoint. Health={savedData.Health} Energy={savedData.Energy:F1} Upgrades={savedData.Upgrades.Count}. Starting restore...");
            Plugin.Logger.LogInfo($"[STATS_SYNC] Reconnect detected for {mySteamId}. Restoring stats...");
            Plugin.Instance.StartCoroutine(RestoreStatsWithRetry(__instance, mySteamId, savedData));
        }
        else
        {
            StatsPersistence.Log("PATCH", "[OnPlayerAvatarStart] No saved checkpoint. Saving current stats as checkpoint...");
            SaveCurrentStats(mySteamId, __instance);
        }
    }

    private static IEnumerator RestoreStatsWithRetry(PlayerAvatar avatar, string steamId, PlayerStatsData data)
    {
        int maxAttempts = 6;
        float[] delays = [0f, 0.5f, 1f, 2f, 3f, 5f];

        bool healthRestored = false;
        bool energyRestored = false;
        int upgradesRestored = 0;
        int totalUpgrades = data.Upgrades.Count;

        for (int attempt = 0; attempt < maxAttempts; attempt++)
        {
            StatsPersistence.Log("RESTORE", $"--- Attempt {attempt + 1}/{maxAttempts} ---");

            if (attempt > 0)
                yield return new WaitForSeconds(delays[attempt] - delays[attempt - 1]);

            // Restore Health
            if (!healthRestored)
            {
                try
                {
                    if (StatsManager.instance == null)
                    {
                        StatsPersistence.Log("RESTORE", "Health: StatsManager.instance is null, waiting...");
                        continue;
                    }

                    int currentHealth = StatsManager.instance.GetPlayerHealth(steamId);
                    StatsPersistence.Log("RESTORE", $"Health: current={currentHealth} target={data.Health}");

                    if (currentHealth != data.Health)
                    {
                        StatsManager.instance.SetPlayerHealth(steamId, data.Health, true);

                        int verifyHealth = StatsManager.instance.GetPlayerHealth(steamId);
                        StatsPersistence.Log("RESTORE", $"Health: after set={verifyHealth} (expected={data.Health})");

                        if (verifyHealth == data.Health)
                        {
                            healthRestored = true;
                            StatsPersistence.Log("RESTORE", "Health: RESTORED OK");
                        }
                        else
                        {
                            StatsPersistence.Log("RESTORE", "Health: restore mismatch, will retry");
                        }
                    }
                    else
                    {
                        healthRestored = true;
                        StatsPersistence.Log("RESTORE", "Health: already correct");
                    }
                }
                catch (Exception ex)
                {
                    StatsPersistence.Log("RESTORE", $"Health: ERROR {ex.Message}");
                }
            }

            // Restore Energy
            if (!energyRestored && _pcEnergyField != null)
            {
                try
                {
                    var pc = PlayerController.instance;
                    if (pc != null && data.Energy > 0)
                    {
                        float currentEnergy = (float)_pcEnergyField.GetValue(pc);
                        StatsPersistence.Log("RESTORE", $"Energy: current={currentEnergy:F1} target={data.Energy:F1}");

                        if (Mathf.Abs(currentEnergy - data.Energy) > 0.1f)
                        {
                            _pcEnergyField.SetValue(pc, data.Energy);

                            float verifyEnergy = (float)_pcEnergyField.GetValue(pc);
                            StatsPersistence.Log("RESTORE", $"Energy: after set={verifyEnergy:F1} (expected={data.Energy:F1})");

                            if (Mathf.Abs(verifyEnergy - data.Energy) < 0.1f)
                            {
                                energyRestored = true;
                                StatsPersistence.Log("RESTORE", "Energy: RESTORED OK");
                            }
                            else
                            {
                                StatsPersistence.Log("RESTORE", "Energy: restore mismatch, will retry");
                            }
                        }
                        else
                        {
                            energyRestored = true;
                            StatsPersistence.Log("RESTORE", "Energy: already correct");
                        }
                    }
                    else
                    {
                        energyRestored = true;
                        StatsPersistence.Log("RESTORE", $"Energy: skipped (PC.instance null={pc == null} energy={data.Energy:F1})");
                    }
                }
                catch (Exception ex)
                {
                    StatsPersistence.Log("RESTORE", $"Energy: ERROR {ex.Message}");
                }
            }
            else if (!energyRestored && _pcEnergyField == null)
            {
                energyRestored = true;
                StatsPersistence.Log("RESTORE", "Energy: skipped (field not resolved)");
            }

            // Restore Upgrades
            if (StatsManager.instance?.dictionaryOfDictionaries != null)
            {
                foreach (var kvp in data.Upgrades)
                {
                    try
                    {
                        if (StatsManager.instance.dictionaryOfDictionaries.TryGetValue(kvp.Key, out var dict))
                        {
                            int currentVal = dict.TryGetValue(steamId, out int cv) ? cv : 0;
                            if (currentVal != kvp.Value)
                            {
                                dict[steamId] = kvp.Value;
                                StatsPersistence.Log("RESTORE", $"Upgrade {kvp.Key}: {currentVal} -> {kvp.Value}");
                            }
                            else
                            {
                                StatsPersistence.Log("RESTORE", $"Upgrade {kvp.Key}: already correct ({currentVal})");
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        StatsPersistence.Log("RESTORE", $"Upgrade {kvp.Key}: ERROR {ex.Message}");
                    }
                }
                upgradesRestored = totalUpgrades;
            }

            if (healthRestored && energyRestored)
            {
                StatsPersistence.Log("RESTORE", $"=== RESTORE COMPLETE on attempt {attempt + 1} ===");
                StatsPersistence.Log("RESTORE", $"Health={healthRestored} Energy={energyRestored} Upgrades={upgradesRestored}/{totalUpgrades}");
                Plugin.Logger.LogInfo($"[STATS_SYNC] Stats restored for {steamId}: Health={healthRestored} Energy={energyRestored} Upgrades={upgradesRestored}/{totalUpgrades}");
                yield break;
            }

            StatsPersistence.Log("RESTORE", $"Attempt {attempt + 1} incomplete. Health={healthRestored} Energy={energyRestored}. Retrying...");
        }

        StatsPersistence.Log("RESTORE", $"=== RESTORE FINAL: Health={healthRestored} Energy={energyRestored} Upgrades={upgradesRestored}/{totalUpgrades} ===");
        Plugin.Logger.LogWarning($"[STATS_SYNC] Restore incomplete after {maxAttempts} attempts for {steamId}");
    }
}
