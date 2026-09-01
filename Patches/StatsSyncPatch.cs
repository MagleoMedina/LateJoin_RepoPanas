#nullable enable
using System;
using System.Collections.Generic;
using System.Reflection;
using ExitGames.Client.Photon;
using HarmonyLib;
using Photon.Pun;
using Photon.Realtime;
using REPOLib.Modules;
using UnityEngine;
using IEnumerator = System.Collections.IEnumerator;

namespace RepoPanas_mod.Patches;

[HarmonyPatch]
internal static class StatsSyncPatch
{
    private static NetworkedEvent? _requestStatsEvent;
    private static NetworkedEvent? _statsSyncEvent;
    private static NetworkedEvent? _statsConfirmEvent;

    private static readonly HashSet<string> _pendingSyncPlayers = [];
    private static readonly HashSet<string> _syncedPlayers = [];
    private static readonly Dictionary<string, Coroutine> _fallbackTimeouts = [];

    internal static void RegisterNetworkedEvents()
    {
        StatsPersistence.Log("SYNC", "Registering NetworkedEvents...");

        _requestStatsEvent = new NetworkedEvent("RepoPanas_RequestStats", HandleRequestStatsEvent);
        _statsSyncEvent = new NetworkedEvent("RepoPanas_StatsSync", HandleStatsSyncEvent);
        _statsConfirmEvent = new NetworkedEvent("RepoPanas_StatsConfirm", HandleStatsSyncConfirmEvent);

        StatsPersistence.Log("SYNC", $"NetworkedEvents registered. RequestCode={_requestStatsEvent.EventCode} SyncCode={_statsSyncEvent.EventCode} ConfirmCode={_statsConfirmEvent.EventCode}");
    }

    // ═══════════════════════════════════════════════════════
    // HOST SIDE
    // ═══════════════════════════════════════════════════════

    [HarmonyPatch(typeof(NetworkManager), "OnPlayerEnteredRoom")]
    [HarmonyPostfix]
    private static void OnPlayerEnteredRoomPostfix(Player newPlayer)
    {
        StatsPersistence.Log("SYNC_HOST", $"OnPlayerEnteredRoomPostfix ENTRY. newPlayer={newPlayer?.NickName} isMaster={SemiFunc.IsMasterClientOrSingleplayer()}");
        try
        {
            if (!SemiFunc.IsMasterClientOrSingleplayer()) return;

            string? steamId = newPlayer != null ? GetSteamId(newPlayer) : null;
            if (string.IsNullOrEmpty(steamId))
            {
                StatsPersistence.Log("SYNC_HOST", "Could not get steamId for new player, aborting.");
                return;
            }

            if (_syncedPlayers.Contains(steamId))
            {
                StatsPersistence.Log("SYNC_HOST", $"{newPlayer!.NickName} ({steamId}) already synced, skipping.");
                return;
            }

            _pendingSyncPlayers.Add(steamId);
            StatsPersistence.Log("SYNC_HOST", $"Player joined: {newPlayer!.NickName} ({steamId}). Pending sync. Waiting for client request...");
        }
        catch (Exception ex)
        {
            StatsPersistence.Log("SYNC_HOST", $"ERROR: {ex.Message}\n{ex.StackTrace}");
        }
    }

    [HarmonyPatch(typeof(NetworkManager), "OnPlayerLeftRoom")]
    [HarmonyPostfix]
    private static void OnPlayerLeftRoomPostfix(Player otherPlayer)
    {
        StatsPersistence.Log("SYNC_HOST", $"OnPlayerLeftRoomPostfix ENTRY. otherPlayer={otherPlayer?.NickName}");
        try
        {
            string? steamId = otherPlayer != null ? GetSteamId(otherPlayer) : null;
            if (!string.IsNullOrEmpty(steamId))
            {
                _pendingSyncPlayers.Remove(steamId);
                _syncedPlayers.Remove(steamId);
            }
        }
        catch { }
    }

