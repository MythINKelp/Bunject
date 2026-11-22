using System;
using System.Collections.Generic;
using Dialogue;
using HarmonyLib;
using Misc;
using UnityEngine;

namespace Bunject.Dialogue
{
  public class CustomDialogueObject : DialogueObject
  {
    public CustomDialogueObject()
    {
      Traverse.Create(this).Field("isBunnyIntroFlee").SetValue(false);
      Traverse.Create(this).Field("isResetTutorial").SetValue(false);
      Traverse.Create(this).Field("isElevatorTutorial").SetValue(false);
      Traverse.Create(this).Field("isTrapTutorial").SetValue(false);
      Traverse.Create(this).Field("isPickaxeTutorial").SetValue(false);
      Traverse.Create(this).Field("isShovelTutorial").SetValue(false);
      Traverse.Create(this).Field("isCarrotTutorial").SetValue(false);
      Traverse.Create(this).Field("isItemSwitchTutorial").SetValue(false);
      Traverse.Create(this).Field("unlocksBackToSurface").SetValue(false);
      Traverse.Create(this).Field("isPowerCatchup").SetValue(false);
      Traverse.Create(this).Field("requiresNoSkipTip").SetValue(false);
      Traverse.Create(this).Field("requiresNoResetTutorial").SetValue(false);
      Traverse.Create(this).Field("requiresNoCarrotTutorial").SetValue(false);
      Traverse.Create(this).Field("requiresAvailableOphelineDialogue").SetValue(false);
      Traverse.Create(this).Field("shouldNotPlayAgainAfterReset").SetValue(false);
      Traverse.Create(this).Field("shouldPlayOnlyOncePerSave").SetValue(false);
      Traverse.Create(this).Field("triggersVoidEffect").SetValue(false);
      Traverse.Create(this).Field("endsWithChoice").SetValue(false);
      Traverse.Create(this).Field("isSurfaceElevatorChoice").SetValue(false);
    }

    public new IReadOnlyList<DialogueLine> DialogueLines
    {
      get { return base.DialogueLines; }
      set { Traverse.Create(this).Field("dialogueLines").SetValue(value); }
    }

    public new bool RequiresBunnyToPassBeforePlaying
    {
      get { return base.RequiresBunnyToPassBeforePlaying; }
      set { Traverse.Create(this).Field("requiresBunnyToPassBeforePlaying").SetValue(value); }
    }

    public new bool RequiresNoBunnyCaptureThisLevel
    {
      get { return base.RequiresNoBunnyCaptureThisLevel; }
      set { Traverse.Create(this).Field("requiresNoBunnyCaptureThisLevel").SetValue(value); }
    }

    public new bool RequiresSpecificEntryDirection
    {
      get { return base.RequiresSpecificEntryDirection; }
      set { Traverse.Create(this).Field("requiresSpecificEntryDirection").SetValue(value); }
    }

    public new Direction SpecificEntryDirection
    {
      get { return base.SpecificEntryDirection; }
      set { Traverse.Create(this).Field("specificEntryDirection").SetValue(value); }
    }

    public new bool RequiresBunnyAtSpecificRelativePosition
    {
      get { return base.RequiresBunnyAtSpecificRelativePosition; }
      set { Traverse.Create(this).Field("requiresBunnyAtSpecificRelativePosition").SetValue(value); }
    }

    public new List<Vector2Int> SpecificBunnyRelativePositions
    {
      get { return base.SpecificBunnyRelativePositions; }
      set { Traverse.Create(this).Field("specificBunnyRelativePositions").SetValue(value); }
    }

    public new bool ShouldForceTurnPaqueretteAtStart
    {
      get { return base.ShouldForceTurnPaqueretteAtStart; }
      set { Traverse.Create(this).Field("shouldForceTurnPaqueretteAtStart").SetValue(value); }
    }
    
    public new Direction ForceTurnPaqueretteDirection
    {
      get { return base.ForceTurnPaqueretteDirection; }
      set { Traverse.Create(this).Field("forceTurnPaqueretteDirection").SetValue(value); }
    }
    
    public new bool ShouldPlayOnlyOncePerSave
    {
      get { return base.ShouldPlayOnlyOncePerSave; }
      set { Traverse.Create(this).Field("shouldPlayOnlyOncePerSave").SetValue(value); }
    }
  }
}