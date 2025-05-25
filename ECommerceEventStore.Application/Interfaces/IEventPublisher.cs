using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using ECommerceEventStore.Domain.Events;

namespace ECommerceEventStore.Application.Interfaces
{
    public interface IEventPublisher
    {
        Task PublishEventsAsync(IEnumerable<OrderEvent> events, CancellationToken cancellationToken);
    }
}