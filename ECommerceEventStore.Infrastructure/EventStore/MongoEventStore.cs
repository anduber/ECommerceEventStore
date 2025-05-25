using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using ECommerceEventStore.Application.Interfaces;
using ECommerceEventStore.Domain.Aggregates;
using ECommerceEventStore.Domain.Events;
using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;
using MongoDB.Driver;
using OpenTelemetry;
using OpenTelemetry.Context.Propagation;
using OpenTelemetry.Trace;

namespace ECommerceEventStore.Infrastructure.EventStore
{
    public class MongoEventStore : IEventStore
    {        
        private readonly IMongoCollection<EventDocument> _eventsCollection;
        private readonly IMongoCollection<SnapshotDocument> _snapshotsCollection;
        private static readonly ActivitySource ActivitySource = new("ECommerceEventStore.EventStore");

        public MongoEventStore(IMongoDatabase database)
        {
            _eventsCollection = database.GetCollection<EventDocument>("events");
            _snapshotsCollection = database.GetCollection<SnapshotDocument>("snapshots");
            
            // Ensure indexes for better performance
            CreateIndexesAsync().GetAwaiter().GetResult();
        }
        
        private async Task CreateIndexesAsync()
        {
            // Create index on AggregateId and Version for events
            var eventsIndexKeys = Builders<EventDocument>.IndexKeys
                .Ascending(e => e.AggregateId)
                .Ascending(e => e.Version);
            
            var eventsIndexModel = new CreateIndexModel<EventDocument>(
                eventsIndexKeys, 
                new CreateIndexOptions { Unique = true, Name = "AggregateId_Version" }
            );
            
            await _eventsCollection.Indexes.CreateOneAsync(eventsIndexModel);
            
            // Create index on AggregateId for snapshots
            var snapshotsIndexKeys = Builders<SnapshotDocument>.IndexKeys
                .Ascending(s => s.AggregateId);
            
            var snapshotsIndexModel = new CreateIndexModel<SnapshotDocument>(
                snapshotsIndexKeys, 
                new CreateIndexOptions { Unique = true, Name = "AggregateId" }
            );
            
            await _snapshotsCollection.Indexes.CreateOneAsync(snapshotsIndexModel);
        }

        public async Task<IEnumerable<OrderEvent>> GetEventsAsync(Guid aggregateId, CancellationToken cancellationToken)
        {
            using var activity = ActivitySource.StartActivity("GetEvents", ActivityKind.Client);
            activity?.SetTag("aggregateId", aggregateId);
            
            var filter = Builders<EventDocument>.Filter.Eq(e => e.AggregateId, aggregateId);
            var sort = Builders<EventDocument>.Sort.Ascending(e => e.Version);
            
            var documents = await _eventsCollection.Find(filter)
                .Sort(sort)
                .ToListAsync(cancellationToken);
            
            activity?.SetTag("eventCount", documents.Count);
            
            return documents.Select(d => d.Event).ToList();
        }

        public async Task SaveEventsAsync(Guid aggregateId, IEnumerable<OrderEvent> events, int expectedVersion, CancellationToken cancellationToken)
        {
            using var activity = ActivitySource.StartActivity("SaveEvents", ActivityKind.Client);
            activity?.SetTag("aggregateId", aggregateId);
            
            var eventsList = events.ToList();
            activity?.SetTag("eventCount", eventsList.Count);
            
            if (!eventsList.Any()) return;
            
            // Check for concurrency conflicts
            if (expectedVersion >= 0)
            {
                var lastEvent = await GetLastEventAsync(aggregateId, cancellationToken);
                var lastVersion = lastEvent != null ? lastEvent.Version : -1;
                
                if (lastVersion != expectedVersion)
                {
                    throw new ConcurrencyException($"Concurrency conflict detected for aggregate {aggregateId}. Expected version {expectedVersion}, but got {lastVersion}");
                }
            }
            
            // Save events
            var documents = eventsList.Select(e => new EventDocument
            {
                AggregateId = aggregateId,
                Version = e.Version,
                Timestamp = e.Timestamp,
                EventType = e.GetType().Name,
                Event = e
            });
            
            try
            {
                await _eventsCollection.InsertManyAsync(documents, cancellationToken: cancellationToken);
                
                // Check if we need to create a snapshot (every 50 events)
                var lastEvent = eventsList.Last();
                if (lastEvent.Version > 0 && lastEvent.Version % 50 == 0)
                {
                    // Reconstruct the aggregate to create a snapshot
                    var allEvents = await GetEventsAsync(aggregateId, cancellationToken);
                    
                    // Create a new order using the first event
                    var firstEvent = allEvents.First() as OrderCreatedEvent;
                    if (firstEvent == null)
                        throw new ApplicationException($"First event for order {aggregateId} is not OrderCreatedEvent");
                        
                    var aggregate = OrderAggregate.CreateOrder(
                        firstEvent.OrderId,
                        firstEvent.CustomerId,
                        firstEvent.Items,
                        firstEvent.ShippingAddress);
                        
                    // Apply the rest of the events
                    aggregate.LoadFromHistory(allEvents.Skip(1));
                    
                    await SaveSnapshotAsync(aggregateId, aggregate, lastEvent.Version, cancellationToken);
                }
            }
            catch (MongoWriteException ex) when (ex.WriteError.Category == ServerErrorCategory.DuplicateKey)
            {
                throw new ConcurrencyException($"Concurrency conflict detected for aggregate {aggregateId}. Duplicate event version.", ex);
            }
        }

