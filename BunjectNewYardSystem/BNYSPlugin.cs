using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using Bunburrows;
using Bunject;
using Bunject.Computer;
using Bunject.Dialogue;
using Bunject.Internal;
using Bunject.Levels;
using Bunject.Menu;
using Bunject.Monitoring;
using Bunject.NewYardSystem.Computer;
using Bunject.NewYardSystem.Exceptions;
using Bunject.NewYardSystem.Internal;
using Bunject.NewYardSystem.Levels;
using Bunject.NewYardSystem.Levels.Archive;
using Bunject.NewYardSystem.Levels.Local;
using Bunject.NewYardSystem.Levels.Web;
using Bunject.NewYardSystem.Model;
using Bunject.NewYardSystem.Resources;
using Bunject.NewYardSystem.Utility;
using Bunject.Tiling;
using Characters.Bunny.Data;
using Dialogue;
using HarmonyLib;
using Levels;
using Misc;
using Newtonsoft.Json;
using System;
using Tiling.Behaviour;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net;
using System.Reflection;
using System.Text;
using UnityEngine;
using UnityEngine.Tilemaps;
using static UnityEngine.ParticleSystem;

namespace Bunject.NewYardSystem
{
  [BepInPlugin(pluginGuid, pluginName, pluginVersion)]
  public class BNYSPlugin : BaseUnityPlugin, IBunjectorPlugin, IMonitor, IMenuSource, IComputerPageSource
  {
    public const string pluginGuid = "sergedev.bunject.newyardsystem";
    public const string pluginName = "BNYS";
    public const string pluginVersion = "1.3.0";

    public static string pluginsDirectory = Path.GetFullPath(Path.Combine(Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location), @"..\"));
    public static string rootDirectory = Path.GetFullPath(Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location));
    public static string inlineDirectory = Path.Combine(rootDirectory, "BNYS");

    private List<CustomWorld> CustomWorlds;
    private List<IModBunburrow> AllModBurrows;
    private List<BNYSModBunburrowBase> BNYSModBurrows;

    public new ManualLogSource Logger => base.Logger;

    private CustomWorld CurrentCustomWorld = null;

    private CustomWorld WorldPendingDeletion = null;

    public void Awake()
    {
      Logger.LogInfo($"Bunject New Yard System [BNYS] Plugin Awakened. v{pluginVersion}");

      // Get cached worlds first - to preserve registration order (which preserves IDs generated)
      // Not the most elegant solution, but I'm just trying to get it functional for now.
      var cache = new CustomBunburrowCache();

      try
      {
        CustomWorlds = LoadCustomWorlds().ToList();
      }
      catch (Exception e)
      {
        Logger.LogError("Error caught on loading custom burrows... ");
        Logger.LogError(e.Message);
        Logger.LogError(e);

        return;
      }

      var modBunburrows = new List<IModBunburrow>();

      if (CustomWorlds.Count > 0)
      {
        Logger.LogInfo("Initial Load - Building Cached Burrows!");

        foreach (var cachedBurrow in cache.CustomBurrows)
        {
          var burrowModel = CustomWorlds.Where(cw => cachedBurrow.World == cw.Title || string.IsNullOrEmpty(cachedBurrow.World))
            .SelectMany(cw => cw.Burrows).FirstOrDefault(b => b.Name == cachedBurrow.Name);

          var customWorld = CustomWorlds.FirstOrDefault(cw => cw.Burrows.Contains(burrowModel));
          if (burrowModel != null)
          {
            Logger.LogInfo($"Cached Burrow : {burrowModel.Name} found!");

            cachedBurrow.World = customWorld.Title;
            cachedBurrow.Prefix = customWorld.Prefix;
            cachedBurrow.Indicator = burrowModel.Indicator; //update cached indicator if needed

            modBunburrows.Add(customWorld.GenerateBunburrow(this, burrowModel.Name)); //   new BNYSModBunburrow(this, customWorld, burrowModel));
          }
          else
          {
            Logger.LogInfo($"Cached Burrow : {cachedBurrow.Name} NOT found!");
            // assume levelpack was removed... register it for save file's sake
            modBunburrows.Add(new BNYSLostBunburrow(cachedBurrow));
          }
        }

        Logger.LogInfo("Initial Load - Building Uncached Burrows!");

        foreach (var burrow in CustomWorlds.SelectMany(cw => cw.Burrows))
        {
          var customWorld = CustomWorlds.FirstOrDefault(cw => cw.Burrows.Contains(burrow));
          var cachedBurrow = cache.CustomBurrows.FirstOrDefault(cb => cb.Name == burrow.Name && (cb.World == customWorld.Title || string.IsNullOrEmpty(cb.World)));
          if (cachedBurrow == null)
          {
            Logger.LogInfo($"Uncached Burrow : {burrow.Name} built!");
            var newBurrow = customWorld.GenerateBunburrow(this, burrow.Name);
            modBunburrows.Add(newBurrow);
            cache.CacheBunburrow(newBurrow);
          }
        }

        cache.SaveCache();

        AllModBurrows = modBunburrows.ToList();
        BNYSModBurrows = modBunburrows.OfType<BNYSModBunburrowBase>().ToList();

        LinkLevelLists(BNYSModBurrows);

        BunjectAPI.RegisterPlugin(this);

        Logger.LogInfo("Initial Load Finished!");
      }
      else
      {
        Logger.LogInfo("All worlds empty! Please configure a burrow with a surface entrance, and depth of at least 1!");
      }
    }

