/** @file
  Copyright (c) 2025, Cory Bennett. All rights reserved.
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

using Database;
using Database.Schemas;


namespace Bot;

public class EventQueue : DLRWrapper
{
  /// <summary>
  /// Represents a queued event to be added to the database.
  /// </summary>
  public class QueueItem(dynamic @event)
  {
    public int Id = @event.Id;
    public string Name = @event.ToString();
    public dynamic Event = @event;
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
    // else
    // {
    //   @event.TournamentStateChanged += new EventCallback<
    //     TournamentStateChangedEventArgs
    //   >((e) =>
    //   {
    //     if (e.NewValue == TournamentState.Finished)
    //     {
    //       Console.WriteLine($"Event '{@event}' is now finished, adding to queue...");
    //       Queue.Enqueue(new QueueItem(@event));
    //     }
    //   });
    // }

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
    // Enqueue or schedule all events that are already in the system.
    await AddEventsToQueue(EventManager.Events);
    Console.WriteLine($"Initialized event queue with {Queue.Count} events.");
  }

  /// <summary>
  /// Blocks the current thread and processes the event queue when updated.
  /// </summary>
  public async Task<bool> ProcessQueue(BotClient client)
  {
    bool hasUpdated = false;
    while (Queue.TryDequeue(out QueueItem? item))
    {
      EventComposite composite = default;
      Console.WriteLine($"\nProcessing event '{item.Name}' ...");

      int retries = 0;
      int maxRetries = 5;
      do
      {
        try
        {
          // Wait until the event is available in the EventManager.
          var start = DateTime.Now;
          if (Unbind(item.Event) == null || retries > 0)
          {
            item.Event = Retry(() => EventManager.GetEvent(item.Id), raise: true)!;
            if (item.Event == null)
            {
              Console.WriteLine($"--> Event '{item.Name}' is not available, skipping...");
              continue;
            }
          }
          var tournament = item.Event as Tournament;

          if (item.entry.HasValue)
          {
            Console.WriteLine($"--> Reusing existing event entry for {tournament}.");
            composite = item.entry.Value;
            composite.BuildCollection(tournament);
          }
          else
          {
            composite = new EventComposite(tournament);
            composite.BuildCollection();
            item.entry = composite;
          }

          Console.WriteLine($"--> Got event entry for {item.Name} ({(DateTime.Now - start).TotalSeconds} s).");
          break;
        }
        catch (Exception e)
        {
          Console.WriteLine($"--> Failed to build event entry for {item.Name}: {e.Message}");

          //
          // As errors may have occured due to a corrupted enumerator state,
          // we must restart the client to ensure the event can be processed
          // without causing any processing issues for future entries.
          //
          Retry(() => client.StartClient(restart: true), delay: 1000, raise: true);
        }
      }
      while (retries++ < maxRetries);
      if (composite.Equals(default)) continue;

      try
      {
        // Add the event to the database.
        var start = DateTime.Now;
        await EventRepository.AddEvent(composite);
        Console.WriteLine($"--> Added event '{item.Name}' to the database ({(DateTime.Now - start).TotalSeconds} s).");
        hasUpdated |= true;
      }
      catch (Exception e)
      {
        Console.WriteLine($"--> Failed to add event '{item.Name}' to the database: {e.Message}");
        Console.WriteLine(e.StackTrace);
        if (e.InnerException != null)
        {
          Console.WriteLine($"    Inner exception: {e.InnerException.Message}");
        }
      }
    }

    return hasUpdated;
  }
}