        public async Task<OrderEvent> GetLastEventAsync(Guid aggregateId, CancellationToken cancellationToken)
        {
            using var activity = ActivitySource.StartActivity("GetLastEvent", ActivityKind.Client);
            activity?.SetTag("aggregateId", aggregateId);
            
            var filter = Builders<EventDocument>.Filter.Eq(e => e.AggregateId, aggregateId);
            var sort = Builders<EventDocument>.Sort.Descending(e => e.Version);
            
            var document = await _eventsCollection.Find(filter)
                .Sort(sort)
                .FirstOrDefaultAsync(cancellationToken);
            
            return document?.Event;
        }

        public async Task SaveSnapshotAsync(Guid aggregateId, object snapshot, int version, CancellationToken cancellationToken)
        {
            using var activity = ActivitySource.StartActivity("SaveSnapshot", ActivityKind.Client);
            activity?.SetTag("aggregateId", aggregateId);
            activity?.SetTag("version", version);
            
            var document = new SnapshotDocument
            {
                AggregateId = aggregateId,
                Version = version,
                Timestamp = DateTime.UtcNow,
                Snapshot = snapshot
            };
            
            var filter = Builders<SnapshotDocument>.Filter.Eq(s => s.AggregateId, aggregateId);
            var options = new ReplaceOptions { IsUpsert = true };
            
            await _snapshotsCollection.ReplaceOneAsync(filter, document, options, cancellationToken);
        }

        public async Task<(object Snapshot, int Version)> GetSnapshotAsync(Guid aggregateId, CancellationToken cancellationToken)
        {
            using var activity = ActivitySource.StartActivity("GetSnapshot", ActivityKind.Client);
            activity?.SetTag("aggregateId", aggregateId);
            
            var filter = Builders<SnapshotDocument>.Filter.Eq(s => s.AggregateId, aggregateId);
            var document = await _snapshotsCollection.Find(filter).FirstOrDefaultAsync(cancellationToken);
            
            if (document == null)
            {
                return (null, -1);
            }
            
            activity?.SetTag("snapshotVersion", document.Version);
            return (document.Snapshot, document.Version);
        }
    }

    public class ConcurrencyException : Exception
    {
        public ConcurrencyException(string message) : base(message)
        {
        }

        public ConcurrencyException(string message, Exception innerException) : base(message, innerException)
        {
        }
    }

    public class EventDocument
    {
        [BsonId]
        [BsonRepresentation(BsonType.ObjectId)]
        public string Id { get; set; } = ObjectId.GenerateNewId().ToString();
        
        public Guid AggregateId { get; set; }
        
        public int Version { get; set; }
        
        public DateTime Timestamp { get; set; }
        
        public string EventType { get; set; }
        
        public OrderEvent Event { get; set; }
    }

    public class SnapshotDocument
    {
        [BsonId]
        [BsonRepresentation(BsonType.ObjectId)]
        public string Id { get; set; } = ObjectId.GenerateNewId().ToString();
        
        public Guid AggregateId { get; set; }
        
        public int Version { get; set; }
        
        public DateTime Timestamp { get; set; }
        
        public object Snapshot { get; set; }
    }
    
    public class MongoEventStoreSettings
    {
        public string ConnectionString { get; set; } = "mongodb://localhost:27017";
        public string DatabaseName { get; set; } = "ECommerceEventStore";
        public string EventsCollectionName { get; set; } = "Events";
        public string SnapshotsCollectionName { get; set; } = "Snapshots";
        public int SnapshotFrequency { get; set; } = 50;
    }
}