/** @file
  Copyright (c) 2023, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

using MTGOSDK.API.Play;
using MTGOSDK.API.Play.Tournaments;
using MTGOSDK.Core.Reflection;
using static MTGOSDK.API.Events;

using WotC.MtGO.Client.Model.Play.Tournaments;

using Database;
using Database.Schemas;


namespace Bot;

public class EventQueue : DLRWrapper<ConcurrentQueue<Tournament>>
{
  /// <summary>
  /// Represents a queued event to be added to the database.
  /// </summary>
  public struct QueueItem(dynamic @event, int retries = 3)
  {
    public int Id => @event.Id;
    public string Name => @event.ToString();
    public int Retries = retries;
    public EventComposite? entry = null;
  }

  /// <summary>
  /// The current queue of events to be added to the database.
  /// </summary>
  public ConcurrentQueue<QueueItem> Queue = new();

  /// <summary>
  /// The number of events in the queue.
  /// </summary>
  public int Count => Queue.Count;

  public EventQueue() => InitializeQueue().Wait();

  /// <summary>
  /// Adds an event to the queue if or once the tournament has finished.
  /// </summary>
  /// <param name="event">The event to add to the queue.</param>
  /// <returns>True if the event was added to the queue, false otherwise.</returns>
  public async Task<bool> AddEventToQueue(dynamic @event)
  {
    // Check whether the event is a tournament type defined in the schema.
    if (!Try<bool>(() => new EventEntry(@event as Tournament).Id != -1))
    {
      return false;
    }

    // Check that the event is not already added to the queue or database.
    if (@event.StartTime.AddMinutes(5) >= BotClient.ResetTime ||
        Queue.Contains(new QueueItem(@event)) ||
        await EventRepository.EventExists(@event.Id))
    {
      return false;
    }

    Console.WriteLine($"Found event '{@event}'...");
    Console.WriteLine($"--> Start Time: {@event.StartTime}");

    if (@event.IsCompleted)
    {
      Console.WriteLine($"Event '{@event}' is already completed, adding to queue...");
      Queue.Enqueue(new QueueItem(@event));
    }
    else
    {
      @event.TournamentStateChanged += new EventCallback<
        TournamentStateChangedEventArgs
      >((e) =>
      {
        if (e.NewValue == TournamentState.Finished)
        {
          Console.WriteLine($"Event '{@event}' is now finished, adding to queue...");
          Queue.Enqueue(new QueueItem(@event));
        }
      });
    }

    return true;
  }

  /// <summary>
  /// Adds a list of events to the queue if or once the tournament has finished.
  /// </summary>
  /// <param name="events">The events to add to the queue.</param>
  /// <returns>True if all of the events were added to the queue, false otherwise.</returns>
  public async Task<bool> AddEventsToQueue(IEnumerable<dynamic> events)
  {
    bool added = false;
    foreach (var @event in events)
    {
      added &= await AddEventToQueue(@event);
    }

    return added;
  }

  /// <summary>
  /// Initializes the event queue and adds callbacks to add events to the queue.
  /// </summary>
  public async Task InitializeQueue()
  {
    // Add a callback to add the event to the queue when new events are created.
    EventManager.PlayerEventsCreated += new EventCallback<
      PlayerEventsCreatedEventArgs
    >(async (e) => await AddEventsToQueue(e.Events));

    // Enqueue or schedule all events that are already in the system.
    await AddEventsToQueue(EventManager.Events);
  }

  /// <summary>
  /// Blocks the current thread and processes the event queue when updated.
  /// </summary>
  public async Task<bool> ProcessQueue()
  {
    bool hasUpdated = false;
    while (Queue.TryDequeue(out QueueItem item))
    {
      Console.WriteLine($"Processing event '{item.Name}' ...");
      try
      {
        // Wait until the event is available in the EventManager.
        var tournament = await TryUntil(() => EventManager.GetEvent(item.Id));
        // Build the composite event entry to add to the database.
        var composite = item.entry ?? new EventComposite(tournament as Tournament);
        Console.WriteLine($"--> Got event entry for {tournament}.");
        item.entry ??= composite;
        // Add the event to the database.
        await EventRepository.AddEvent(composite);
        Console.WriteLine($"--> Added event '{tournament}' to the database.");
        hasUpdated |= true;
      }
      catch (Exception e)
      {
        Console.WriteLine($"Failed to process event '{item.Name}': {e.Message}");
        if (item.Retries-- <= 0)
        {
          Console.WriteLine($"Event '{item.Name}' has exceeded the maximum number of retries, skipping...");
          continue;
        }
        Queue.Enqueue(item);
      }
    }

    return hasUpdated;
  }
}
