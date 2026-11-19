using Archipelago.MultiClient.Net;
using Archipelago.MultiClient.Net.BounceFeatures.DeathLink;
using Archipelago.MultiClient.Net.Enums;
using Archipelago.MultiClient.Net.Helpers;
using Archipelago.MultiClient.Net.MessageLog.Messages;
using Archipelago.MultiClient.Net.Packets;
using Bunburrows;
using Bunject.Archipelago.Client;
using Bunject.Archipelago.UI;
using Bunject.Computer;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using UnityEngine;

namespace Bunject.Archipelago.Archipelago
{
  public class ArchipelagoClient : IDisposable
  {
    public const string GameName = "Paquerette Down The Bunburrows";
    public const bool Authenticated = false;

    public static readonly Version ArchipelagoProtocolVersion = new Version(0, 6, 0);

    public string Seed { get; private set; }
    public int Slot { get; private set; }

    public ArchipelagoOptions Options { get; private set; }

    private ArchipelagoSession session;
    private bool disposedValue;

    public HashSet<string> AllMissingTools = MissingToolsGenerator.Generate(true);
    public HashSet<string> MissingTools = null;
    public HashSet<string> ToolsFound = new HashSet<string>();
    public Dictionary<string, int> AllItemsFound = new Dictionary<string, int>();
    private DeathLinkHandler deathLink;
    private TrapHandler trapHandler;

    public bool GoalAchieved { get; private set; } = false;


    public static ArchipelagoClient Connect(string hostName, string userName, string password)
    {
      var session = ArchipelagoSessionFactory.CreateSession(hostName);

      var client = new ArchipelagoClient(session);

      var loginResult = session.TryConnectAndLogin(GameName, userName, ItemsHandlingFlags.AllItems, version: ArchipelagoProtocolVersion, password: password, requestSlotData: true);
      if (loginResult.Successful && loginResult is LoginSuccessful successful)
      {
        client.Options = ArchipelagoOptions.ParseSlotData(successful.SlotData);

        if (!client.Options.IsWorldVersionCompatible)
        {
          var seedVersion = client.Options.world_version?.ToString() ?? "pre-0.2.0 (no world_version in slot_data)";
          var supported = ArchipelagoOptions.SupportedWorldVersion;
          var msg = $"APWorld version mismatch: seed is {seedVersion}, this BunjectArchipelago build "
                  + $"targets {supported.Major}.{supported.Minor}.x. Continuing anyway — update the mod "
                  + "if items, locations, or the goal misbehave.";
          ArchipelagoPlugin.BepinLogger.LogWarning(msg);
          ArchipelagoConsole.LogMessage(msg);
        }
        client.MissingTools = MissingToolsGenerator.Generate(client.Options.victory_condition != VictoryCondition.Credits);

        client.Seed = session.RoomState.Seed;
        client.Slot = successful.Slot;

        client.trapHandler = new TrapHandler(client, session.DataStorage);

        if (client.Options.death_link)
          client.deathLink = new DeathLinkHandler(session.CreateDeathLinkService(), userName, client.Options, client.trapHandler);

        client.CheckForGoldenFluff(false);

        ArchipelagoConsole.LogMessage("Enabling Traps...");
        client.trapHandler.Enable();

        return client;
      }
      else
      {
        ArchipelagoConsole.LogMessage($"Failed to Connect: {loginResult}");
      }

      return null;
    }

    private ArchipelagoClient(ArchipelagoSession session)
    {
      this.session = session;
      session.Items.ItemReceived += OnItemReceieved;

      session.MessageLog.OnMessageReceived += OnMessageReceived;
      session.Socket.ErrorReceived += OnSessionErrorReceived;
      session.Socket.SocketClosed += OnSessionSocketClosed;

      AllItemsFound[GoldenFluff] = 0;
    }

    public bool HasToolsForLevel(string level)
    {
      if (MissingTools.Contains(level))
      {
        return ToolsFound.Contains(level);
      }
      return true;
    }

    public void NotifyBunnyCaptured(string bunny)
    {
      var locationId = session.Locations.GetLocationIdFromName(GameName, bunny);

      if (locationId != -1)
      {
        session.Locations.CompleteLocationChecks(locationId);
      }

      if (bunny == "C-27-1" && Options.victory_condition == VictoryCondition.GoldenBunny && !GoalAchieved)
      {
        ArchipelagoConsole.LogMessage("Game Complete!");
        SetGoalAchieved();
      }

      if (session.Locations.AllMissingLocations.Count == 0 && Options.victory_condition == VictoryCondition.FullClear && !GoalAchieved)
      {
        ArchipelagoConsole.LogMessage("Game Complete!");
        SetGoalAchieved();
      }
    }

