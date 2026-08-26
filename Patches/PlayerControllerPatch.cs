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

    private static PlayerController ResolveInstance()
    {
        if (_resolveInstance != null) return _resolveInstance();

        if (_resolutionAttempted) return PlayerController.instance;
        _resolutionAttempted = true;

        try
        {
            var prop = AccessTools.PropertyGetter(typeof(PlayerController), "instance");
            if (prop != null)
            {
                _resolveInstance = () => (PlayerController)prop.Invoke(null, null);
                return _resolveInstance();
            }
        }
        catch { }

        try
        {
            var propUpper = AccessTools.PropertyGetter(typeof(PlayerController), "Instance");
            if (propUpper != null)
            {
                _resolveInstance = () => (PlayerController)propUpper.Invoke(null, null);
                return _resolveInstance();
            }
        }
        catch { }

        try
        {
            var field = AccessTools.Field(typeof(PlayerController), "instance");
            if (field != null)
            {
                _resolveInstance = () => (PlayerController)field.GetValue(null);
                return _resolveInstance();
            }
        }
        catch { }

        try
        {
            var fieldUpper = AccessTools.Field(typeof(PlayerController), "Instance");
            if (fieldUpper != null)
            {
                _resolveInstance = () => (PlayerController)fieldUpper.GetValue(null);
                return _resolveInstance();
            }
        }
        catch { }

        try
        {
            foreach (var f in typeof(PlayerController).GetFields(BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic))
            {
                if (f.FieldType == typeof(PlayerController))
                {
                    var resolved = f;
                    _resolveInstance = () => (PlayerController)resolved.GetValue(null);
                    return _resolveInstance();
                }
            }
        }
        catch { }

        _resolveInstance = () => PlayerController.instance;
        return PlayerController.instance;
    }

    [HarmonyPrefix]
    [HarmonyPatch(typeof(PhysGrabber), "Update")]
    private static bool PhysGrabberUpdatePrefix(PhysGrabber __instance)
    {
        if (__instance == null) return false;

        try
        {
            var pc = ResolveInstance();
            if (pc == null) return false;
        }
        catch
        {
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
            return pc != null && PlayerAvatar.instance != null;
        }
        catch
        {
            return false;
        }
    }
}