    public void OnAssetsLoaded() { }

    //IBunjector Members
    // IMPORTANT NOTE: DEPTH is 1-indexed.
    public void OnProgressionLoaded(GeneralProgression progression)
    {
      if (CurrentCustomWorld != null)
      {
        progression.HandleBunburrowSignsDiscovery();
        progression.HandleBackToSurfaceUnlock();
        progression.HandleOphelinePortableComputerUnlock();
        progression.HandleMapUnlock();
        progression.HandleSeenBootAnimation();
        progression.HandleHerbeDialoguePlayed("HerbeFoundInTemple"); // Prevents Hèrbe from going missing

        if (CurrentCustomWorld.TeleportAnywhereUnlocked)
          progression.HandleTeleportAnywhereUnlock();
      }
    }

    public LevelObject OnLevelLoad(LevelObject level, LevelIdentity identity)
    {
      return level;
    }

    public string OnLevelTitle(string title, LevelIdentity _, bool useWhite)
    {
      return title;
    }

    public void OnBunnyCapture(BunnyIdentity bunnyIdentity, bool wasHomeCapture) { }

    public void OnPowerTile(PowerUnlockTile tile, LevelIdentity identity, Action dismiss)
    {
      if (!identity.Bunburrow.IsCustomBunburrow())
        return;

      if (GameManager.GeneralProgression.HasUnlockedTeleportAnywhere)
      {
        dismiss();
        return;
      }

      GameManager.UIController.DisplayDialogue(BuildTeleportUnlockDialogue(), null);

      Action<bool> onDialogueEnd = null;
      onDialogueEnd = _ =>
      {
        GameManager.UIController.OnDialogueEnd -= onDialogueEnd;
        dismiss(); // Dismisses the powerup sprite
      };
      GameManager.UIController.OnDialogueEnd += onDialogueEnd;
    }

    public bool TryResolvePowerTileSprite(PowerUnlockTile tile, LevelIdentity identity, out TileBase sprite)
    {
      if (!identity.Bunburrow.IsCustomBunburrow())
      {
        sprite = null;
        return false;
      }

      sprite = GameManager.GeneralProgression.HasUnlockedTeleportAnywhere
        ? null
        : AssetsManager.TeleportAnywherePowerUnlockAnimatedTile;
      return true;
    }

    private static CustomDialogueObject BuildTeleportUnlockDialogue()
    {
      var dialogue = ScriptableObject.CreateInstance<CustomDialogueObject>();
      dialogue.name = "BNYS::TeleportAnywhereUnlock";
      dialogue.DialogueLines = new List<DialogueLine>
      {
        new CustomDialogueLine { Headshot = "PaqueretteHappy", Content = "Teleport Powers Unlocked!" }
      };

      Traverse.Create(dialogue).Field("unlocksTeleportAnywhere").SetValue(true);
      return dialogue;
    }

