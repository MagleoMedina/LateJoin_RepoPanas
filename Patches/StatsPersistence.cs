#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using Newtonsoft.Json;
using System.Threading;

namespace RepoPanas_mod.Patches;

internal static class StatsPersistence
{
    private static string _statsDir = string.Empty;
    private static string _logPath = string.Empty;
    private static readonly object _logLock = new();


    internal static void Initialize(string pluginPath)
    {
        _statsDir = Path.Combine(pluginPath, "player_stats");
        _logPath = Path.Combine(pluginPath, "repo_panas_debug.log");

        try
        {
            if (!Directory.Exists(_statsDir))
                Directory.CreateDirectory(_statsDir);

            File.WriteAllText(_logPath, string.Empty);
            Log("INIT", $"StatsPersistence initialized. Dir={_statsDir} Log={_logPath}");
        }
        catch (Exception ex)
        {
            Plugin.Logger.LogError($"[STATS_PERSISTENCE] Failed to initialize: {ex.Message}");
        }
    }

    internal static void Log(string category, string message)
    {
        try
        {
            string timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff");
            string line = $"[{timestamp}] [{category}] {message}";

            lock (_logLock)
            {
                File.AppendAllText(_logPath, line + Environment.NewLine);
            }
        }
        catch
        {
            // Silent fail for logging
        }
    }

    internal static void Save(PlayerStatsData data)
    {
        try
        {
            if (string.IsNullOrEmpty(data.SteamId))
            {
                Log("SAVE", "ERROR: SteamId is null or empty, cannot save.");
                return;
            }

            string fileName = $"{data.SteamId}.json";
            string filePath = Path.Combine(_statsDir, fileName);
            string json = JsonConvert.SerializeObject(data, Formatting.Indented);

            File.WriteAllText(filePath, json);

            Log("SAVE", $"Player={data.PlayerName} Steam={data.SteamId}");
            Log("SAVE", $"Health={data.Health} Energy={data.Energy:F1} Upgrades={data.Upgrades.Count}");
            Log("SAVE", $"File saved: {filePath}");
        }
        catch (Exception ex)
        {
            Log("SAVE", $"ERROR: {ex.Message}");
            Plugin.Logger.LogError($"[STATS_PERSISTENCE] Save failed: {ex.Message}");
        }
    }

    internal static PlayerStatsData? Load(string steamId)
    {
        try
        {
            if (string.IsNullOrEmpty(steamId))
            {
                Log("LOAD", "ERROR: SteamId is null or empty.");
                return null;
            }

            string fileName = $"{steamId}.json";
            string filePath = Path.Combine(_statsDir, fileName);

            if (!File.Exists(filePath))
            {
                Log("LOAD", $"No saved stats found for Steam={steamId}");
                return null;
            }

            string json = File.ReadAllText(filePath);
                    PlayerStatsData? data = JsonConvert.DeserializeObject<PlayerStatsData>(json);

            if (data != null)
            {
                Log("LOAD", $"Found saved stats for Player={data.PlayerName} Steam={data.SteamId}");
                Log("LOAD", $"Health={data.Health} Energy={data.Energy:F1} Upgrades={data.Upgrades.Count}");
                Log("LOAD", $"Timestamp={data.Timestamp}");
            }
            else
            {
                Log("LOAD", $"WARNING: Deserialized to null for Steam={steamId}");
            }

            return data;
        }
        catch (Exception ex)
        {
            Log("LOAD", $"ERROR: {ex.Message}");
            Plugin.Logger.LogError($"[STATS_PERSISTENCE] Load failed: {ex.Message}");
            return null;
        }
    }

    internal static List<PlayerStatsData> GetAll()
    {
        var results = new List<PlayerStatsData>();

        try
        {
            if (!Directory.Exists(_statsDir))
                return results;

            string[] files = Directory.GetFiles(_statsDir, "*.json");
            Log("GET_ALL", $"Found {files.Length} stat files.");

            foreach (string file in files)
            {
                try
                {
                    string json = File.ReadAllText(file);
            PlayerStatsData? data = JsonConvert.DeserializeObject<PlayerStatsData>(json);
                    if (data != null)
                        results.Add(data);
                }
                catch (Exception ex)
                {
                    Log("GET_ALL", $"ERROR reading {file}: {ex.Message}");
                }
            }
        }
        catch (Exception ex)
        {
            Log("GET_ALL", $"ERROR: {ex.Message}");
        }

        return results;
    }
}