    private static void HandleRequestStatsEvent(EventData eventData)
    {
        if (!SemiFunc.IsMasterClientOrSingleplayer()) return;

        try
        {
            if (eventData.CustomData is not Hashtable data) return;

            string? requestSteamId = data["SteamId"] as string;
            string? requestName = data["Name"] as string;
            int senderActor = eventData.Sender;

            StatsPersistence.Log("SYNC_HOST", $"Received stats request from {requestName} ({requestSteamId}). SenderActor={senderActor}");

            if (string.IsNullOrEmpty(requestSteamId))
            {
                StatsPersistence.Log("SYNC_HOST", "Request missing SteamId, aborting.");
                return;
            }

            Plugin.Instance.StartCoroutine(SendStatsToClient(requestSteamId, senderActor, requestName ?? "Unknown"));
        }
        catch (Exception ex)
        {
            StatsPersistence.Log("SYNC_HOST", $"HandleRequestStatsEvent ERROR: {ex.Message}\n{ex.StackTrace}");
        }
    }

    private static IEnumerator SendStatsToClient(string targetSteamId, int targetActor, string targetName)
    {
        StatsPersistence.Log("SYNC_HOST", $"=== Sync coroutine started for {targetName} ({targetSteamId}) ===");

        StatsPersistence.Log("SYNC_HOST", "Waiting for LevelGenerator.Generated...");
        float timeout = 30f;
        while (timeout > 0f)
        {
            if (LevelGenerator.Instance != null && LevelGenerator.Instance.Generated)
                break;
            yield return new WaitForSeconds(0.2f);
            timeout -= 0.2f;
        }
        StatsPersistence.Log("SYNC_HOST", $"LevelGenerator.Generated={LevelGenerator.Instance?.Generated} (timeout={timeout:F1}s)");

        StatsPersistence.Log("SYNC_HOST", $"Waiting for avatar of {targetSteamId}...");
        timeout = 20f;
        PlayerAvatar? targetAvatar = null;
        while (timeout > 0f)
        {
            targetAvatar = FindAvatarBySteamId(targetSteamId);
            if (targetAvatar != null)
                break;
            yield return new WaitForSeconds(0.3f);
            timeout -= 0.3f;
        }

        if (targetAvatar == null)
        {
            StatsPersistence.Log("SYNC_HOST", $"Could not find avatar for {targetSteamId} after timeout. Aborting.");
            _pendingSyncPlayers.Remove(targetSteamId);
            yield break;
        }

        StatsPersistence.Log("SYNC_HOST", $"Avatar found for {targetName}. Waiting 3s for initialization...");
        yield return new WaitForSeconds(3f);

        if (StatsManager.instance == null)
        {
            StatsPersistence.Log("SYNC_HOST", "StatsManager.instance is null, aborting.");
            _pendingSyncPlayers.Remove(targetSteamId);
            yield break;
        }

        var upgrades = new Hashtable();
        foreach (string key in PlayerStatsData.UpgradeKeys)
        {
            try
            {
                var field = AccessTools.Field(typeof(StatsManager), key);
                if (field == null) continue;

                var dict = field.GetValue(StatsManager.instance) as Dictionary<string, int>;
                if (dict == null) continue;

                int level = dict.TryGetValue(targetSteamId, out int cv) ? cv : 0;
                upgrades[key] = level;
            }
            catch { }
        }

        int health = StatsManager.instance.GetPlayerHealth(targetSteamId);
        int maxHealth = StatsManager.instance.GetPlayerMaxHealth(targetSteamId) + 100;

        var syncData = new Hashtable
        {
            { "SteamId", targetSteamId },
            { "Upgrades", upgrades },
            { "Health", health },
            { "MaxHealth", maxHealth }
        };

        StatsPersistence.Log("SYNC_HOST", $"Sending stats to actor {targetActor}: Upgrades={upgrades.Count} Health={health} MaxHealth={maxHealth}");

        _statsSyncEvent?.RaiseEvent(syncData,
            new RaiseEventOptions { TargetActors = new[] { targetActor } },
            SendOptions.SendReliable);

        _pendingSyncPlayers.Remove(targetSteamId);
        _syncedPlayers.Add(targetSteamId);
        StatsPersistence.Log("SYNC_HOST", $"=== Stats sent to {targetName} ({targetSteamId}) ===");
    }

