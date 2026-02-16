/** @file
  Copyright (c) 2025, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/

using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Transactions;

using Dapper;
using Npgsql;

using MTGOSDK.Core.Security;
using MTGOSDK.Core.Logging;

using Database.Schemas;
using Database.Types;


namespace Database;

public class EventRepository
{
  private static readonly NpgsqlDataSource dataSource = GetDataSource();

  private static NpgsqlDataSource GetDataSource()
  {
    NpgsqlDataSourceBuilder dbDataSource = new(
      string.Join(";", new string[] {
        $"Host={DotEnv.Get("PGHOST")}",
        $"Port={DotEnv.Get("PGPORT") ?? "5432"}",
        $"Database={DotEnv.Get("PGDATABASE")}",
        $"Username={DotEnv.Get("PGUSER")}",
        $"Password={DotEnv.Get("PGPASSWORD")}",
        "Keepalive=15",
        "IncludeErrorDetail=true",
        "CommandTimeout=0",
      })
    );

    dbDataSource.MapEnum<FormatType>();
    dbDataSource.MapEnum<EventType>();
    dbDataSource.MapEnum<ResultType>();

    dbDataSource.MapComposite<CardQuantityPair>("cardquantitypair");
    dbDataSource.MapComposite<GameResult>("gameresult");

    var dataSource = dbDataSource.Build()
      ?? throw new Exception("Failed to build data source.");

    SqlMapper.AddTypeHandler(new DateOnlyTypeHandler());
    SqlMapper.AddTypeHandler(new CardQuantityPairArrayHandler());
    SqlMapper.AddTypeHandler(new GameResultArrayHandler());

    return dataSource;
  }

  /// <summary>
  /// Handler to convert between Postgres cardquantitypair[] and C# CardQuantityPair[].
  /// </summary>
  private class CardQuantityPairArrayHandler : SqlMapper.TypeHandler<CardQuantityPair[]>
  {
    public override void SetValue(System.Data.IDbDataParameter parameter, CardQuantityPair[] value)
    {
      parameter.Value = value;
    }

    public override CardQuantityPair[] Parse(object value)
    {
      return (CardQuantityPair[])value;
    }
  }

  /// <summary>
  /// Handler to convert between Postgres gameresult[] and C# GameResult[].
  /// </summary>
  private class GameResultArrayHandler : SqlMapper.TypeHandler<GameResult[]>
  {
    public override void SetValue(System.Data.IDbDataParameter parameter, GameResult[] value)
    {
      parameter.Value = value;
    }

    public override GameResult[] Parse(object value)
    {
      return (GameResult[])value;
    }
  }

  /// <summary>
  /// Handler to convert between Postgres DateOnly and C# DateTime.
  /// </summary>
  private class DateOnlyTypeHandler : SqlMapper.TypeHandler<DateTime>
  {
    public override void SetValue(System.Data.IDbDataParameter parameter, DateTime value)
    {
      parameter.Value = DateOnly.FromDateTime(value);
    }

    public override DateTime Parse(object value)
    {
      if (value is DateOnly dateOnly)
        return dateOnly.ToDateTime(TimeOnly.MinValue);
      
      if (value is DateTime dateTime)
        return dateTime;

      return Convert.ToDateTime(value);
    }
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
    Log.Information("Adding event {DisplayName}:\n{Entry}", entry.DisplayName, entry);
    using (var transactionScope = new TransactionScope(
      TransactionScopeOption.Required,
      TimeSpan.FromMinutes(10),
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
        // Try to insert with the official ID first.
        int rows = await connection.ExecuteAsync($@"
          INSERT INTO Players (id, name)
          VALUES (@Id, @Name)
          ON CONFLICT (id) DO NOTHING
        ", player);

        // If no rows were affected, the ID is already in the database.
        // We check if the name is also present; if not, we have a name change
        // conflict and must use a synthetic ID to record the new name.
        if (rows == 0)
        {
          bool nameExists = await connection.ExecuteScalarAsync<bool>($@"
            SELECT EXISTS(SELECT 1 FROM Players WHERE name = @Name)
          ", player);

          if (!nameExists)
          {
            await connection.ExecuteAsync($@"
              INSERT INTO Players (id, name)
              VALUES (@Id, @Name)
              ON CONFLICT (id) DO NOTHING
            ", new { Id = PlayerEntry.GenerateStableId(player.Name), Name = player.Name });
          }
        }
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
        await connection.ExecuteAsync($@"
          INSERT INTO Decks
          VALUES {deck}
        ");
      }
    }
  }

  /// <summary>
  /// Adds a collection of ArchetypeEntry instances to the database.
  /// </summary>
  public static async Task AddArchetypeEntries(IEnumerable<ArchetypeEntry> archetypes)
  {
    using (var connection = GetConnection())
    {
      foreach(var arch in archetypes)
      {
        await connection.ExecuteAsync($@"
          INSERT INTO Archetypes (id, deck_id, name, archetype, archetype_id)
          VALUES ({arch.Id}, {arch.DeckId}, @Name, @Archetype, {arch.ArchetypeId?.ToString() ?? "NULL"})
          ON CONFLICT (id) DO UPDATE SET
            deck_id = EXCLUDED.deck_id,
            name = EXCLUDED.name,
            archetype = EXCLUDED.archetype,
            archetype_id = EXCLUDED.archetype_id
        ", arch);
      }
    }
  }

  /// <summary>
  /// Retrieves a list of events that have decks without associated archetypes.
  /// </summary>
  /// <param name="days">The number of days to look back for unlabeled events.</param>
  public static async Task<IEnumerable<EventEntry>> GetUnlabeledEventsAsync(int days = 7)
  {
    using (var connection = GetConnection())
    {
      return await connection.QueryAsync<EventEntry>(@"
        SELECT DISTINCT e.*
        FROM Events e
        JOIN Decks d ON e.id = d.event_id
        LEFT JOIN Archetypes a ON d.id = a.deck_id
        WHERE a.deck_id IS NULL
        AND e.date >= CURRENT_DATE - (@days * INTERVAL '1 day')
      ", new { days });
    }
  }

  /// <summary>
  /// Retrieves the deck entries associated with the given event ID.
  /// </summary>
  public static async Task<IEnumerable<DeckEntry>> GetDecksByEventAsync(int eventId)
  {
    using (var connection = GetConnection())
    {
      return await connection.QueryAsync<DeckEntry>(@"
        SELECT * FROM Decks
        WHERE event_id = @eventId
      ", new { eventId });
    }
  }
}
