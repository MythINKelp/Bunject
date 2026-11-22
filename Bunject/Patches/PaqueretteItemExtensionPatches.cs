using System;
using Bunject.Dialogue;
using Bunject.Levels;
using Characters;
using Dialogue;
using HarmonyLib;
using Items;
using Levels;
using Misc;

namespace Bunject.Patches.PaqueretteItemExtensionPatches
{
  [HarmonyPatch(typeof(PaqueretteItemExtension), "IsAvailableInContext")]
  internal class IsAvailableInContextPatch
  {
    static bool Prefix(ref bool __result, Item item)
    {
      if ((item == Item.Talk)
        && (GameManager.LevelStates.CurrentLevelState.LevelIdentity.Depth != 0)
        && (GameManager.CurrentLevel.BaseData is ModLevelObject)
        && ((GameManager.OphelineController != null) || (GameManager.HerbeController != null)))
      {
        __result = true;
        return false;
      }
      return true;
    }

    static void Postfix(ref bool __result, Item item)
    {
      if (__result == false && GameManager.CurrentLevel.BaseData is ModLevelObject)
      {
        var level = (ModLevelObject)GameManager.CurrentLevel.BaseData;
        __result = IsDialogueItemAvailable(level, item);
      }
    }

    public static bool IsDialogueItemAvailable(ModLevelObject level, Item item)
    {
      foreach (DialogueObject dialogue in level.Dialogues)
      {
        if (GameManager.GeneralProgression.PlayedOneShotDialogues.ContainsEquatable(dialogue.name))
        {
          foreach (DialogueLine dialogueLine in dialogue.DialogueLines)
          {
            if (dialogueLine is CustomDialogueLine)
            {
              var customDialogueLine = (CustomDialogueLine)dialogueLine;
              var tools = customDialogueLine.Tools;
              switch (item)
              {
                case Item.Trap:
                  if (tools.Traps > 0)
                  {
                    return true;
                  }
                  break;

                case Item.Pickaxe:
                  if (tools.Pickaxes > 0)
                  {
                    return true;
                  }
                  break;

                case Item.Shovel:
                  if (tools.Shovels > 0)
                  {
                    return true;
                  }
                  break;
                  
                case Item.Carrot:
                  if (tools.Carrots > 0)
                  {
                    return true;
                  }
                  break;
              }
            }
          }
        }
      }
      return false;
    }
  }

  [HarmonyPatch(typeof(PaqueretteItemExtension), "IsInitialCountNonZero")]
  internal class IsInitialCountNonZeroPatch
  {
    static void Postfix(ref bool __result, Item item)
    {
      if (__result == false && GameManager.CurrentLevel.BaseData is ModLevelObject)
      {
        var level = (ModLevelObject)GameManager.CurrentLevel.BaseData;
        __result = IsAvailableInContextPatch.IsDialogueItemAvailable(level, item);
      }
    }
  }
}