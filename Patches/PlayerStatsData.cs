using System;
using System.Collections.Generic;

namespace RepoPanas_mod.Patches;

[Serializable]
public class PlayerStatsData
{
    public string SteamId { get; set; } = string.Empty;
    public string PlayerName { get; set; } = string.Empty;
    public int Health { get; set; }
    public float Energy { get; set; }
    public Dictionary<string, int> Upgrades { get; set; } = new();
    public long Timestamp { get; set; }

    public static readonly string[] UpgradeKeys =
    [
        "playerUpgradeHealth",
        "playerUpgradeSpeed",
        "playerUpgradeStamina",
        "playerUpgradeRange",
        "playerUpgradeStrength",
        "playerUpgradeThrow",
        "playerUpgradeLaunch",
        "playerUpgradeJump",
        "playerUpgradeCrouch Rest",
        "playerUpgradeTumble Wings",
        "playerUpgradeTumble Climb",
        "playerUpgradeMap Player Count",
        "playerUpgradeDeath Head"
    ];
}