    private static void HandleStatsSyncConfirmEvent(EventData eventData)
    {
        try
        {
            if (eventData.CustomData is not Hashtable data) return;

            string? confirmSteamId = data["SteamId"] as string;
            bool success = data["Success"] is true;

            StatsPersistence.Log("SYNC_HOST", $"Client confirmed: {confirmSteamId} Success={success}");
        }
        catch (Exception ex)
        {
            StatsPersistence.Log("SYNC_HOST", $"HandleStatsSyncConfirmEvent ERROR: {ex.Message}");
        }
    }

    // ═══════════════════════════════════════════════════════
    // CLIENT SIDE
    // ═══════════════════════════════════════════════════════

    [HarmonyPatch(typeof(PlayerAvatar), "Start")]
    [HarmonyPostfix]
    private static void OnPlayerAvatarStart(PlayerAvatar __instance)
    {
        StatsPersistence.Log("PATCH", $"OnPlayerAvatarStart ENTRY. isMine={__instance?.photonView?.IsMine} isRoom={PhotonNetwork.InRoom} isMaster={PhotonNetwork.IsMasterClient}");
        if (!PhotonNetwork.InRoom) return;
        if (__instance == null || __instance.photonView == null) return;
        if (!__instance.photonView.IsMine) return;

        string? mySteamId = GetLocalSteamId();
        if (string.IsNullOrEmpty(mySteamId))
        {
            StatsPersistence.Log("PATCH", "[OnPlayerAvatarStart] ERROR: Could not get local SteamId");
            return;
        }

        StatsPersistence.Log("PATCH", $"[OnPlayerAvatarStart] Local SteamId={mySteamId}");

        if (PhotonNetwork.IsMasterClient)
        {
            StatsPersistence.Log("PATCH", "[OnPlayerAvatarStart] Master client. Saving own stats as checkpoint...");
            SaveCurrentStats(mySteamId, __instance);
            return;
        }

        StatsPersistence.Log("SYNC_CLIENT", $"Requesting stats from host... SteamId={mySteamId} Name={PhotonNetwork.LocalPlayer?.NickName}");

        var requestData = new Hashtable
        {
            { "SteamId", mySteamId },
            { "Name", PhotonNetwork.LocalPlayer?.NickName ?? "Unknown" }
        };

        _requestStatsEvent?.RaiseEvent(requestData,
            NetworkingEvents.RaiseMasterClient,
            SendOptions.SendReliable);

        string capturedSteamId = mySteamId;
        PlayerAvatar capturedAvatar = __instance;
        Coroutine timeoutCoroutine = Plugin.Instance.StartCoroutine(FallbackTimeout(capturedSteamId, capturedAvatar));
        _fallbackTimeouts[mySteamId] = timeoutCoroutine;
    }

    private static IEnumerator FallbackTimeout(string steamId, PlayerAvatar avatar)
    {
        StatsPersistence.Log("SYNC_CLIENT", $"Fallback timeout started (15s) for {steamId}...");
        yield return new WaitForSeconds(15f);

        if (_syncedPlayers.Contains(steamId))
        {
            StatsPersistence.Log("SYNC_CLIENT", $"Already synced {steamId}, ignoring timeout.");
            yield break;
        }

        StatsPersistence.Log("SYNC_CLIENT", $"Host didn't respond after 15s. Falling back to JSON restore for {steamId}...");

        PlayerStatsData? savedData = StatsPersistence.Load(steamId);
        if (savedData != null)
        {
            StatsPersistence.Log("SYNC_CLIENT", $"Found JSON checkpoint. Restoring Health={savedData.Health} Upgrades={savedData.Upgrades.Count}");
            ApplyStatsFromJSON(avatar, steamId, savedData);
        }
        else
        {
            StatsPersistence.Log("SYNC_CLIENT", "No JSON checkpoint found either. Nothing to restore.");
        }
    }

