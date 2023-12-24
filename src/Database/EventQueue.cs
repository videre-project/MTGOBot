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


namespace Database;

public class EventQueue : DLRWrapper<ConcurrentQueue<Tournament>>
{
  /// <summary>
  /// The current queue of events to be added to the database.
  /// </summary>
  public ConcurrentQueue<int> Queue = new();

  public EventQueue() => InitializeQueue().Wait();

  /// <summary>
  /// Adds an event to the queue if or once the tournament has finished.
  /// </summary>
  /// <param name="event">The event to add to the queue.</param>
  /// <returns>True if the event was added to the queue, false otherwise.</returns>
  public async Task<bool> AddEventToQueue(dynamic @event)
  {
    // Check whether the event is a tournament type defined in the schema.
    if (@event is not Tournament ||
        @event.StartTime > DateTime.Now.AddDays(1) ||
        @event.ToString().Contains("Queue") ||
        @event.ToString().Contains("Draft"))
    {
      return false;
    }

    // Check that the event is not already added to the queue or database.
    if (Queue.Contains((int)@event.Id) ||
        await EventRepository.EventExists(@event.Id))
    {
      return false;
    }

    if (@event.IsCompleted)
    {
      Console.WriteLine($"Event '{@event}' is already completed, adding to queue...");
      Queue.Enqueue(@event.Id);
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
          Queue.Enqueue(@event.Id);
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
  public async Task ProcessQueue()
  {
    while (Queue.TryDequeue(out int id))
    {
      var tournament = await TryUntil(() => EventManager.GetEvent(id)) as Tournament;
      Console.WriteLine($"Processing event {tournament} ...");
      var composite = await TryUntil(() => new EventComposite(tournament));
      Console.WriteLine($"--> Got event entry for {tournament}.");

      await EventRepository.AddEvent(composite);
      Console.WriteLine($"--> Added event '{tournament}' to the database.");
    }
  }
}
