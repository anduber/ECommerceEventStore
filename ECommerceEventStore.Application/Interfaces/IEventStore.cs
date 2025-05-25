using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using ECommerceEventStore.Domain.Events;

namespace ECommerceEventStore.Application.Interfaces
{
    public interface IEventStore
    {
        Task<IEnumerable<OrderEvent>> GetEventsAsync(Guid aggregateId, CancellationToken cancellationToken);
        Task SaveEventsAsync(Guid aggregateId, IEnumerable<OrderEvent> events, int expectedVersion, CancellationToken cancellationToken);
        Task<OrderEvent> GetLastEventAsync(Guid aggregateId, CancellationToken cancellationToken);
        Task SaveSnapshotAsync(Guid aggregateId, object snapshot, int version, CancellationToken cancellationToken);
        Task<(object Snapshot, int Version)> GetSnapshotAsync(Guid aggregateId, CancellationToken cancellationToken);
    }
}