using System;
using System.Reflection;
using HarmonyLib;
using Photon.Pun;
using UnityEngine;

namespace RepoPanas_mod.Patches;

[HarmonyPatch]
internal static class PlayerControllerPatch
{
    private static Func<PlayerController> _resolveInstance;
    private static bool _resolutionAttempted;
    private static int _resolveAttemptCount;

    private static PlayerController ResolveInstance()
    {
        if (_resolveInstance != null) return _resolveInstance();

        if (_resolutionAttempted) return PlayerController.instance;
        _resolutionAttempted = true;
        _resolveAttemptCount++;

        StatsPersistence.Log("PATCH", $"[ResolveInstance] Attempt #{_resolveAttemptCount}");

        try
        {
            var prop = AccessTools.PropertyGetter(typeof(PlayerController), "instance");
            if (prop != null)
            {
                StatsPersistence.Log("PATCH", "[ResolveInstance] Found property getter 'instance'");
                _resolveInstance = () => (PlayerController)prop.Invoke(null, null);
                return _resolveInstance();
            }
        }
        catch (Exception ex)
        {
            StatsPersistence.Log("PATCH", $"[ResolveInstance] property 'instance' failed: {ex.Message}");
        }

        try
        {
            var propUpper = AccessTools.PropertyGetter(typeof(PlayerController), "Instance");
            if (propUpper != null)
            {
                StatsPersistence.Log("PATCH", "[ResolveInstance] Found property getter 'Instance'");
                _resolveInstance = () => (PlayerController)propUpper.Invoke(null, null);
                return _resolveInstance();
            }
        }
        catch (Exception ex)
        {
            StatsPersistence.Log("PATCH", $"[ResolveInstance] property 'Instance' failed: {ex.Message}");
        }

        try
        {
            var field = AccessTools.Field(typeof(PlayerController), "instance");
            if (field != null)
            {
                StatsPersistence.Log("PATCH", "[ResolveInstance] Found field 'instance'");
                _resolveInstance = () => (PlayerController)field.GetValue(null);
                return _resolveInstance();
            }
        }
        catch (Exception ex)
        {
            StatsPersistence.Log("PATCH", $"[ResolveInstance] field 'instance' failed: {ex.Message}");
        }

        try
        {
            var fieldUpper = AccessTools.Field(typeof(PlayerController), "Instance");
            if (fieldUpper != null)
            {
                StatsPersistence.Log("PATCH", "[ResolveInstance] Found field 'Instance'");
                _resolveInstance = () => (PlayerController)fieldUpper.GetValue(null);
                return _resolveInstance();
            }
        }
        catch (Exception ex)
        {
            StatsPersistence.Log("PATCH", $"[ResolveInstance] field 'Instance' failed: {ex.Message}");
        }

        try
        {
            foreach (var f in typeof(PlayerController).GetFields(BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic))
            {
                if (f.FieldType == typeof(PlayerController))
                {
                    StatsPersistence.Log("PATCH", $"[ResolveInstance] Found static field via reflection: {f.Name}");
                    var resolved = f;
                    _resolveInstance = () => (PlayerController)resolved.GetValue(null);
                    return _resolveInstance();
                }
            }
        }
        catch (Exception ex)
        {
            StatsPersistence.Log("PATCH", $"[ResolveInstance] Reflection scan failed: {ex.Message}");
        }

        StatsPersistence.Log("PATCH", "[ResolveInstance] All methods failed, falling back to PlayerController.instance");
        _resolveInstance = () => PlayerController.instance;
        return PlayerController.instance;
    }

    [HarmonyPrefix]
    [HarmonyPatch(typeof(PhysGrabber), "Update")]
    private static bool PhysGrabberUpdatePrefix(PhysGrabber __instance)
    {
        if (__instance == null)
        {
            StatsPersistence.Log("PATCH", "[PhysGrabberUpdate] __instance is null, blocking.");
            return false;
        }

        try
        {
            var pc = ResolveInstance();
            if (pc == null)
            {
                StatsPersistence.Log("PATCH", "[PhysGrabberUpdate] PlayerController is null, blocking.");
                return false;
            }
            StatsPersistence.Log("PATCH", $"[PhysGrabberUpdate] OK. grabStrength={__instance.grabStrength:F1} throwStrength={__instance.throwStrength:F1} grabRange={__instance.grabRange:F1}");
        }
        catch (Exception ex)
        {
            StatsPersistence.Log("PATCH", $"[PhysGrabberUpdate] ERROR: {ex.Message}");
            return false;
        }

        return true;
    }

    [HarmonyPrefix]
    [HarmonyPatch(typeof(EnergyUI), "Update")]
    private static bool EnergyUIUpdatePrefix()
    {
        try
        {
            var pc = ResolveInstance();
            bool pcOk = pc != null;
            bool avatarOk = PlayerAvatar.instance != null;
            StatsPersistence.Log("PATCH", $"[EnergyUIUpdate] PC={pcOk} Avatar={avatarOk}");
            return pcOk && avatarOk;
        }
        catch (Exception ex)
        {
            StatsPersistence.Log("PATCH", $"[EnergyUIUpdate] ERROR: {ex.Message}");
            return false;
        }
    }
}