    private static void HandleStatsSyncEvent(EventData eventData)
    {
        try
        {
            if (eventData.CustomData is not Hashtable data) return;

            string? receivedSteamId = data["SteamId"] as string;
            Hashtable? upgradesData = data["Upgrades"] as Hashtable;
            int health = data["Health"] is int h ? h : 0;
            int maxHealth = data["MaxHealth"] is int mh ? mh : 100;

            StatsPersistence.Log("SYNC_CLIENT", $"Received stats from host! SteamId={receivedSteamId} Upgrades={upgradesData?.Count ?? 0} Health={health} MaxHealth={maxHealth}");

            string? localSteamId = GetLocalSteamId();
            if (string.IsNullOrEmpty(localSteamId) || localSteamId != receivedSteamId)
            {
                StatsPersistence.Log("SYNC_CLIENT", $"SteamId mismatch: expected={localSteamId} received={receivedSteamId}. Ignoring.");
                return;
            }

            if (_fallbackTimeouts.TryGetValue(localSteamId, out var timeoutCoroutine))
            {
                Plugin.Instance.StopCoroutine(timeoutCoroutine);
                _fallbackTimeouts.Remove(localSteamId);
                StatsPersistence.Log("SYNC_CLIENT", "Fallback timeout cancelled (host responded).");
            }

            _syncedPlayers.Add(localSteamId);

            var upgrades = new Dictionary<string, int>();
            if (upgradesData != null)
            {
                foreach (string key in PlayerStatsData.UpgradeKeys)
                {
                    upgrades[key] = upgradesData.ContainsKey(key) ? (int)upgradesData[key] : 0;
                }
            }

            ApplyStatsFromHost(localSteamId, upgrades, health, maxHealth);

            var confirmData = new Hashtable
            {
                { "SteamId", localSteamId },
                { "Success", true }
            };

            _statsConfirmEvent?.RaiseEvent(confirmData,
                NetworkingEvents.RaiseMasterClient,
                SendOptions.SendReliable);

            StatsPersistence.Log("SYNC_CLIENT", "=== Stats applied successfully from host ===");
        }
        catch (Exception ex)
        {
            StatsPersistence.Log("SYNC_CLIENT", $"ERROR applying stats: {ex.Message}\n{ex.StackTrace}");
        }
    }

    // ═══════════════════════════════════════════════════════
    // APPLY STATS
    // ═══════════════════════════════════════════════════════

    private static void ApplyStatsFromHost(string steamId, Dictionary<string, int> upgrades, int health, int maxHealth)
    {
        if (StatsManager.instance == null)
        {
            StatsPersistence.Log("APPLY", "StatsManager.instance is null, cannot apply.");
            return;
        }

        foreach (var kvp in upgrades)
        {
            try
            {
                if (kvp.Value <= 0) continue;

                var field = AccessTools.Field(typeof(StatsManager), kvp.Key);
                if (field == null) continue;

                var dict = field.GetValue(StatsManager.instance) as Dictionary<string, int>;
                if (dict == null) continue;

                int current = dict.TryGetValue(steamId, out int cv) ? cv : 0;
                if (current != kvp.Value)
                {
                    dict[steamId] = kvp.Value;
                    StatsPersistence.Log("APPLY", $"Upgrade {kvp.Key}: {current} -> {kvp.Value}");
                }
            }
            catch (Exception ex)
            {
                StatsPersistence.Log("APPLY", $"Upgrade {kvp.Key} ERROR: {ex.Message}");
            }
        }

        try
        {
            int currentHealth = StatsManager.instance.GetPlayerHealth(steamId);
            if (currentHealth != health)
            {
                StatsManager.instance.SetPlayerHealth(steamId, health, true);
                StatsPersistence.Log("APPLY", $"Health: {currentHealth} -> {health}");
            }
        }
        catch (Exception ex)
        {
            StatsPersistence.Log("APPLY", $"Health ERROR: {ex.Message}");
        }

        try
        {
            if (PlayerAvatar.instance?.playerHealth != null && maxHealth > 100)
            {
                PlayerAvatar.instance.playerHealth.maxHealth = maxHealth;
                StatsPersistence.Log("APPLY", $"MaxHealth set: {maxHealth}");
            }
        }
        catch (Exception ex)
        {
            StatsPersistence.Log("APPLY", $"MaxHealth ERROR: {ex.Message}");
        }

        ApplyRuntimeFields(upgrades);
    }