    public void OnMainMenu()
    {
      if (CurrentCustomWorld != null)
      {
        // Undo all major changes
        BunjectAPI.ClearRegisters();

        var assetManager = Traverse.Create(typeof(AssetsManager));
        var middleBase = assetManager.Field<LevelObject>("surfaceMiddleLevelBase").Value;
        var middleExpanded = assetManager.Field<LevelObject>("surfaceMiddleLevelExpanded").Value;
        var middleNoHerbe = assetManager.Field<LevelObject>("surfaceMiddleLevelExpanded2NoHerbe").Value;
        var middleHerbe = assetManager.Field<LevelObject>("surfaceMiddleLevelExpanded2Herbe").Value;
        ExtendedSurfaceLevelGenerator.LinkSurface(middleBase, AssetsManager.SurfaceRightLevel);
        ExtendedSurfaceLevelGenerator.LinkSurface(middleExpanded, AssetsManager.SurfaceRightLevel);
        ExtendedSurfaceLevelGenerator.LinkSurface(middleNoHerbe, AssetsManager.SurfaceRightLevel);
        ExtendedSurfaceLevelGenerator.LinkSurface(middleHerbe, AssetsManager.SurfaceRightLevel);

        CurrentCustomWorld = null;
      }
    }

    public void OnShowCredits() { }

    private EmergencyLevelsList emergencyList;
    public LevelsList LoadEmergencyLevelsList(LevelsList original)
    {
      if (emergencyList == null)
      {
        emergencyList = ScriptableObject.CreateInstance<EmergencyLevelsList>();
        emergencyList.Bnys = this;
      }
      return emergencyList;
    }

    public void LoadBurrowSurfaceLevel(string listName, LevelObject otherwise)
    {
      //Logger.LogInfo($"Rappelling from {listName}");
      var burrow = CustomWorlds.SelectMany(cw => cw.Burrows).FirstOrDefault(b => b.Name == listName);

      if (burrow != null)
      {
        //Logger.LogInfo($"Burrow found!");
        var world = CustomWorlds.FirstOrDefault(cw => cw.Burrows.Contains(burrow));
        var surfaceIndex = world.Burrows.Where(b => b.Depth > 0 && b.HasSurfaceEntry).ToList().IndexOf(burrow);
        /*
        if (surfaceIndex >= 0 && (surfaceIndex / 3) < world.GeneratedSurfaceLevels.Count)
          return world.GeneratedSurfaceLevels[surfaceIndex / 3];*/

        Logger.LogWarning($"Burrow was not surfaceable / not in the list of generated surface levels!");
      }

      //return base.LoadBurrowSurfaceLevel(listName, otherwise);
    }
    //IBunjector Members End

    public IEnumerable<CustomWorld> LoadCustomWorlds()
    {
      // loop through all subfolders of our root
      if (Directory.Exists(inlineDirectory))
      {
        foreach (var directory in Directory.EnumerateDirectories(inlineDirectory))
        {
          var configFile = Path.Combine(directory, "config.json");
          if (File.Exists(configFile))
          {
            CustomWorld world = LoadWorldConfig(configFile);
            if (world != null)
            {
              if (!world.Burrows.Any(b => b.HasSurfaceEntry && b.Depth > 0) || !world.Enabled)
                continue;

              // TODO REPLACE THIS WITH BETTER. 
              foreach (var burrow in world.Burrows)
              {
                PatchBurrowDetails(directory, burrow);
              }

              yield return world;
            }
          }
        }
      }

      // loop through all other plugins installed
      foreach (var directory in Directory.EnumerateDirectories(pluginsDirectory))
      {
        foreach (var bnysFile in Directory.EnumerateFiles(directory, "*.bnys"))
        {
          CustomWorld world = LoadWorldConfigFromArchive(bnysFile);
          if (world != null)
          {
            if (!world.Burrows.Any(b => b.HasSurfaceEntry && b.Depth > 0) || !world.Enabled)
              continue;

            yield return world;
          }
        }
      }
    }

