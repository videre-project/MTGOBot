/** @file
  Copyright (c) 2025, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/

using System;
using System.Collections.Generic;
using System.Linq;
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

  /// <summary>
  /// Waits until a database connection can be established, retrying on socket
  /// errors (e.g. pgpool not yet ready after system restart).
  /// </summary>
  public static async Task WaitForConnectionAsync()
  {
    var deadline = DateTime.UtcNow + TimeSpan.FromMinutes(10);
    while (DateTime.UtcNow < deadline)
    {
      try
      {
        using var conn = dataSource.OpenConnection();
        return;
      }
      catch (NpgsqlException ex) when (ex.InnerException is System.Net.Sockets.SocketException)
      {
        Log.Information("Database not reachable, retrying in 30s...");
        await Task.Delay(TimeSpan.FromSeconds(30));
      }
    }
    throw new NpgsqlException("Database unavailable after 10 minutes.");
  }

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
    await AddEventPayload(
      entry.@event,
      entry.players,
      entry.standings,
      entry.matches,
      entry.decklists,
      []
    );
  }

  public static async Task AddEventPayload(
    EventEntry @event,
    IEnumerable<PlayerEntry> players,
    IEnumerable<StandingEntry> standings,
    IEnumerable<MatchEntry> matches,
    IEnumerable<DeckEntry> decklists,
    IEnumerable<ArchetypeEntry> archetypes)
  {
    Log.Information(
      "Adding event {Name} #{Id}: Players={Players}, Standings={Standings}, Matches={Matches}, Decklists={Decklists}, Archetypes={Archetypes}",
      @event.Name,
      @event.Id,
      players.Count(),
      standings.Count(),
      matches.Count(),
      decklists.Count(),
      archetypes.Count()
    );

    using (var transactionScope = new TransactionScope(
      TransactionScopeOption.Required,
      TimeSpan.FromMinutes(10),
      TransactionScopeAsyncFlowOption.Enabled
    ))
    {
      await AddEventEntry(@event);
      await AddPlayerEntries(players);
      await AddStandingEntries(standings);
      await AddMatchEntries(matches);
      await AddDeckEntries(decklists);
      await AddArchetypeEntries(archetypes);

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
        // Try to insert with the official ID first. Either unique key can
        // already be present: id can be reused after a rename, while name can
        // appear with a different scraped id. The name is the FK target used by
        // event payload tables, so an existing name already satisfies the import
        // without adding a duplicate player row.
        int rows = await connection.ExecuteAsync($@"
          INSERT INTO Players (id, name)
          VALUES (@Id, @Name)
          ON CONFLICT DO NOTHING
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
              ON CONFLICT DO NOTHING
            ", new { Id = PlayerEntry.GenerateStableId(player.Name), Name = player.Name });
          }
        }

        bool playerNameExists = await connection.ExecuteScalarAsync<bool>($@"
          SELECT EXISTS(SELECT 1 FROM Players WHERE name = @Name)
        ", player);

        if (!playerNameExists)
        {
          throw new Exception($"Failed to insert or find player name '{player.Name}'.");
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
        int updatedRows = await connection.ExecuteAsync(@"
          UPDATE Archetypes
          SET
            name = @Name,
            archetype = @Archetype,
            archetype_id = @ArchetypeId,
            provider = @Provider
          WHERE deck_id = @DeckId
        ", arch);

        if (updatedRows > 0)
          continue;

        int insertId = await GetArchetypeInsertId(connection, arch);

        await connection.ExecuteAsync(@"
          INSERT INTO Archetypes (id, deck_id, name, archetype, archetype_id, provider)
          VALUES (@Id, @DeckId, @Name, @Archetype, @ArchetypeId, @Provider)
          ON CONFLICT (deck_id) DO UPDATE SET
            name = EXCLUDED.name,
            archetype = EXCLUDED.archetype,
            archetype_id = EXCLUDED.archetype_id,
            provider = EXCLUDED.provider
        ", new
        {
          Id = insertId,
          arch.DeckId,
          arch.Name,
          arch.Archetype,
          arch.ArchetypeId,
          arch.Provider
        });
      }
    }
  }

  private static async Task<int> GetArchetypeInsertId(
    NpgsqlConnection connection,
    ArchetypeEntry arch)
  {
    bool idTakenByOtherDeck = await connection.ExecuteScalarAsync<bool>(@"
      SELECT EXISTS (
        SELECT 1
        FROM Archetypes
        WHERE id = @Id
          AND deck_id <> @DeckId
      )
    ", arch);

    if (!idTakenByOtherDeck)
      return arch.Id;

    for (int attempt = 0; attempt < 100; attempt++)
    {
      int candidate = StableNegativeId($"archetype:{arch.DeckId}:{attempt}");
      bool candidateTaken = await connection.ExecuteScalarAsync<bool>(@"
        SELECT EXISTS (
          SELECT 1
          FROM Archetypes
          WHERE id = @Id
        )
      ", new { Id = candidate });

      if (!candidateTaken)
        return candidate;
    }

    throw new Exception($"Could not allocate archetype ID for deck {arch.DeckId}.");
  }

  private static int StableNegativeId(string value)
  {
    uint hash = 2166136261;
    foreach (char c in value)
    {
      hash ^= c;
      hash *= 16777619;
    }

    return -1 - (int)(hash % 2147483646);
  }

  /// <summary>
  /// Retrieves a list of events that still need archetype backfill.
  /// </summary>
  /// <param name="days">The number of days to look back for unresolved events.</param>
  public static async Task<IEnumerable<EventEntry>> GetUnlabeledEventsAsync(int days = 7)
  {
    using (var connection = GetConnection())
    {
      return await connection.QueryAsync<EventEntry>(@"
        SELECT e.*
        FROM Events e
        JOIN Decks d ON e.id = d.event_id
        LEFT JOIN Archetypes a ON d.id = a.deck_id
        WHERE e.date >= CURRENT_DATE - (@days * INTERVAL '1 day')
        GROUP BY e.id, e.name, e.date, e.format, e.kind, e.rounds, e.players
        HAVING
          BOOL_OR(a.deck_id IS NULL)
          OR BOOL_AND(a.archetype_id IS NULL)
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
