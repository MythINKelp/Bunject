using Bunject.Dialogue;
using Bunject.Levels;
using Bunject.NewYardSystem.Utility;
using Dialogue;
using Levels;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using UnityEngine;

namespace Bunject.NewYardSystem.Model
{
  public class LevelMetadata
  {
    public string Name { get; set; }

    public bool LiveReloading { get; set; } = false;
    public LevelTools Tools { get; set; } = new LevelTools();

    public string Style { get; set; }
    public bool IsTemple { get; set; }
    public bool IsHell { get; set; }

    public string Content { get; set; }

    public bool IsWebLevel { get; set; }

    public List<LevelHint> Hints { get; set; }

    public int[] Teleport
    {
      get
      {
        if (TeleportX.HasValue && TeleportY.HasValue)
          return new int[] { TeleportX.Value, TeleportY.Value };
        return null; 
      }
      set
      {
        if (value != null && value.Length == 2)
        {
          TeleportX = value[0];
          TeleportY = value[1];
        }
        else
        {
          TeleportX = null;
          TeleportY = null;
        }
      }
    }

    public int? TeleportX { get; set; }
    public int? TeleportY { get; set; }

    public Vector2Int ToTeleportPosition()
    {
      if (TeleportX.HasValue && TeleportY.HasValue)
        return new Vector2Int(TeleportX.Value, TeleportY.Value);
      return new Vector2Int(-1, -1);
    }

    public List<LevelDialogue> Dialogues { get; set; } = new List<LevelDialogue>();
    public List<LevelNPCDialogue> OphelineDialogues { get; set; } = new List<LevelNPCDialogue>();
    public List<LevelNPCDialogue> HerbeDialogues { get; set; } = new List<LevelNPCDialogue>();
  }

  public class LevelTools
  {
    public int Traps { get; set; }
    public int Pickaxes { get; set; }
    public int Carrots { get; set; }
    public int Shovels { get; set; }
  }
  
  public class LevelHint
  {
    public int[] Position
    {
      get
      {
        return new int[] { PositionX, PositionY };
      }
      set
      {
        if (value != null && value.Length == 2)
        {
          PositionX = value[0];
          PositionY = value[1];
        }
        else
        {
          PositionX = 0;
          PositionY = 0;
        }
      }
    }

    public int PositionX { get; set; }
    public int PositionY { get; set; }
    public Misc.Direction Orientation { get; set; } = Misc.Direction.Down;
  }
  
  public class LevelDialogue
  {
    public string Name { get; set; }
    public List<CustomDialogueLine> Lines { get; set; }

    public bool RequiresBunnyToPassBeforePlaying { get; set; }

    public bool RequiresNoBunnyCaptureThisLevel { get; set; }

    public bool RequiresSpecificEntryDirection { get; set; }
    public Misc.Direction SpecificEntryDirection { get; set; } = Misc.Direction.Down;

    public bool RequiresBunnyAtSpecificRelativePosition { get; set; }
    public List<int[]> SpecificBunnyRelativePositions { get; set; }

    public bool ShouldForceTurnPaqueretteAtStart { get; set; }
    public Misc.Direction ForceTurnPaqueretteDirection { get; set; }

    public bool ShouldOnlyPlayOnce { get; set; }

    // Need this ugly workaround so CustomDialogueObjects can be instantiated properly
    public CustomDialogueObject ConvertToCustomDialogueObject()
    {
      CustomDialogueObject result = (CustomDialogueObject)ScriptableObject.CreateInstance(typeof(CustomDialogueObject));
      result.name = "BNYS::" + this.Name;
      if (this.Lines != null)
      {
        result.DialogueLines = this.Lines.ConvertAll(line => (DialogueLine)line);
      }
      result.RequiresBunnyToPassBeforePlaying = this.RequiresBunnyToPassBeforePlaying;
      result.RequiresNoBunnyCaptureThisLevel = this.RequiresNoBunnyCaptureThisLevel;
      result.RequiresSpecificEntryDirection = this.RequiresSpecificEntryDirection;
      result.SpecificEntryDirection = this.SpecificEntryDirection;
      result.RequiresBunnyAtSpecificRelativePosition = this.RequiresBunnyAtSpecificRelativePosition;
      result.SpecificBunnyRelativePositions = new List<Vector2Int>();
      if (this.SpecificBunnyRelativePositions != null && this.SpecificBunnyRelativePositions.Count > 0)
      {
        foreach (int[] specificBunnyRelativePosition in this.SpecificBunnyRelativePositions)
        {
          if (specificBunnyRelativePosition.Length > 1)
          {
            result.SpecificBunnyRelativePositions.Add(new Vector2Int(specificBunnyRelativePosition[0], specificBunnyRelativePosition[1]));
          }
        }
      }
      result.ShouldForceTurnPaqueretteAtStart = this.ShouldForceTurnPaqueretteAtStart;
      result.ForceTurnPaqueretteDirection = this.ForceTurnPaqueretteDirection;
      result.ShouldPlayOnlyOncePerSave = this.ShouldOnlyPlayOnce;
      return result;
    }
  }

  public class LevelNPCDialogue
  {
    public string Name { get; set; } = "";
    public List<CustomDialogueLine> Lines { get; set; }

    public int RequiredCaptures { get; set; } = 0;
    public int RequiredBabies { get; set; } = 0;
    public int RequiredHomeCaptures { get; set; } = 0;

    public bool ShouldOnlyPlayOnce { get; set; } = false;

    public NPCDialogueObject ConvertToNPCDialogueObject()
    {
      NPCDialogueObject result = (NPCDialogueObject)ScriptableObject.CreateInstance(typeof(NPCDialogueObject));
      result.name = "BNYS::" + this.Name;
      if (this.Lines != null)
      {
        result.DialogueLines = this.Lines.ConvertAll(line => (DialogueLine)line);
      }
      result.RequiredCaptures = this.RequiredCaptures;
      result.RequiredBabies = this.RequiredBabies;
      result.RequiredHomeCaptures = this.RequiredHomeCaptures;
      result.ShouldOnlyPlayOnce = this.ShouldOnlyPlayOnce;
      return result;
    }
  }
}
