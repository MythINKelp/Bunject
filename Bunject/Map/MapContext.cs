using Bunburrows;
using Bunject.Internal;
using Bunject.Levels;
using Computer.Opheline.Map;
using HarmonyLib;
using Levels;
using LibTessDotNet;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Unity.Collections;
using UnityEngine;
using UnityEngine.Analytics;

namespace Bunject.Map
{
  /// <summary>
  ///  Map Context is created every time the player opens the map
  ///  It holds all the details that map rendering needs to render custom bunburrow maps
  ///  Please forgive the Enumerable hacks, they are there so that the map renderer can make the correct location/level/map correlations.
  /// </summary>
  internal class MapContext : IDisposable, IEnumerable<Bunburrows.Bunburrow>
  {
    public static MapContext Instance { get; private set; }

    public Bunburrows.Bunburrow Bunburrow { get; private set; }

    public IReadOnlyList<MapCoordinate> Coordinates { get; private set; }

    public Vector2Int CurrentCoordinates => coordinateEnumerator.Current.MapIndex;

    public Vector2Int InitialCenter { get; private set; }

    public MapContext(Bunburrows.Bunburrow bunburrow)
    {
      Bunburrow = bunburrow;

      var (coordinates, center) = GenerateMap(bunburrow);
      this.Coordinates = coordinates;

      Instance = this;
    }

    private static (List<MapCoordinate>, Vector2Int) GenerateMap(Bunburrows.Bunburrow bunburrow)
    {
      // Lots of gross / semiunsafe indexing here, sorry.
      // Preliminary requirement: creating a reverse lookup list from levelsLists to Bunburrows.
      var lookup = new Dictionary<LevelsList, Bunburrows.Bunburrow>();
      foreach (var metadata in BunburrowManager.Bunburrows)
      {
        lookup.Add(metadata.ModBunburrow.GetLevels(), metadata.ID.ToBunburrow());
      }

      // Start by generating lists of all bunburrows going upward, downward, rightward, leftward.  Stop on a duplicate.
      var upList = GetAllBunburrowsInDirection(bunburrow, Misc.Direction.Up, lookup).Skip(1).ToList();
      var downList = GetAllBunburrowsInDirection(bunburrow, Misc.Direction.Down, lookup).Reverse().ToList();
      var leftList = GetAllBunburrowsInDirection(bunburrow, Misc.Direction.Left, lookup).Reverse().ToList();
      var rightList = GetAllBunburrowsInDirection(bunburrow, Misc.Direction.Right, lookup).Skip(1).ToList();

      // Create the initial rows/columns
      var innerRow = leftList.Concat(rightList).ToArray();
      var innerColumn = downList.Concat(upList).ToArray();

      // Determine map grid metrics
      var width = innerRow.Length;
      var height = innerColumn.Length;

      var centerX = leftList.Count - 1;
      var centerY = downList.Count - 1;

      // Create the grid
      var coordinates = new Bunburrows.Bunburrow[width, height];

      for (var x = 0; x < width; x++)
      {
        for (var y = 0; y < height; y++)
        {
          coordinates[x, y] = (Bunburrows.Bunburrow)(-1);
        }
      }

      // Initialize inner row/column
      for (var x = 0; x < width; x++)
      {
        coordinates[x, centerY] = innerRow[x];
      }

      for (var y = 0; y < height; y++)
      {
        coordinates[centerX, y] = innerColumn[y];
      }

      // Build top-left quadrant
      InitializeQuadrant(coordinates, centerX, centerY, -1, +1, -1, height, Misc.Direction.Left, Misc.Direction.Up, lookup);
      // Top-right quadrant
      InitializeQuadrant(coordinates, centerX, centerY, +1, +1, width, height, Misc.Direction.Right, Misc.Direction.Up, lookup);
      // Bottom-right quadrant
      InitializeQuadrant(coordinates, centerX, centerY, +1, -1, width, -1, Misc.Direction.Right, Misc.Direction.Down, lookup);
      // Bottom-left quadrant
      InitializeQuadrant(coordinates, centerX, centerY, -1, -1, -1, -1, Misc.Direction.Left, Misc.Direction.Down, lookup);

      // Finally, convert coordinates to a list that can be used by the mapping infra
      var results = new List<MapCoordinate>();
      for (var x = 0; x < width; x++)
      {
        for (var y = 0; y < height; y++)
        {
          if (coordinates[x, y] >= 0)
          {
            results.Add(new MapCoordinate()
            {
              // (1, 1) HAS to be center..
              MapIndex = new UnityEngine.Vector2Int(x - centerX + 1, y - centerY + 1),
              Bunburrow = coordinates[x, y]
            });
          }
        }
      }

      return (results, CreatePixelOffset(centerX, centerY));
    }

