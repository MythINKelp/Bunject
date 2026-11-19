using HarmonyLib;
using System;
using System.Collections;
using System.Collections.Generic;
using Bunburrows;
using Bunject.Internal;
using Characters;
using Characters.Bunny;
using Misc;
using UnityEngine;

namespace Bunject.Patches
{
  internal static class ReleasePathSanitizer
  {
    //Same spawn tile and 15x9 surface bounds the vanilla release bakes in
    private static readonly Vector2Int SpawnTile = new Vector2Int(0, -6);
    private const int MinX = 0;
    private const int MaxX = 14;
    private const int MinY = -8;
    private const int MaxY = 0;

    internal static List<Vector2Int> Sanitize(List<Vector2Int> path)
    {
      var result = new List<Vector2Int>();
      if (path == null)
        return result;

      var cursor = SpawnTile;
      foreach (var raw in path)
      {
        var target = new Vector2Int(
          Mathf.Clamp(raw.x, MinX, MaxX),
          Mathf.Clamp(raw.y, MinY, MaxY));

        if (target == cursor)
          continue;

        //Released bunnies walk one fixed direction per leg - diagonal targets loop forever
        if (target.x != cursor.x && target.y != cursor.y)
        {
          var corner = new Vector2Int(target.x, cursor.y);
          result.Add(corner);
          cursor = corner;
        }

        result.Add(target);
        cursor = target;
      }

      return result;
    }
  }

  [HarmonyPatch(typeof(BunniesReleaseAnimator), "ConvertBunburrowToReleaseTargetsList")]
  internal class ConvertBunburrowToReleaseTargetsListPatches
  {
    // Return a release path for custom burrows
    private static bool Prefix(ref List<Vector2Int> __result, Bunburrow bunburrow)
    {
      if (!bunburrow.IsCustomBunburrow())
        return true;

      var mod = bunburrow.GetModBunburrow();
      __result = mod?.GetReleasePath() ?? new List<Vector2Int>();
      return false;
    }

    private static void Postfix(ref List<Vector2Int> __result)
    {
      __result = ReleasePathSanitizer.Sanitize(__result);
    }
  }

  [HarmonyPatch(typeof(MovingCharacterController), "Move", new Type[] { typeof(Direction), typeof(bool) })]
  internal class ReleasedBunnyMoveEdgeGuard
  {
    //No bounds check in vanilla - stepping off the level throws and kills the release coroutine
    private static bool Prefix(MovingCharacterController __instance, Direction direction)
    {
      if (!(__instance is ReleasedBunnyController bunny))
        return true;

      var current = __instance.CurrentTile;
      if (current != null && current.GetAdjacentTile(direction) != null)
        return true;

      UnityEngine.Debug.LogWarning(
        $"[Bunject] Released bunny '{bunny.BunnyIdentity.GetIdentityString()}' would step off the level; retiring it to avoid a stuck release.");

      var animator = Traverse.Create(bunny).Field("bunniesReleaseAnimator").GetValue<BunniesReleaseAnimator>();
      animator?.NotifyBunnyInHole(bunny);
      bunny.PrepareDestruction();
      return false;
    }
  }

  internal static class ReleaseWatchdog
  {
    private const float BaseTimeoutSeconds = 20f;

    //Longest vanilla delay between bunnies, so big healthy releases don't trip the watchdog
    internal const float ReleaseSecondsPerBunny = 0.25f;
    internal const float AllReleaseSecondsPerBunny = 0.15f;
    internal const float RecaptureSecondsPerBunny = 0.15f;

    //Only the latest watchdog may act, so a stale one can't cut off a later release
    internal static int Generation;

    internal static IEnumerator Run(BunniesReleaseAnimator animator, int generation, int bunnyCount, float secondsPerBunny, Action freeBunnies)
    {
      var timeoutSeconds = BaseTimeoutSeconds + Mathf.Max(0, bunnyCount) * secondsPerBunny;

      yield return new WaitForSecondsRealtime(timeoutSeconds);

      if (animator == null || generation != Generation || !animator.AreBunniesBeingReleased)
        yield break;

      UnityEngine.Debug.LogError(
        $"[Bunject] Bunny release/recapture did not finish within {timeoutSeconds:0}s; forcing completion to avoid a soft-lock.");

      try { animator.StopRelease(); }
      catch (Exception ex) { UnityEngine.Debug.LogError(ex); }

      try { GameManager.UIController.SwitchToPenIcon(false); }
      catch (Exception ex) { UnityEngine.Debug.LogError(ex); }

      try { freeBunnies?.Invoke(); }
      catch (Exception ex) { UnityEngine.Debug.LogError(ex); }
    }
  }

  [HarmonyPatch(typeof(BunniesReleaseAnimator), nameof(BunniesReleaseAnimator.StartRelease))]
  internal class StartReleaseWatchdogPatch
  {
    private static void Postfix(BunniesReleaseAnimator __instance, Bunburrow bunburrow)
    {
      //Bump before the void check so a void release still invalidates older watchdogs
      var generation = ++ReleaseWatchdog.Generation;

      //Void release doesn't use the walk, so it can't hang
      if (bunburrow.IsVoidBunburrow())
        return;

      var bunnyCount = GameManager.GeneralProgression.GetCapturedBunniesFromBunburrow(bunburrow).Count;
      __instance.StartCoroutine(ReleaseWatchdog.Run(
        __instance, generation, bunnyCount, ReleaseWatchdog.ReleaseSecondsPerBunny,
        () => GameManager.GeneralProgression.FreeBunniesFromBunburrow(bunburrow)));
    }
  }

  [HarmonyPatch(typeof(BunniesReleaseAnimator), nameof(BunniesReleaseAnimator.StartAllRelease))]
  internal class StartAllReleaseWatchdogPatch
  {
    private static void Postfix(BunniesReleaseAnimator __instance)
    {
      var generation = ++ReleaseWatchdog.Generation;
      var bunnyCount = GameManager.GeneralProgression.GetNonVoidBunniesCount(true);
      __instance.StartCoroutine(ReleaseWatchdog.Run(
        __instance, generation, bunnyCount, ReleaseWatchdog.AllReleaseSecondsPerBunny,
        () => GameManager.GeneralProgression.FreeAllNonVoidBunnies(true)));
    }
  }

  [HarmonyPatch(typeof(BunniesReleaseAnimator), nameof(BunniesReleaseAnimator.StartAllRecapture))]
  internal class StartAllRecaptureWatchdogPatch
  {
    private static void Postfix(BunniesReleaseAnimator __instance)
    {
      var generation = ++ReleaseWatchdog.Generation;
      var progression = GameManager.GeneralProgression;
      var bunnyCount = progression.HistoryCapturedBunnies.Count - progression.CapturedBunnies.Count;
      __instance.StartCoroutine(ReleaseWatchdog.Run(
        __instance, generation, bunnyCount, ReleaseWatchdog.RecaptureSecondsPerBunny, null));
    }
  }
}
