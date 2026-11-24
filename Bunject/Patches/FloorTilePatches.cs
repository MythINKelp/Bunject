using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using Bunject.Dialogue;
using HarmonyLib;
using Tiling.Behaviour;

namespace Bunject.Patches.FloorTilePatches
{
  [HarmonyPatch(typeof(FloorTile), nameof(FloorTile.CheckForDialogue))]
  internal class CheckForDialoguePatch
  {
    private static MethodInfo getLevelsCompletedOnce = typeof(GeneralProgression).GetProperty(nameof(GeneralProgression.LevelsCompletedOnce)).GetGetMethod();
    
    // replace the "branch if (this level is completed)" flag with a 
    // "branch if (this level is completed) and (this tile does not have a tool dialogue)"
    private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
    {
      int stage = 0;
      foreach (var instruction in instructions)
      {
        switch (stage)
        {
          case 0:
            // find the (this level is in the list of completed levels) instructions
            if (instruction.Calls(getLevelsCompletedOnce))
            {
              stage++;
            }
            break;
          case 1:
            // now find the branch instruction and insert the code just before it
            if (instruction.opcode == OpCodes.Brtrue)
            {
              // (instructions returning whether or not this level is completed)
              yield return new CodeInstruction(OpCodes.Ldarg_0);
              yield return CodeInstruction.Call(typeof(CheckForDialoguePatch), nameof(TileHasToolDialogue));
              yield return new CodeInstruction(OpCodes.Not);
              yield return new CodeInstruction(OpCodes.And);
              // brtrue instruction already in the code
              stage++;
            }
            break;
        }
        yield return instruction;

        // For some reason Harmony changes how the method works if a leave instruction directly preceeds an exception block boundary
        // This can be fixed by adding a nop after the leave instruction
        // Without this fix the RequiresBunnyAtSpecificRelativePosition condition will not work
        if (instruction.opcode == OpCodes.Leave || instruction.opcode == OpCodes.Leave_S)
        {
          yield return new CodeInstruction(OpCodes.Nop);
        }
      }
    }

    private static bool TileHasToolDialogue(FloorTile tile)
    {
      if (!GameManager.CurrentLevel.BaseData.TryGetDialogue(tile.TriggersDialogue, out var dialogue))
      {
        return false;
      }
      
      foreach (var line in dialogue.DialogueLines)
      {
        if (line is CustomDialogueLine)
        {
          var customDialogueLine = (CustomDialogueLine)line;
          var tools = customDialogueLine.Tools;
          if ((tools.Traps > 0) || (tools.Pickaxes > 0) || (tools.Shovels > 0) || (tools.Carrots > 0))
          {
            return true;
          }
        }
      }
      return false;
    }
  }
}