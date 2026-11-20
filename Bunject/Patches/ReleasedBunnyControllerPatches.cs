using Bunburrows;
using Bunject.Internal;
using Characters.Bunny;
using Characters.Bunny.Data;
using HarmonyLib;
using Levels;
using System;
using System.Runtime.CompilerServices;
using UnityEngine;

namespace Bunject.Patches.ReleasedBunnyControllerPatches
{
  internal static class ReleasedBunnySpriteSheets
  {
    internal class Override
    {
      //null = skip the animation
      public int? SpriteSheetID;
    }

    //Absent = vanilla behaviour
    internal static readonly ConditionalWeakTable<ReleasedBunnyController, Override> Overrides = new ConditionalWeakTable<ReleasedBunnyController, Override>();

    internal static bool HasSprites(int spriteSheetID)
    {
      return AssetsManager.BunnySprites != null && AssetsManager.BunnySprites.ContainsKey($"bunny{spriteSheetID}_0");
    }

    internal static int? Resolve(BunnyIdentity bunny)
    {
      try
      {
        var levelStyle = LevelIndicatorGenerator.GetLevelBunburrowStyle(new LevelIdentity(bunny.Bunburrow, bunny.InitialDepth));
        if (levelStyle != null && HasSprites(levelStyle.BunnySpriteSheetsID))
          return levelStyle.BunnySpriteSheetsID;
      }
      catch (Exception ex)
      {
        Debug.LogWarning($"[Bunject] Could not resolve level style for released bunny '{bunny.GetIdentityString()}': {ex.Message}");
      }

      var burrowStyle = bunny.Bunburrow.GetModBunburrow()?.Style;
      if (burrowStyle != null && HasSprites(burrowStyle.BunnySpriteSheetsID))
        return burrowStyle.BunnySpriteSheetsID;

      return null;
    }
  }

  [HarmonyPatch(typeof(ReleasedBunnyController), nameof(ReleasedBunnyController.Initialize))]
  internal class InitializePatch
  {
    private static void Postfix(ReleasedBunnyController __instance, BunnyIdentity bunnyIdentity, BunniesReleaseAnimator bunniesReleaseAnimator)
    {
      if (ReleasedBunnySpriteSheets.HasSprites(bunnyIdentity.SpriteSheetID))
        return;

      var resolved = ReleasedBunnySpriteSheets.Resolve(bunnyIdentity);
      ReleasedBunnySpriteSheets.Overrides.Remove(__instance);
      ReleasedBunnySpriteSheets.Overrides.Add(__instance, new ReleasedBunnySpriteSheets.Override { SpriteSheetID = resolved });
      if (resolved != null)
        return;

      //No usable sprites - release it logically without the walk
      Debug.LogWarning($"[Bunject] No bunny sprites for released bunny '{bunnyIdentity.GetIdentityString()}' (sheet {bunnyIdentity.SpriteSheetID}); skipping its release animation.");
      try { bunniesReleaseAnimator?.NotifyBunnyInHole(__instance); }
      catch (Exception ex) { Debug.LogError(ex); }
      __instance.PrepareDestruction();
    }
  }

  [HarmonyPatch(typeof(ReleasedBunnyController), "LateUpdate")]
  internal class LateUpdatePatch
  {
    private static bool Prefix(ReleasedBunnyController __instance, SpriteRenderer ___spriteRenderer)
    {
      if (!ReleasedBunnySpriteSheets.Overrides.TryGetValue(__instance, out var entry))
        return true;

      var sheet = entry.SpriteSheetID;
      if (sheet == null || !GameManager.IsReady || ___spriteRenderer == null || ___spriteRenderer.sprite == null)
        return false;

      var parts = ___spriteRenderer.sprite.name.Split('_');
      if (parts.Length > 1 && AssetsManager.BunnySprites.TryGetValue($"bunny{sheet}_{parts[1]}", out var sprite))
        ___spriteRenderer.sprite = sprite;
      return false;
    }
  }
}
