using Bunburrows;
using Bunject.Computer;
using Bunject.Internal;
using Bunject.Map;
using Characters.Bunny.Data;
using Computer;
using Computer.Opheline.Map;
using Computer.Opheline.Tabs;
using HarmonyLib;
using Levels;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;
using UnityEngine;

namespace Bunject.Patches.OphelineComputerMapTabControllerPatches
{

  [HarmonyPatch(typeof(OphelineComputerMapTabController), nameof(OphelineComputerMapTabController.HandleOpen))]
  internal static class HandleOpenPatch 
  {
    internal static void Prefix()
    {
      MapContext.Instance?.Dispose();

      var currentBurrow = GameManager.LevelStates.CurrentLevelState?.LevelIdentity.Bunburrow;
      if (currentBurrow.HasValue && currentBurrow.Value.IsCustomBunburrow())
      {
        new MapContext(currentBurrow.Value);
      }
    }
  }
  
  [HarmonyPatch(typeof(OphelineComputerMapTabController), "UpdateSelector")]
  internal static class UpdateSelectorPatch
  {
    // Prevent infinite loops because you never know
    private static bool firstTime = true;

    // Allow infinite map scroll
    internal static void Postfix(ref OphelineComputerMapTabController __instance)
    {
      var currentBurrow = GameManager.LevelStates.CurrentLevelState?.LevelIdentity.Bunburrow;
      if (currentBurrow.HasValue && currentBurrow.Value.IsCustomBunburrow() && firstTime)
      {
        firstTime = false;
        var traverse = Traverse.Create(__instance);
        var mapRectTransform = traverse.Field<RectTransform>("mapRectTransform").Value;
        var currentlyHoveredLevel = traverse.Field<LevelIdentity?>("currentlyHoveredLevel").Value;

        // Determine how far away the map is from the center burrow
        var mapScreenOffset = new Vector2(mapRectTransform.rect.xMin, mapRectTransform.rect.yMin);
        var selectorPosition = -mapRectTransform.anchoredPosition - mapScreenOffset - ComputerMapBuilder.InitialOffset;
        var selectedBurrowCoords = new Vector2Int((int)(selectorPosition.x / 75f), (int)(selectorPosition.y / 45f));
        if ((selectedBurrowCoords != new Vector2(1, 1)) && currentlyHoveredLevel.HasValue)
        {
          // Seamlessly transition from one burrow to the next
          var diffFromCenterCoords = selectedBurrowCoords - new Vector2(1, 1);
          traverse.Field<RectTransform>("mapRectTransform").Value.anchoredPosition += new Vector2(
            diffFromCenterCoords.x * ComputerMapBuilder.LevelPixelSize.x, 
            diffFromCenterCoords.y * ComputerMapBuilder.LevelPixelSize.y
          );
          AccessTools.Method(typeof(OphelineComputerMapTabController), "ForceUpdatePosition").Invoke(__instance, null);
          AccessTools.Method(typeof(OphelineComputerMapTabController), "UpdateSelector").Invoke(__instance, null);

          // Force redraw map
          var newBurrow = currentlyHoveredLevel.Value.Bunburrow;
          MapContext.Instance?.Dispose();
          new MapContext(newBurrow);
          AccessTools.Method(typeof(OphelineComputerMapTabController), "DrawMap").Invoke(__instance, null);
          AccessTools.Method(typeof(OphelineComputerMapTabController), "UpdateSelector").Invoke(__instance, null);

          // Hide pin selector object when entering a different burrow
          var pinnedBunnyMapInfo = traverse.Field<MapBunnyInfo>("pinnedBunnyMapInfo").Value;
          var pinnedSelectorGameObject = traverse.Field<GameObject>("pinnedSelectorGameObject").Value;
          if (pinnedBunnyMapInfo != null)
          {
            if (pinnedBunnyMapInfo.BunnyIdentity.Bunburrow == newBurrow)
            {
              pinnedSelectorGameObject.SetActive(true);
            }
            else
            {
              pinnedSelectorGameObject.SetActive(false);
            }
          }
        }
      }
      firstTime = true;
    }
  }

  [HarmonyPatch(typeof(OphelineComputerMapTabController), "HandleExtraInput")]
  internal static class HandleExtraInputPatch
  {
    // Fix pin selector display in looping burrows
    internal static void Postfix(ref OphelineComputerMapTabController __instance)
    {
      var currentBurrow = GameManager.LevelStates.CurrentLevelState?.LevelIdentity.Bunburrow;
      if (currentBurrow.HasValue && currentBurrow.Value.IsCustomBunburrow())
      {
        var traverse = Traverse.Create(__instance);
        var mapRectTransform = traverse.Field<RectTransform>("mapRectTransform").Value;
        var pinnedSelectorRectTransform = traverse.Field<RectTransform>("pinnedSelectorRectTransform").Value;

        var mapScreenOffset = new Vector2(mapRectTransform.rect.xMin, mapRectTransform.rect.yMin);
        var selectorPosition = pinnedSelectorRectTransform.anchoredPosition + mapScreenOffset + ComputerMapBuilder.InitialOffset;
        var pinBurrowCoords = new Vector2Int((int)(selectorPosition.x / 75f), (int)(selectorPosition.y / 45f));
        if (pinBurrowCoords != new Vector2(1, 1))
        {
          // Move pin to center of map
          var diffFromCenterCoords = pinBurrowCoords - new Vector2(1, 1);
          pinnedSelectorRectTransform.anchoredPosition -= new Vector2(
            diffFromCenterCoords.x * ComputerMapBuilder.LevelPixelSize.x,
            diffFromCenterCoords.y * ComputerMapBuilder.LevelPixelSize.y
          );
        }
      }
    }
  }

  [HarmonyPatch(typeof(OphelineComputerMapTabController), nameof(UpdateHeaderText))]
  internal static class UpdateHeaderText
  {
    internal static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
    {
      var codeMatcher = new CodeMatcher(instructions);

      codeMatcher.MatchForward(false, new CodeMatch(OpCodes.Callvirt, typeof(List<BunnyIdentity>).GetMethod(nameof(List<BunnyIdentity>.Add))))
        .SetInstruction(Transpilers.EmitDelegate<Action<List<BunnyIdentity>, BunnyIdentity>>(AddIfUnique));

      return codeMatcher.InstructionEnumeration();
    }

    private static void AddIfUnique(List<BunnyIdentity> list, BunnyIdentity identity)
    {
      if (!list.Any(i => i.Equals(identity)))
        list.Add(identity);
    }
  }

}
