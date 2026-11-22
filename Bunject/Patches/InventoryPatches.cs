using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using Bunject.Dialogue;
using Bunject.Levels;
using Dialogue;
using HarmonyLib;
using Levels;
using Misc;

namespace Bunject.Patches.InventoryPatches
{
  [HarmonyPatch(typeof(Inventory), nameof(Inventory.HandleLevelReset))]
  internal class HandleLevelResetPatch
  {
    // Put items from dialogues into Paquerette's inventory on level load
    static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
    {
      int stage = 0;
      foreach (var instruction in instructions)
      {
        switch (stage)
        {
          // Inserting the custom behaviour just after the tools are initially set
          case 0:
          case 1:
            if (instruction.opcode == OpCodes.Ldc_I4_0)
            {
              stage++;
            }
            break;
          case 2:
            if (instruction.opcode == OpCodes.Ldc_I4_0)
            {
              yield return new CodeInstruction(OpCodes.Ldarg_1);
              yield return CodeInstruction.Call(typeof(HandleLevelResetPatch), nameof(GiveDialogueTools));
              stage++;
            }
            break;
        }
        yield return instruction;
      }
    }

    // Add tools to the player's inventory based on what dialogues were seen
    static void GiveDialogueTools(Level level)
    {
      if (level.BaseData is ModLevelObject)
      {
        var traverse = Traverse.Create(GameManager.Inventory);
        var modLevel = (ModLevelObject)level.BaseData;
        foreach (DialogueObject dialogue in modLevel.Dialogues)
        {
          if (GameManager.GeneralProgression.PlayedOneShotDialogues.ContainsEquatable(dialogue.name))
          {
            foreach (DialogueLine dialogueLine in dialogue.DialogueLines)
            {
              if (dialogueLine is CustomDialogueLine)
              {
                var customDialogueLine = (CustomDialogueLine)dialogueLine;
                var tools = customDialogueLine.Tools;
                traverse.Property("NumberOfTraps").SetValue(GameManager.Inventory.NumberOfTraps + tools.Traps);
                traverse.Property("NumberOfPickaxes").SetValue(GameManager.Inventory.NumberOfPickaxes + tools.Pickaxes);
                traverse.Property("NumberOfShovels").SetValue(GameManager.Inventory.NumberOfShovels + tools.Shovels);
                traverse.Property("NumberOfCarrots").SetValue(GameManager.Inventory.NumberOfCarrots + tools.Carrots);
              }
            }
          }
        }
      }
    }
  }
}