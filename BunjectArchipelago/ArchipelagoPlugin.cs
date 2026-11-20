using BepInEx;
using BepInEx.Logging;
using Bunburrows;
using Bunject;
using Bunject.Archipelago.Archipelago;
using Bunject.Archipelago.UI;
using Bunject.Archipelago.Utils;
using Bunject.Computer;
using Bunject.Menu;
using Bunject.Monitoring;
using Characters.Bunny.Data;
using HarmonyLib;
using Items;
using Levels;
using Misc;
using System;
using System.Collections;
using System.IO;
using System.Linq;
using System.Reflection;
using TMPro;
using UnityEngine;

namespace Bunject.Archipelago
{
  [BepInPlugin(PluginGUID, PluginName, PluginVersion)]
  public class ArchipelagoPlugin : BaseUnityPlugin, IMonitor, IComputerPageSource
  {
    static ArchipelagoPlugin()
    {
      Harmony.CreateAndPatchAll(typeof(ArchipelagoPlugin).Assembly);
    }

    public static ArchipelagoPlugin Instance { get; private set; }

    public const string PluginGUID = "sergedev.bunject.archipelago";
    public const string PluginName = "BunjectArchipelago";
    public const string PluginVersion = "1.3.1";

    public static ManualLogSource BepinLogger;

    public static string RootDirectory = Path.Combine(Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location));
    
    
    internal ArchipelagoClient ArchipelagoClient = null;
    private ArchipelagoMenu Menu = null;


    public LevelsList LoadEmergencyLevelsList(LevelsList original)
    {
      return original;
    }

    public void OnAssetsLoaded() { }

    public void OnProgressionLoaded(GeneralProgression progression)
    {
      if (ArchipelagoClient != null)
      {
        if (ArchipelagoClient.Options.unlock_computer)
          progression.HandleOphelinePortableComputerUnlock();

        if (ArchipelagoClient.Options.unlock_map)
          progression.HandleMapUnlock();
      }

      ArchipelagoConsole.ShowConsole(); 
    }

    public LevelObject OnLevelLoad(LevelObject level, LevelIdentity identity)
    {
      if (ArchipelagoClient != null)
      {
        var levelString = identity.Bunburrow.ToIndicator() + "-" + identity.Depth;
        if (!ArchipelagoClient.HasToolsForLevel(levelString))
        {
          return LevelLocker.Lock(level);
        }
      }
      return level;
    }

    public string OnLevelTitle(string title, LevelIdentity identity, bool useWhite)
    {
      if (ArchipelagoClient != null)
      {
        var levelString = identity.Bunburrow.ToIndicator() + "-" + identity.Depth;
        if (!ArchipelagoClient.HasToolsForLevel(levelString))
        {
          return LevelLocker.AppendLock(title);
        }
      }
      return title;
    }

    public void OnBunnyCapture(BunnyIdentity bunny, bool wasHomeCapture)
    {
      if (ArchipelagoClient != null)
      {
        if (wasHomeCapture || (ArchipelagoClient.Options.home_captures == false))
        {
          ArchipelagoClient.NotifyBunnyCaptured(bunny.GetIdentityString());
        }
      }
    }

    public void OnShowCredits()
    {
      if (ArchipelagoClient != null)
      {
        ArchipelagoClient.OnShowCredits();
      }
    }

    public void OnPowerTile(global::Tiling.Behaviour.PowerUnlockTile tile, LevelIdentity identity, Action dismiss) { }

    public bool TryResolvePowerTileSprite(global::Tiling.Behaviour.PowerUnlockTile tile, LevelIdentity identity, out UnityEngine.Tilemaps.TileBase sprite)
    {
      sprite = null;
      return false;
    }

    protected void Awake()
    {
      DontDestroyOnLoad(gameObject);

      Instance = this;

      // Plugin startup logic
      BepinLogger = Logger;

      ArchipelagoConsole.Awake();

      Menu = new ArchipelagoMenu(() => new GameObject().AddComponent<DummyBehavior>().StartCoroutine(TryConnect()));

      BunjectAPI.RegisterPlugin(this);
      BunjectAPI.RegisterPlugin(Menu);
    }

    public void OnMainMenu()
    {
      // We're back from the game - disconnect and reset any major changes
      if (ArchipelagoClient != null)
      {
        BunjectAPI.ClearRegisters();

        ArchipelagoClient.Dispose();
        ArchipelagoClient = null;

        ArchipelagoConsole.ShowConsole(false);
      }
    }

    private IEnumerator TryConnect()
    {
      BunjectAPI.BeginLoadingScreen();

      yield return null;

      try
      {
        // Create Client and Start Connection
        ArchipelagoClient = ArchipelagoClient.Connect(Menu.Uri, Menu.SlotName, Menu.Password);

        if (ArchipelagoClient != null)
        {
          BunjectAPI.RegisterBunburrow(ElevatorTrapBunburrow.Instance);

          // Get world id from client
          string saveName = GetSaveName(ArchipelagoClient);

          BunjectAPI.LoadSave("Archipelago", saveName);
        }
        else
        {
          BunjectAPI.CancelLoadingScreen();
        }
      }
      catch (Exception e)
      {
        BepinLogger.LogError(e);
        ArchipelagoConsole.LogMessage(e.Message);

        BunjectAPI.CancelLoadingScreen();
      }
      finally
      {
        ArchipelagoConsole.ShowConsole(true);
      }

      yield return null;
    }

    private string GetSaveName(ArchipelagoClient client)
    {
      return $"{client.Seed}_{client.Slot}";
    }

    public void LockComputerVisuals(ItemListOf<TextMeshProUGUI> levelItemTexts, ItemListOf<GameObject> levelItemObjects)
    {
      var items = Enum.GetValues(typeof(Item)).OfType<Item>().ToList();
      foreach (var item in items.Skip(1))
      {
        var obj = levelItemObjects[item];
        if (obj != null)
        {
          levelItemTexts[item].text = "";
          obj.SetActive(false);
        }
      }

      var firstItem = items.First();
      levelItemTexts[firstItem].text = "Locked";
      levelItemObjects[firstItem].SetActive(true);
    }

    public void GeneratePages(ICustomPageGenerator pageMaker)
    {
      if (ArchipelagoClient != null)
      {
        pageMaker.CreateComputerPage<ProgressComputerPage>();
      }
    }
  }
}