    private static void ApplyRuntimeFields(Dictionary<string, int> upgrades)
    {
        try
        {
            var physGrab = PhysGrabber.instance;
            if (physGrab != null)
            {
                int str = upgrades.GetValueOrDefault("playerUpgradeStrength", 0);
                int thr = upgrades.GetValueOrDefault("playerUpgradeThrow", 0);
                int rng = upgrades.GetValueOrDefault("playerUpgradeRange", 0);

                physGrab.grabStrength = 1f + str * 0.2f;
                physGrab.throwStrength = thr * 0.3f;
                physGrab.grabRange = rng * 1f;

                StatsPersistence.Log("APPLY", $"PhysGrabber: grabStrength={physGrab.grabStrength:F1} throwStrength={physGrab.throwStrength:F1} grabRange={physGrab.grabRange:F1}");
            }
        }
        catch (Exception ex)
        {
            StatsPersistence.Log("APPLY", $"PhysGrabber ERROR: {ex.Message}");
        }

        try
        {
            var pc = PlayerController.instance;
            if (pc != null)
            {
                int spd = upgrades.GetValueOrDefault("playerUpgradeSpeed", 0);
                int jmp = upgrades.GetValueOrDefault("playerUpgradeExtraJump", 0);
                int stam = upgrades.GetValueOrDefault("playerUpgradeStamina", 0);

                pc.SprintSpeed += spd;
                pc.JumpExtra = jmp;
                pc.EnergyStart += stam * 10;

                StatsPersistence.Log("APPLY", $"PlayerController: SprintSpeed={pc.SprintSpeed} JumpExtra={pc.JumpExtra} EnergyStart={pc.EnergyStart}");
            }
        }
        catch (Exception ex)
        {
            StatsPersistence.Log("APPLY", $"PlayerController ERROR: {ex.Message}");
        }

        try
        {
            var avatar = PlayerAvatar.instance;
            if (avatar != null)
            {
                avatar.upgradeCrouchRest = upgrades.GetValueOrDefault("playerUpgradeCrouchRest", 0);
                avatar.upgradeTumbleWings = upgrades.GetValueOrDefault("playerUpgradeTumbleWings", 0);
                avatar.upgradeTumbleClimb = upgrades.GetValueOrDefault("playerUpgradeTumbleClimb", 0);
                avatar.upgradeMapPlayerCount = upgrades.GetValueOrDefault("playerUpgradeMapPlayerCount", 0);
                avatar.upgradeDeathHeadBattery = upgrades.GetValueOrDefault("playerUpgradeDeathHeadBattery", 0);

                StatsPersistence.Log("APPLY", $"Avatar upgrades: CrouchRest={avatar.upgradeCrouchRest} TumbleWings={avatar.upgradeTumbleWings} TumbleClimb={avatar.upgradeTumbleClimb} MapCount={avatar.upgradeMapPlayerCount} DeathHead={avatar.upgradeDeathHeadBattery}");
            }
        }
        catch (Exception ex)
        {
            StatsPersistence.Log("APPLY", $"Avatar upgrades ERROR: {ex.Message}");
        }
    }

    // ═══════════════════════════════════════════════════════
    // FALLBACK (JSON RESTORE)
    // ═══════════════════════════════════════════════════════

