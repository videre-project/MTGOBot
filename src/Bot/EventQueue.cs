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
using MTGOSDK.Core.Logging;
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
    public DateTime StartTime = @event.StartTime.ToLocalTime();
    public DateTime EndTime = @event.EndTime.ToLocalTime();

    public dynamic Event = @event;
    public EventComposite? entry = null;
  }

  /// <summary>
  /// The current queue of events to be added to the database.
  /// </summary>
  public ConcurrentQueue<QueueItem> Queue = new();

  /// <summary>
  /// The queue of upcoming events that are still in progress.
  /// </summary>
  public ConcurrentQueue<QueueItem> UpcomingQueue = new();

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
    if (@event.StartTime.AddMinutes(5) >= BotClient.ResetTime.ToLocalTime() ||
        Queue.Contains(new QueueItem(@event)) ||
        await EventRepository.EventExists(@event.Id))
    {
      return false;
    }

    var eventEntry = new QueueItem(@event);
    Log.Information(
      "Found event {Name}: Start={StartTime}, End={EndTime}",
      eventEntry.Name,
      eventEntry.StartTime,
      eventEntry.EndTime);

    if (@event.IsCompleted)
    {
      Log.Information("Event {Name} is already completed, adding to queue...", eventEntry.Name);
      Queue.Enqueue(eventEntry);
    }
    else
    {
      UpcomingQueue.Enqueue(eventEntry);
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
  public async Task InitializeQueue(bool retry = true)
  {
    // Enqueue or schedule all events that are already in the system.
    await AddEventsToQueue(EventManager.Events);
    if (Queue.Count > 0)
    {
      Log.Information("Initialized event queue with {Count} events.", Queue.Count);
    }
    else if (retry)
    {
      // Lets wait 30 seconds before checking again.
      await Task.Delay(30_000);
      await InitializeQueue(false);
    }
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
      Log.Information("\nProcessing event {Name} ...", item.Name);

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
              Log.Information("--> Event '{Name}' is not available, skipping...", item.Name);
              continue;
            }
          }
          var tournament = item.Event as Tournament;

          if (item.entry.HasValue)
          {
            Log.Debug("--> Reusing existing event entry for {Tournament}.", tournament);
            composite = item.entry.Value;
            composite.BuildCollection(tournament);
          }
          else
          {
            composite = new EventComposite(tournament);
            composite.BuildCollection();
            item.entry = composite;
          }

          Log.Information("--> Got event entry for {Name} ({Duration} s).", item.Name, (DateTime.Now - start).TotalSeconds);
          break;
        }
        catch (Exception e)
        {
          Log.Error("--> Failed to build event entry for {Name}: {Message}", item.Name, e.Message);
          Log.Trace("{StackTrace}", (object?)e.StackTrace ?? "");

          // If there is a nested exception, we should iterate over and log it.
          if (e.InnerException != null)
          {
            Log.Error("--> Inner exception: {Message}", e.InnerException.Message);
            Log.Trace("{StackTrace}", (object?)e.InnerException.StackTrace ?? "");
          }

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
        Log.Information("--> Added event {Name} to the database ({Duration} s).", item.Name, (DateTime.Now - start).TotalSeconds);
        hasUpdated |= true;
      }
      catch (Exception e)
      {
        Log.Error("--> Failed to add event '{Name}' to the database: {Message}", item.Name, e.Message);
        Log.Trace("{StackTrace}", (object?)e.StackTrace ?? "");
        if (e.InnerException != null)
        {
          Log.Error("    Inner exception: {Message}", e.InnerException.Message);
          Log.Trace("{StackTrace}", (object?)e.InnerException.StackTrace ?? "");
        }
      }
    }

    return hasUpdated;
  }
}