    private CustomWorld LoadWorldConfig(string filename)
    {
      CustomWorld world = null;
      try
      {
        using (var reader = new StreamReader(filename))
        {
          world = (CustomWorld)new JsonSerializer().Deserialize(reader, typeof(LocalCustomWorld));
        }
      }
      catch (Exception e)
      {
        Logger.LogError("Following file could not be parsed");
        Logger.LogError(filename);
        Logger.LogError(e.Message);
        Logger.LogError(e);
      }

      if (string.IsNullOrEmpty(world.Title))
        world.Title = "Untitled";

      if (!string.IsNullOrEmpty(world.ProxyURL))
      {
        try
        {
          return LoadProxyWorld(world);
        }
        catch (Exception e)
        {
          Logger.LogError("Error loading Proxy World config.json");
          Logger.LogError(filename);
          Logger.LogError(e.Message);
          Logger.LogError(e);
        }
      }

      return world;
    }

    private CustomWorld LoadWorldConfigFromArchive(string archivePath)
    {
      var archive = ZipFile.OpenRead(archivePath);
      Logger.LogInfo("Opening World Archive: " + archivePath);

      var config = archive.GetEntry("config.json");
      if (config == null)
      {
        Logger.LogError("Archive " + Path.GetFileName(archivePath) + " is missing config.json.");
        Logger.LogError("Please double check that config.json is at the root level of the .bnys (zip) file.");
        return null;
      }

      ArchiveCustomWorld world = null;
      try
      {
        using (var stream = config.Open())
        using (var reader = new StreamReader(stream))
        {
          world = (ArchiveCustomWorld)new JsonSerializer().Deserialize(reader, typeof(ArchiveCustomWorld));
        }
      }
      catch (Exception e)
      {
        Logger.LogError("Archive " + Path.GetFileName(archivePath) + " config.json file could not be parsed: ");
        Logger.LogError(e.Message);
        Logger.LogError(e);
      }

      world.Archive = archive;

      if (string.IsNullOrEmpty(world.Title))
        world.Title = "Untitled";

      if (!string.IsNullOrEmpty(world.ProxyURL))
      {
        try
        {
          return LoadProxyWorld(world);
        }
        catch (Exception e)
        {
          Logger.LogError("Error loading Archive Proxy World config.json - " + Path.GetFileName(archivePath));
          Logger.LogError(e.Message);
          Logger.LogError(e);
        }
      }

      return world;
    }

    private WebCustomWorld LoadProxyWorld(CustomWorld basis)
    {
      var uri = new Uri(basis.ProxyURL);
      var configUri = new Uri(uri, "config.json");

      var world = configUri.Load<WebCustomWorld>();

      if (world != null)
      {
        world.ProxyUri = uri;
        world.ProxyURL = basis.ProxyURL;

        world.Enabled = world.Enabled || basis.Enabled;
        world.LiveReloading = false; // force global reload off.

        if (string.IsNullOrEmpty(world.Title))
          world.Title = basis.Title;

        if (world.Burrows != null)
        {
          foreach (var burrow in world.Burrows)
          {
            burrow.ProxyUri = new Uri(world.ProxyUri, $"{burrow.Directory}/");
          }

          if (world.SurfaceEntries == null || world.SurfaceEntries.Count == 0)
          {
            world.SurfaceEntries = basis.SurfaceEntries;
          }
        }

        if (string.IsNullOrEmpty(world.Title))
          world.Title = "Untitled";
      }

      return world;
    }

    private void PatchBurrowDetails(string directory, Burrow burrow)
    {
      if (!Path.IsPathRooted(burrow.Directory))
      {
        burrow.Directory = Path.Combine(directory, burrow.Directory);
        Logger.LogInfo("Burrow Directory: " +  burrow.Directory);
      }
    }