    private static void ApplyStatsFromJSON(PlayerAvatar avatar, string steamId, PlayerStatsData data)
    {
        if (StatsManager.instance == null)
        {
            StatsPersistence.Log("FALLBACK", "StatsManager.instance is null, cannot restore from JSON.");
            return;
        }

        foreach (var kvp in data.Upgrades)
        {
            try
            {
                if (kvp.Value <= 0) continue;

                var field = AccessTools.Field(typeof(StatsManager), kvp.Key);
                if (field == null) continue;

                var dict = field.GetValue(StatsManager.instance) as Dictionary<string, int>;
                if (dict == null) continue;

                int current = dict.TryGetValue(steamId, out int cv) ? cv : 0;
                if (current != kvp.Value)
                {
                    dict[steamId] = kvp.Value;
                    StatsPersistence.Log("FALLBACK", $"Upgrade {kvp.Key}: {current} -> {kvp.Value}");
                }
            }
            catch (Exception ex)
            {
                StatsPersistence.Log("FALLBACK", $"Upgrade {kvp.Key} ERROR: {ex.Message}");
            }
        }

        try
        {
            if (data.Health > 0)
            {
                StatsManager.instance.SetPlayerHealth(steamId, data.Health, true);
                StatsPersistence.Log("FALLBACK", $"Health restored: {data.Health}");
            }
        }
        catch (Exception ex)
        {
            StatsPersistence.Log("FALLBACK", $"Health ERROR: {ex.Message}");
        }

        try
        {
            if (avatar.playerHealth != null && data.MaxHealth > 100)
            {
                avatar.playerHealth.maxHealth = data.MaxHealth;
                StatsPersistence.Log("FALLBACK", $"MaxHealth restored: {data.MaxHealth}");
            }
        }
        catch (Exception ex)
        {
            StatsPersistence.Log("FALLBACK", $"MaxHealth ERROR: {ex.Message}");
        }

        ApplyRuntimeFields(data.Upgrades);
        StatsPersistence.Log("FALLBACK", "=== JSON restore complete ===");
    }

    // ═══════════════════════════════════════════════════════
    // UTILITIES
    // ═══════════════════════════════════════════════════════

    private static PlayerAvatar? FindAvatarBySteamId(string steamId)
    {
        foreach (var avatar in UnityEngine.Object.FindObjectsOfType<PlayerAvatar>())
        {
            if (avatar.steamID == steamId)
                return avatar;
        }
        return null;
    }

    internal static string? GetSteamId(Player player)
    {
        if (player == null) return null;
        if (player.CustomProperties == null) return null;

        var keys = new[] { "steamID", "SteamID", "steamId", "SteamId", "STEAMID", "SteamIDLong", "steam_id", "steamid" };
        foreach (var key in keys)
        {
            if (player.CustomProperties.TryGetValue(key, out var prop))
            {
                if (prop is ulong u) return u.ToString();
                if (prop is long l) return l.ToString();
                if (prop is int i) return i.ToString();
                if (prop is string s && !string.IsNullOrEmpty(s)) return s;
            }
        }

        if (!string.IsNullOrEmpty(player.UserId))
            return player.UserId;

        return null;
    }

    internal static string? GetLocalSteamId()
    {
        var player = PhotonNetwork.LocalPlayer;
        if (player == null) return null;
        return GetSteamId(player);
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
            int maxHealth = StatsManager.instance.GetPlayerMaxHealth(steamId) + 100;

            var upgrades = new Dictionary<string, int>();
            foreach (string key in PlayerStatsData.UpgradeKeys)
            {
                try
                {
                    var field = AccessTools.Field(typeof(StatsManager), key);
                    if (field != null)
                    {
                        var dict = field.GetValue(StatsManager.instance) as Dictionary<string, int>;
                        if (dict != null && dict.TryGetValue(steamId, out int val))
                            upgrades[key] = val;
                        else
                            upgrades[key] = 0;
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

            var data = new PlayerStatsData
            {
                SteamId = steamId,
                PlayerName = PhotonNetwork.LocalPlayer?.NickName ?? "Unknown",
                Health = health,
                MaxHealth = maxHealth,
                Energy = 0,
                Upgrades = upgrades,
                Timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
            };

            StatsPersistence.Save(data);
            Plugin.Logger.LogInfo($"[STATS_SYNC] Saved checkpoint for {steamId}: Health={health} Upgrades={upgrades.Count}");
        }
        catch (Exception ex)
        {
            StatsPersistence.Log("PATCH", $"[SaveCurrentStats] FATAL ERROR: {ex.Message}\n{ex.StackTrace}");
        }
    }
}