    public void OnShowCredits()
    {
      if (Options.victory_condition == VictoryCondition.Credits && !GoalAchieved)
      {
        ArchipelagoConsole.LogMessage("Game Complete!");
        SetGoalAchieved();
      }
    }

    public void SendMessage(string message)
    {
      session.Socket.SendPacketAsync(new SayPacket { Text = message });
    }

    private void OnItemReceieved(ReceivedItemsHelper items)
    {
      var itemReceived = items?.PeekItem();
      if (itemReceived != null)
      {
        if (itemReceived.ItemGame == GameName)
        {
          if (AllMissingTools.Contains(itemReceived.ItemName))
          {
            ToolsFound.Add(itemReceived.ItemName);
          }

          if (!GoalAchieved)
          {
            HandlePossibleTrap(itemReceived.ItemName);
          }

          if (AllItemsFound.ContainsKey(itemReceived.ItemName))
          {
            AllItemsFound[itemReceived.ItemName] += 1;
          }
          else
          {
            AllItemsFound.Add(itemReceived.ItemName, 1);
          }

          CheckForGoldenFluff(itemReceived.ItemName == GoldenFluff);
        }
      }
      items.DequeueItem();
    }

    const string GoldenFluff = "Golden Fluff";
    private void CheckForGoldenFluff(bool printProgress)
    {
      if (Options?.victory_condition == VictoryCondition.GoldenFluff && !GoalAchieved)
      {
        if (AllItemsFound[GoldenFluff] >= Options.golden_fluff)
        {
          SetGoalAchieved();
          ArchipelagoConsole.LogMessage($"You found the last Golden Fluff!  Game Complete!");
        }
        else if (printProgress)
        {
          ArchipelagoConsole.LogMessage($"You found a Golden Fluff!  Only {Options.golden_fluff - AllItemsFound[GoldenFluff]} to go!");
        }
      }
    }

    public int GoldenFluffCount
    {
      get
      {
        return AllItemsFound.ContainsKey(GoldenFluff) ? AllItemsFound[GoldenFluff] : 0;
      }
    }

    private void SetGoalAchieved()
    {
      GoalAchieved = true;
      session.SetGoalAchieved();
    }

    private void HandlePossibleTrap(string itemName)
    {
      switch (itemName)
      {
        case "Surface Teleport Trap":
          trapHandler?.TrapRecieved(Trap.SurfaceTeleport);
          break;
        case "Elevator Trap":
          trapHandler?.TrapRecieved(Trap.Elevator);
          break;
      }
    }

    private void OnMessageReceived(LogMessage message)
    {
      ArchipelagoConsole.LogMessage(message.ToString());
    }

    private void OnSessionErrorReceived(Exception e, string message)
    {
      ArchipelagoPlugin.BepinLogger.LogError(e);
      ArchipelagoConsole.LogMessage(message);
    }

    private void OnSessionSocketClosed(string reason)
    {
      ArchipelagoPlugin.BepinLogger.LogError($"Connection to Archipelago lost: {reason}");
      session = null;
    }


    #region IDisposable Implementation
    protected virtual void Dispose(bool disposing)
    {
      if (!disposedValue)
      {
        if (disposing) { }

        try
        {
          trapHandler?.Disable();

          if (session != null)
          {
            session.Items.ItemReceived -= OnItemReceieved;
            session.MessageLog.OnMessageReceived -= OnMessageReceived;
            session.Socket.ErrorReceived -= OnSessionErrorReceived;
            session.Socket.SocketClosed -= OnSessionSocketClosed;

            if (session.Socket.Connected)
            {
              Task.Run(() => session.Socket.DisconnectAsync().Wait());
            }
          }
          session = null;
        }
        catch { /* Well, we tried */ }

        disposedValue = true;
      }
    }

    // // TODO: override finalizer only if 'Dispose(bool disposing)' has code to free unmanaged resources
    // ~ArchipelagoClient()
    // {
    //     // Do not change this code. Put cleanup code in 'Dispose(bool disposing)' method
    //     Dispose(disposing: false);
    // }

    public void Dispose()
    {
      // Do not change this code. Put cleanup code in 'Dispose(bool disposing)' method
      Dispose(disposing: true);
      GC.SuppressFinalize(this);
    }
    #endregion
  }
}
