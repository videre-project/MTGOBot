/** @file
  Copyright (c) 2023, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/

using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Transactions;

using Dapper;
using Npgsql;

using MTGOSDK.API.Play.Tournaments;
using MTGOSDK.Core.Security;

using Database.Schemas;
using Database.Types;


namespace Database;

public class EventRepository
{
  private static readonly NpgsqlDataSource dataSource = GetDataSource();

  private static NpgsqlDataSource GetDataSource()
  {
    NpgsqlDataSourceBuilder dbDataSource = new(DotEnv.Get("CONNECTION_STRING"));

    dbDataSource.MapEnum<FormatType>();
    dbDataSource.MapEnum<EventType>();
    dbDataSource.MapEnum<ResultType>();

    dbDataSource.MapComposite<CardQuantityPair>();
    dbDataSource.MapComposite<GameResult>();

    var dataSource = dbDataSource.Build()
      ?? throw new Exception("Failed to build data source.");

    return dataSource;
  }

  /// <summary>
  /// Retrieves a pooled connection to the database.
  /// </summary>
  /// <remarks>
  /// This connection should be disposed of after use to return it to the pool.
  /// </remarks>
  private static NpgsqlConnection GetConnection() => dataSource.OpenConnection();

  //
  // Database CRUD Operations
  //

  /// <summary>
  /// Checks whether an event with the given ID exists in the database.
  /// </summary>
  /// <param name="eventId">The ID of the event to check for.</param>
  /// <returns>Whether the event already exists in the database.</returns>
  public static async Task<bool> EventExists(int eventId)
  {
    using (var connection = GetConnection())
    {
      return await connection.ExecuteScalarAsync<bool>($@"
        SELECT EXISTS (
          SELECT 1 FROM Events
          WHERE id = {eventId}
        )
      ");
    }
  }

  /// <summary>
  /// Adds entries from an EventComposite view to the database.
  /// </summary>
  /// <param name="entry">The EventComposite instance to pull records from.</param>
  /// <returns>The status of the transaction.</returns>
  /// <remarks>
  /// This method is wrapped in a transaction scope to ensure that all records
  /// are added atomically. If any of the records fail to be added, the entire
  /// transaction will be rolled back.
  /// <para/>
  /// Usage of this method is preferred over usage of individual Add* methods in
  /// this class when adding a new event and its associated records to the
  /// database.
  /// </remarks>
  public static async Task AddEvent(EventComposite entry)
  {
    using (var transactionScope = new TransactionScope(
      TransactionScopeAsyncFlowOption.Enabled
    ))
    {
      await AddEventEntry(entry.@event);
      await AddPlayerEntries(entry.players);
      await AddStandingEntries(entry.standings);
      await AddMatchEntries(entry.matches);
      await AddDeckEntries(entry.decklists);

      transactionScope.Complete();
    }
  }

  /// <summary>
  /// Adds an EventEntry instance to the database.
  /// </summary>
  public static async Task AddEventEntry(EventEntry @event)
  {
    using (var connection = GetConnection())
    {
      await connection.ExecuteAsync($@"
        INSERT INTO Events
        VALUES {@event}
      ");
    }
  }

  /// <summary>
  /// Adds a collection of PlayerEntry instances to the database.
  /// </summary>
  public static async Task AddPlayerEntries(IEnumerable<PlayerEntry> players)
  {
    using (var connection = GetConnection())
    {
      foreach(var player in players)
      {
        await connection.ExecuteAsync($@"
          INSERT INTO Players
          VALUES {player}
          ON CONFLICT (id) DO UPDATE
          SET
            name = EXCLUDED.name
        ");
      }
    }
  }

  /// <summary>
  /// Adds a collection of StandingEntry instances to the database.
  /// </summary>
  public static async Task AddStandingEntries(IEnumerable<StandingEntry> standings)
  {
    using (var connection = GetConnection())
    {
      foreach(var standing in standings)
      {
        await connection.ExecuteAsync($@"
          INSERT INTO Standings
          VALUES {standing}
        ");
      }
    }
  }

  /// <summary>
  /// Adds a collection of MatchEntry instances to the database.
  /// </summary>
  public static async Task AddMatchEntries(IEnumerable<MatchEntry> matches)
  {
    using (var connection = GetConnection())
    {
      foreach(var match in matches)
      {
        await connection.ExecuteAsync($@"
          INSERT INTO Matches
          VALUES {match}
        ");
      }
    }
  }

  /// <summary>
  /// Adds a collection of DeckEntry instances to the database.
  /// </summary>
  public static async Task AddDeckEntries(IEnumerable<DeckEntry> decks)
  {
    using (var connection = GetConnection())
    {
      foreach(var deck in decks)
      {
        try
        {
        await connection.ExecuteAsync($@"
          INSERT INTO Decks
          VALUES {deck}
        ");
        }
        catch
        {
          Console.WriteLine($@"
            INSERT INTO Decks
            VALUES {deck}
          ");
          throw;
        }
      }
    }
  }
}