    private void GenerateSurfaceLevels(CustomWorld world)
    {
      var assetManager = Traverse.Create(typeof(AssetsManager));
      var middleBase = assetManager.Field<LevelObject>("surfaceMiddleLevelBase").Value;
      var middleExpanded = assetManager.Field<LevelObject>("surfaceMiddleLevelExpanded").Value;
      var middleNoHerbe = assetManager.Field<LevelObject>("surfaceMiddleLevelExpanded2NoHerbe").Value;
      var middleHerbe = assetManager.Field<LevelObject>("surfaceMiddleLevelExpanded2Herbe").Value;

      var previous = middleBase;

      try
      {
        ExtendedSurfaceLevelGenerator.CreateSurfaceLevels(world, BNYSModBurrows.Where(b => b.World == world).ToList(), previous);
      }
      catch (Exception e)
      {
        Logger.LogError($"Error occurred while generating surface levels for world: {world.Title}");
        Logger.LogError(e.Message);
        Logger.LogError(e);
      }

      previous = world.GeneratedSurfaceLevels.LastOrDefault() ?? previous;
      var first = world.GeneratedSurfaceLevels.FirstOrDefault() ?? previous;

      ExtendedSurfaceLevelGenerator.LinkSurface(middleBase, first);
      ExtendedSurfaceLevelGenerator.LinkSurface(middleExpanded, first);
      ExtendedSurfaceLevelGenerator.LinkSurface(middleNoHerbe, first);
      ExtendedSurfaceLevelGenerator.LinkSurface(middleHerbe, first);

      PatchLevelAsEndcap(previous);
    }

    private void PatchLevelAsEndcap(LevelObject endcapLevel)
    {
      Traverse.Create(endcapLevel).Field("specificBackground").SetValue(SurfaceBurrowsPatch.EndingBackground);
    }

    private void LinkLevelLists(List<BNYSModBunburrowBase> burrows)
    {
      foreach (var burrow in burrows)
      {
        if (!string.IsNullOrEmpty(burrow.BurrowModel.Links.Left))
        {
          var target = burrows.FirstOrDefault(bb => bb.LocalName == burrow.BurrowModel.Links.Left);
          if (target != null)
            burrow.GetLevels().AdjacentBunburrows.SetPart(Direction.Left, target.GetLevels());
        }
        if (!string.IsNullOrEmpty(burrow.BurrowModel.Links.Up))
        {
          var target = burrows.FirstOrDefault(bb => bb.LocalName == burrow.BurrowModel.Links.Up);
          if (target != null)
            burrow.GetLevels().AdjacentBunburrows.SetPart(Direction.Up, target.GetLevels());
        }
        if (!string.IsNullOrEmpty(burrow.BurrowModel.Links.Right))
        {
          var target = burrows.FirstOrDefault(bb => bb.LocalName == burrow.BurrowModel.Links.Right);
          if (target != null)
            burrow.GetLevels().AdjacentBunburrows.SetPart(Direction.Right, target.GetLevels());
        }
        if (!string.IsNullOrEmpty(burrow.BurrowModel.Links.Down))
        {
          var target = burrows.FirstOrDefault(bb => bb.LocalName == burrow.BurrowModel.Links.Down);
          if (target != null)
            burrow.GetLevels().AdjacentBunburrows.SetPart(Direction.Down, target.GetLevels());
        }
      }
    }

