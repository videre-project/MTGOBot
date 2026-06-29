/** @file
  Copyright (c) 2025, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

using MTGOSDK.API.Play;
using MTGOSDK.API.Play.Tournaments;
using MTGOSDK.Core.Logging;
using MTGOSDK.Core.Reflection;
using Newtonsoft.Json;

using Database;
using Database.Schemas;


namespace Bot;

public class EventQueue : DLRWrapper
{
  public record PendingEvent(int Id, string Name, DateTime StartTime, DateTime EndTime);

  private static readonly TimeSpan MissingCompletedEventRetention = TimeSpan.FromHours(6);

  private static class PendingEventStore
  {
    private static readonly string StorePath = Path.Combine(
      AppContext.BaseDirectory,
      "pending-events.json"
    );

    public static async Task<List<PendingEvent>> LoadAsync()
    {
      try
      {
        if (!File.Exists(StorePath)) return new List<PendingEvent>();

        var json = await File.ReadAllTextAsync(StorePath);
        return JsonConvert.DeserializeObject<List<PendingEvent>>(json)
          ?? new List<PendingEvent>();
      }
      catch (Exception e)
      {
        Log.Warning("Failed to load pending event store {Path}: {Message}", StorePath, e.Message);
        return new List<PendingEvent>();
      }
    }

    public static async Task UpsertAsync(PendingEvent pendingEvent)
    {
      var events = await LoadAsync();
      events.RemoveAll(e => e.Id == pendingEvent.Id);
      events.Add(pendingEvent);
      await SaveAsync(events);
    }

    public static async Task RemoveAsync(int eventId)
    {
      var events = await LoadAsync();
      if (events.RemoveAll(e => e.Id == eventId) > 0)
      {
        await SaveAsync(events);
      }
    }

    private static async Task SaveAsync(List<PendingEvent> events)
    {
      var json = JsonConvert.SerializeObject(
        events.OrderBy(e => e.EndTime).ToList(),
        Formatting.Indented
      );
      await File.WriteAllTextAsync(StorePath, json);
    }
  }

  /// <summary>
  /// Represents a queued event to be added to the database.
  /// </summary>
  public class QueueItem
  {
    public int Id;
    public string Name;
    public DateTime StartTime;
    public DateTime EndTime;

    public Tournament? Event;
    public EventComposite? entry = null;

    public string DisplayName => EventQueue.FormatEventName(Name, Id);

    public QueueItem(dynamic @event)
    {
      Id = @event.Id;
      Name = @event.ToString();
      StartTime = @event.StartTime.ToLocalTime();
      EndTime = @event.EndTime.ToLocalTime();
      Event = @event as Tournament;
    }

    public QueueItem(PendingEvent pendingEvent)
    {
      Id = pendingEvent.Id;
      Name = pendingEvent.Name;
      StartTime = pendingEvent.StartTime;
      EndTime = pendingEvent.EndTime;
      Event = null;
    }

    public PendingEvent ToPendingEvent() =>
      new(Id, Name, StartTime, EndTime);
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

  private static string FormatEventName(string name, int id)
  {
    var suffix = $"#{id}";
    return name.Contains(suffix, StringComparison.Ordinal)
      ? name
      : $"{name} {suffix}";
  }

  private bool ContainsEvent(int eventId) =>
    Queue.Any(item => item.Id == eventId) ||
    UpcomingQueue.Any(item => item.Id == eventId);

  private static bool StartsInsideQueueWindow(dynamic @event) =>
    @event.StartTime.AddMinutes(5) < BotClient.ResetTime.ToLocalTime();

  private static void LogEventDiscoverySummary(IReadOnlyCollection<dynamic> events)
  {
    int insideWindow = 0;
    int outsideWindow = 0;
    int unreadable = 0;

    foreach (var @event in events)
    {
      try
      {
        if (StartsInsideQueueWindow(@event))
          insideWindow++;
        else
          outsideWindow++;
      }
      catch
      {
        unreadable++;
      }
    }

    Log.Information(
      "EventManager detected {Total} event(s): {InsideWindow} inside queue window, {OutsideWindow} outside queue window, {Unreadable} unreadable. Queue window ends at {WindowEnd}; reset at {ResetTime}.",
      events.Count,
      insideWindow,
      outsideWindow,
      unreadable,
      BotClient.ResetTime.ToLocalTime().AddMinutes(-5),
      BotClient.ResetTime.ToLocalTime()
    );
  }

  private static bool IsMissingEvent(Exception exception, int eventId)
  {
    for (Exception? current = exception; current != null; current = current.InnerException)
    {
      if (current.Message.Contains(
            $"Event #{eventId} could not be found",
            StringComparison.OrdinalIgnoreCase))
      {
        return true;
      }
    }

    return false;
  }

  private static bool IsExpiredMissingEvent(QueueItem item) =>
    item.EndTime <= DateTime.Now &&
    DateTime.Now - item.EndTime > MissingCompletedEventRetention;

  private static async Task<bool> RemoveExpiredMissingEvent(QueueItem item)
  {
    if (!IsExpiredMissingEvent(item))
    {
      return false;
    }

    Log.Warning(
      "Pending event {Name} ended at {EndTime} and is no longer available from EventManager; removing it from pending events.",
      item.DisplayName,
      item.EndTime
    );
    await PendingEventStore.RemoveAsync(item.Id);
    return true;
  }

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
    if (!StartsInsideQueueWindow(@event) ||
        ContainsEvent(@event.Id))
    {
      return false;
    }

    var eventEntry = new QueueItem(@event);
    if (await EventRepository.EventExists(eventEntry.Id))
    {
      await PendingEventStore.RemoveAsync(eventEntry.Id);
      return false;
    }

    Log.Information(
      "Found event {Name}: Start={StartTime}, End={EndTime}",
      eventEntry.DisplayName,
      eventEntry.StartTime,
      eventEntry.EndTime);

    await PendingEventStore.UpsertAsync(eventEntry.ToPendingEvent());

    if (@event.IsCompleted)
    {
      Log.Information("Event {Name} is already completed, adding to queue...", eventEntry.DisplayName);
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
      added |= await AddEventToQueue(@event);
    }

    return added;
  }

  private async Task RestorePendingEvents()
  {
    var pendingEvents = await PendingEventStore.LoadAsync();
    if (pendingEvents.Count == 0) return;

    Log.Information("Restoring {Count} pending event(s) from previous runs.", pendingEvents.Count);

    foreach (var pendingEvent in pendingEvents.OrderBy(e => e.EndTime))
    {
      var eventLabel = FormatEventName(pendingEvent.Name, pendingEvent.Id);

      if (ContainsEvent(pendingEvent.Id))
      {
        continue;
      }

      if (await EventRepository.EventExists(pendingEvent.Id))
      {
        await PendingEventStore.RemoveAsync(pendingEvent.Id);
        continue;
      }

      try
      {
        var @event = EventManager.GetEvent(pendingEvent.Id);
        if (@event != null)
        {
          await AddEventToQueue(@event);
          continue;
        }
      }
      catch (Exception e)
      {
        Log.Information(
          "Pending event {Name} is not currently available from EventManager; checking saved metadata: {Message}",
          eventLabel,
          e.Message
        );
      }

      var item = new QueueItem(pendingEvent);
      if (item.EndTime <= DateTime.Now)
      {
        if (!await RemoveExpiredMissingEvent(item))
        {
          Log.Information(
            "Pending event {Name} ended at {EndTime} but is not currently available from EventManager; keeping it pending.",
            item.DisplayName,
            item.EndTime
          );
        }
      }
      else
      {
        Log.Information(
          "Pending event {Name} still ends in the future at {EndTime}.",
          item.DisplayName,
          item.EndTime
        );
        UpcomingQueue.Enqueue(item);
      }
    }
  }

  /// <summary>
  /// Initializes the event queue and adds callbacks to add events to the queue.
  /// </summary>
  public async Task InitializeQueue(bool retry = true)
  {
    // On first call, wait until pgpool/postgres is reachable (e.g. after restart).
    if (retry) await EventRepository.WaitForConnectionAsync();

    // Recover events discovered before a runner sleep/restart.
    await RestorePendingEvents();

    // Enqueue or schedule all events that are already in the system.
    var detectedEvents = ((IEnumerable<dynamic>)EventManager.Events).ToList();
    LogEventDiscoverySummary(detectedEvents);
    await AddEventsToQueue(detectedEvents);
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
      EventComposite? composite = null;
      Log.Information("\nProcessing event {Name} ...", item.DisplayName);

      int retries = 0;
      int maxRetries = 5;
      bool missingEventHandled = false;
      do
      {
        try
        {
          // Wait until the event is available in the EventManager.
          var start = DateTime.Now;
          if (item.Event == null || Unbind(dro: item.Event) == null || retries > 0)
          {
            item.Event = Retry(() => EventManager.GetEvent(item.Id), raise: true) as Tournament;
            if (item.Event == null)
            {
              if (await RemoveExpiredMissingEvent(item))
              {
                missingEventHandled = true;
                break;
              }

              Log.Warning(
                "--> Event {Name} is not available from EventManager, retrying...",
                item.DisplayName
              );
              continue;
            }
          }
          var tournament = item.Event;

          if (item.entry.HasValue)
          {
            Log.Debug("--> Reusing existing event entry for {Tournament}.", tournament);
            EventComposite existingComposite = item.entry.Value;
            existingComposite.BuildCollection(tournament);
            composite = existingComposite;
          }
          else
          {
            EventComposite newComposite = new EventComposite(tournament);
            newComposite.BuildCollection();
            item.entry = newComposite;
            composite = newComposite;
          }

          Log.Information("--> Got event entry for {Name} ({Duration} s).", item.DisplayName, (DateTime.Now - start).TotalSeconds);
          break;
        }
        catch (Exception e)
        {
          if (IsMissingEvent(e, item.Id))
          {
            missingEventHandled = true;
            if (!await RemoveExpiredMissingEvent(item))
            {
              Log.Information(
                "--> Event {Name} is not currently available from EventManager; keeping it pending for a future run.",
                item.DisplayName
              );
            }
            break;
          }

          Log.Error("--> Failed to build event entry for {Name}: {Message}", item.DisplayName, e.Message);
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
      if (!composite.HasValue)
      {
        if (!missingEventHandled)
        {
          Log.Warning(
            "--> Gave up processing event {Name} after {Attempts} attempts; leaving it pending for a future run.",
            item.DisplayName,
            maxRetries + 1
          );
        }
        continue;
      }

      try
      {
        // Add the event to the database.
        var start = DateTime.Now;
        await EventRepository.AddEvent(composite.Value);
        await PendingEventStore.RemoveAsync(item.Id);
        Log.Information("--> Added event {Name} to the database ({Duration} s).", item.DisplayName, (DateTime.Now - start).TotalSeconds);
        hasUpdated |= true;
      }
      catch (Exception e)
      {
        Log.Error("--> Failed to add event {Name} to the database: {Message}", item.DisplayName, e.Message);
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