    // Ugly function awful design, but its low-effort.
    private static void InitializeQuadrant(Bunburrow[,] coordinates, int centerX, int centerY, int xStep, int yStep, int xBound, int yBound, Misc.Direction xDirection, Misc.Direction yDirection, IReadOnlyDictionary<LevelsList, Bunburrows.Bunburrow> burrowLookup)
    {
      bool first = true;

      for (var x = centerX + xStep; x != xBound; x += xStep)
      {
        for (var y = centerY + yStep; y != yBound; y += yStep)
        { 
          var previousXburrow = coordinates[x - xStep, y];
          var previousYburrow = coordinates[x, y - yStep];

          var seenFromX = previousXburrow >= 0 ? GetNextBunburrow(previousXburrow, xDirection, burrowLookup) : null;
          var seenFromY = previousYburrow >= 0 ? GetNextBunburrow(previousYburrow, yDirection, burrowLookup) : null;

          if (seenFromX.HasValue)
          {
            if (seenFromY.HasValue)
            {
              // If both sides see a level here, only populate it if they match.
              if (seenFromY == seenFromX)
              {
                coordinates[x, y] = seenFromX.Value;
              }
            }
            else
            {
              coordinates[x, y] = seenFromX.Value;
            }
          }
          else if (seenFromY.HasValue)
          {
            coordinates[x, y] = seenFromY.Value;
          }
        }
      }
    }

    private static Bunburrows.Bunburrow? GetNextBunburrow(Bunburrows.Bunburrow bunburrow, Misc.Direction step, IReadOnlyDictionary<LevelsList, Bunburrows.Bunburrow> burrowLookup)
    {
      var nextLevelsList = bunburrow.GetModBunburrow().GetLevels().AdjacentBunburrows[step];

      if ((nextLevelsList != null) && (burrowLookup.ContainsKey(nextLevelsList)))
        return burrowLookup[nextLevelsList];

      return null;
    }

    private static IEnumerable<Bunburrows.Bunburrow> GetAllBunburrowsInDirection(Bunburrows.Bunburrow bunburrow, Misc.Direction step, IReadOnlyDictionary<LevelsList, Bunburrows.Bunburrow> burrowLookup)
    {
      var results = new List<Bunburrows.Bunburrow>();
      var next = bunburrow;
      while (true)
      {
        results.Add(next);
        var nextLevelsList = bunburrow.GetModBunburrow().GetLevels().AdjacentBunburrows[step];

        var maybeNext = GetNextBunburrow(next, step, burrowLookup);
        if (!maybeNext.HasValue)
          return results;

        next = maybeNext.Value;

        if (results.Contains(next))
        {
          // include the repeat anyways just to cap it off
          results.Add(next);
          return results;
        }
      }
    }

    private static Vector2Int CreatePixelOffset(int centerX, int centerY)
    {
      var x = ((centerX + 0.5) * (double)ComputerMapBuilder.LevelPixelSize.x) + 6;
      var y = ((centerY + 0.5) * (double)ComputerMapBuilder.LevelPixelSize.y) + 5;

      return new Vector2Int((int)x, (int)y);
    }

    // Abusing enumeration interfaces for sure, but at the end of the day it will work, and its not the biggest deal.

    #region IEnumerable Boilerplate
    public IEnumerator<Bunburrows.Bunburrow> GetEnumerator()
    {
      this.coordinateEnumerator = Coordinates.GetEnumerator();
      return new MapContextEnumerator(this);
    }

    IEnumerator IEnumerable.GetEnumerator()
    {
      return GetEnumerator();
    }
    #endregion

    #region IEnumerator Boilerplate
    private IEnumerator<MapCoordinate> coordinateEnumerator;

    public Bunburrows.Bunburrow Current => coordinateEnumerator.Current.Bunburrow;

    public bool MoveNext()
    {
      return coordinateEnumerator.MoveNext();
    }

    public void Reset()
    {
      coordinateEnumerator.Reset();
    }
    #endregion

    #region IDisposable Boilerplate
    private bool disposedValue;

    protected virtual void Dispose(bool disposing)
    {
      if (!disposedValue)
      {
        if (disposing)
        {
          if (Instance == this)
          {
            Instance = null;
          }
        }
        disposedValue = true;
      }
    }

    public void Dispose()
    {
      Dispose(disposing: true);
      GC.SuppressFinalize(this);
    }

    #endregion

    // Need a separate object to prevent dispose on foreach
    private class MapContextEnumerator : IEnumerator<Bunburrows.Bunburrow>
    {
      private MapContext owner;
      public MapContextEnumerator(MapContext owner)
      {
        this.owner = owner;
      }

      public Bunburrow Current => owner.Current;

      object IEnumerator.Current => Current;

      public void Dispose() { }

      public bool MoveNext()
      {
        return this.owner.MoveNext();
      }

      public void Reset()
      {
        this.owner.Reset();
      }
    }
  }
}