    public static BunburrowStyle ResolveStyle(string style)
    {
      switch (style)
      {
        case "Aquatic":
        case "Sunken":
          return AssetsManager.BunburrowsListOfStyles[Bunburrow.Aquatic];
        case "Hay":
          return AssetsManager.BunburrowsListOfStyles[Bunburrow.Hay];
        case "Forgotten":
        case "Purple":
          return AssetsManager.BunburrowsListOfStyles[Bunburrow.Purple];
        case "Spooky":
        case "Ghostly":
          return AssetsManager.BunburrowsListOfStyles[Bunburrow.Ghostly];
        case "Void":
          return AssetsManager.BunburrowsListOfStyles.VoidB;
        case "Temple":
          return AssetsManager.BunburrowsListOfStyles.Temple;
        case "Hell":
          return AssetsManager.BunburrowsListOfStyles.Hell;
        case "PinkHell":
          return AssetsManager.BunburrowsListOfStyles.PinkHell;
        case "AquaticHell":
        case "SunkenHell":
          return AssetsManager.BunburrowsListOfStyles.AquaticHell;
        case "HayHell":
          return AssetsManager.BunburrowsListOfStyles.HayHell;
        case "ForgottenHell":
        case "PurpleHell":
          return AssetsManager.BunburrowsListOfStyles.PurpleHell;
        case "SpookyHell":
        case "GhostlyHell":
          return AssetsManager.BunburrowsListOfStyles.GhostlyHell;
        case "HellTemple":
          return AssetsManager.BunburrowsListOfStyles.HellTemple;
        case "Pink":
        default:
          return AssetsManager.BunburrowsListOfStyles[Bunburrow.Pink];
      }
    }

    #region MenuSource implementation
    public string MenuTitle => "BNYS";
    public void DrawMenuOptions()
    {
      foreach (var world in CustomWorlds)
      {
        CreateButtonFor(world);
      }
    }

    public void CreateButtonFor(CustomWorld world)
    {
      if (WorldPendingDeletion == world)
      {
        CreateDeleteConfirmationFor(world);
        return;
      }

      GUILayout.BeginHorizontal();
      GUILayout.Label(world.Title, GUILayout.ExpandWidth(true));

      if (GUILayout.Button("Load", GUILayout.Width(100)))
      {
        LoadWorld(world);
      }

      if (GUILayout.Button("Delete", GUILayout.Width(100)))
      {
        WorldPendingDeletion = world;
      }

      GUILayout.EndHorizontal();
    }

    private void CreateDeleteConfirmationFor(CustomWorld world)
    {
      GUILayout.BeginHorizontal();
      GUILayout.Label($"Delete save?", GUILayout.ExpandWidth(true));

      if (GUILayout.Button("Yes", GUILayout.Width(100)))
      {
        DeleteWorldSave(world);
        WorldPendingDeletion = null;
      }

      if (GUILayout.Button("No", GUILayout.Width(100)))
      {
        WorldPendingDeletion = null;
      }

      GUILayout.EndHorizontal();
    }

    private void LoadWorld(CustomWorld world)
    {
      BunjectAPI.BeginLoadingScreen();

      try
      {
        // TODO replace this behavior - its outdated
        SurfaceBurrowsPatch.PatchSurfaceBurrows(AssetsManager.SurfaceRightLevel, null);

        // Ensure our bunburrows are registered.
        foreach (var bunburrow in BNYSModBurrows.Where(b => b.World == world))
        {
          BunjectAPI.RegisterBunburrow(bunburrow);

          if (bunburrow is BNYSModBunburrowBase bnysBurrow)
          {
            foreach (var elevatorDepth in bnysBurrow.BurrowModel.ElevatorDepths)
            {
              BunjectAPI.RegisterElevator(bunburrow.ID, elevatorDepth);
            }
          }
        }

        //Now do our surface generation if it hasn't been done.
        GenerateSurfaceLevels(world);

        BunjectAPI.LoadSave("BNYS", world.Title);

        CurrentCustomWorld = world;
      }
      catch (Exception ex)
      {
        Debug.LogException(ex);

        CurrentCustomWorld = null;
        BunjectAPI.CancelLoadingScreen();
      }
    }

    private void DeleteWorldSave(CustomWorld world)
    {
      if (SaveFileModUtility.PluginSaveExists("BNYS", world.Title))
      {
        File.Delete(SaveFileModUtility.GetPluginSaveFilePath("BNYS", world.Title));
      }
    }

    #endregion

    #region IComputerPageSource implementation
    public void GeneratePages(ICustomPageGenerator pageMaker)
    {
      if (CurrentCustomWorld != null)
      {
        var creditsPage = pageMaker.CreateComputerPage<CreditsPage>();
        creditsPage.World = CurrentCustomWorld;
      }
    }
    #endregion
  }
}
