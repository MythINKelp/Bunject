using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using Bunject.Dialogue;
using Dialogue;
using HarmonyLib;
using Items;
using Localization;
using UnityEngine;

namespace Bunject.Patches.UIControllerPatches
{
  [HarmonyPatch(typeof(UIController), "DisplayDialogue")]
  internal class ConvertToCustomDialoguePatch
  {
    static bool Prefix(ref DialogueObject dialogue)
    {
      // Filter out custom dialogue from regular dialogue
      if (dialogue is CustomDialogueObject)
      {
        var customDialogue = (CustomDialogueObject)dialogue;

        // Do not display dialogue if there are no lines
        if (customDialogue.DialogueLines == null || customDialogue.DialogueLines.Count == 0)
        {
          UnityEngine.Debug.LogWarning("Dialogue has no DialogueLines.");
          return false;
        }

        var convertedLines = new List<DialogueLine>();
        foreach (DialogueLine dialogueLine in customDialogue.DialogueLines)
        {
          CustomDialogueLine customDialogueLine = (CustomDialogueLine)dialogueLine;
          if (!HeadshotManager.TryResolve(customDialogueLine.Headshot, out HeadshotObject headshot))
          {
            UnityEngine.Debug.LogWarning($"HeadshotObject {customDialogueLine.Headshot} not found.");
          }
          customDialogueLine.HeadshotObject = headshot;

          // Prevent DialogueLines with no Content field/null Content field from breaking the game
          if (customDialogueLine.Content == null)
          {
            customDialogueLine.Content = "";
          }

          convertedLines.Add((DialogueLine)customDialogueLine);
        }
        customDialogue.DialogueLines = convertedLines;
        dialogue = customDialogue;
      }
      return true;
    }
  }

  [HarmonyPatch(typeof(UIController), "DisplayDialogue")]
  internal class CustomDialogueLocalizationPatch
  {
    // Prevent localization from causing issues with custom dialogues
    static MethodInfo getLanguageLocalizationObjectLoadedIn = typeof(AssetsManager).GetProperty("LanguageLocalizationObjectLoadedIn").GetGetMethod();

    static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
    {
      int stage = 0;
      foreach (var instruction in instructions)
      {
        switch (stage)
        {
          // Look for localization
          case 0:
            if (instruction.Calls(getLanguageLocalizationObjectLoadedIn))
            {
              stage++;
            }
            break;

          // Look for branch instruction
          case 1:
            if (instruction.opcode == OpCodes.Ldloca_S)
            {
              stage++;
            }
            break;
          case 2:
            stage++;
            break;

          // Skip localization if the dialogue is custom, then proceed as normal
          case 3:
            yield return instruction;

            yield return new CodeInstruction(OpCodes.Ldarg_1);
            yield return new CodeInstruction(OpCodes.Isinst, typeof(CustomDialogueObject));
            yield return new CodeInstruction(OpCodes.Brtrue_S, instruction.operand);

            stage++;
            continue;
        }
        yield return instruction;
      }
    }
  }

  // Allow tools in dialogues
  [HarmonyPatch(typeof(UIController), "DoDialogueLineEffects")]
  internal class DoDialogueLineEffectsPatch
  {
    static MethodInfo enableItemIcon = AccessTools.Method(typeof(UIController), "EnableItemIcon");

    static void Postfix(DialogueLine dialogueLine)
    {
      if (dialogueLine is CustomDialogueLine)
      {
        var customDialogueLine = (CustomDialogueLine)dialogueLine;
        var dialogueTools = customDialogueLine.Tools;
        if (dialogueTools.Traps > 0)
        {
          for (var i = 0; i < dialogueTools.Traps; i++)
          {
            GameManager.Inventory.HandleTrapRetrieval(false, true);
          }
          GameManager.PaqueretteController.ForceSwitchItem(Item.Trap);
          EnableItemIcon(Item.Trap);
        }
        if (dialogueTools.Pickaxes > 0)
        {
          for (var i = 0; i < dialogueTools.Pickaxes; i++)
          {
            GameManager.Inventory.HandlePickaxeRetrieval(false, true);
          }
          GameManager.PaqueretteController.ForceSwitchItem(Item.Pickaxe);
          EnableItemIcon(Item.Pickaxe);
					PaqueretteActionResolver.CheckPickaxeOverlayDisplayConditions();
        }
        if (dialogueTools.Shovels > 0)
        {
          for (var i = 0; i < dialogueTools.Shovels; i++)
          {
            GameManager.Inventory.HandleShovelRetrieval(false, true);
          }
          GameManager.PaqueretteController.ForceSwitchItem(Item.Shovel);
          EnableItemIcon(Item.Shovel);
        }
        if (dialogueTools.Carrots > 0)
        {
          for (var i = 0; i < dialogueTools.Carrots; i++)
          {
            GameManager.Inventory.HandleCarrotRetrieval(false, true);
          }
          GameManager.PaqueretteController.ForceSwitchItem(Item.Carrot);
          EnableItemIcon(Item.Carrot);
        }
      }
    }

    static void EnableItemIcon(Item item)
    {
      enableItemIcon.Invoke(GameManager.UIController, new object[] { item, true });
    }
  }
}