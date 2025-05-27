using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using ECommerceEventStore.Application.Commands;
using ECommerceEventStore.Application.Interfaces;
using ECommerceEventStore.Domain.Aggregates;
using ECommerceEventStore.Domain.Events;
using MediatR;
using Microsoft.Extensions.Logging;

namespace ECommerceEventStore.Application.Handlers
{
    public class OrderCommandHandlers : 
        IRequestHandler<CreateOrderCommand, Guid>,
        IRequestHandler<PayOrderCommand, bool>,
        IRequestHandler<ShipOrderCommand, bool>,
        IRequestHandler<CancelOrderCommand, bool>
    {
        private readonly IEventStore _eventStore;
        private readonly IEventPublisher _eventPublisher;
        private readonly ILogger<OrderCommandHandlers> _logger;

        public OrderCommandHandlers(
            IEventStore eventStore, 
            IEventPublisher eventPublisher,
            ILogger<OrderCommandHandlers> logger)
        {
            _eventStore = eventStore;
            _eventPublisher = eventPublisher;
            _logger = logger;
        }

        public async Task<Guid> Handle(CreateOrderCommand request, CancellationToken cancellationToken)
        {
            var orderId = Guid.NewGuid();
            
            _logger.LogInformation("Creating new order {OrderId} for customer {CustomerId}", orderId, request.CustomerId);
            
            var items = request.Items.Select(i => i.ToEventDto()).ToList();
            var order = OrderAggregate.CreateOrder(orderId, request.CustomerId, items, request.ShippingAddress);
            
            await _eventStore.SaveEventsAsync(order.Id, order.GetUncommittedEvents(), -1, cancellationToken);
            await _eventPublisher.PublishEventsAsync(order.GetUncommittedEvents(), cancellationToken);
            
            order.ClearUncommittedEvents();
            
            return orderId;
        }

        public async Task<bool> Handle(PayOrderCommand request, CancellationToken cancellationToken)
        {
            try
            {
                _logger.LogInformation("Processing payment for order {OrderId}", request.OrderId);
            
                var order = await LoadOrderAsync(request.OrderId, cancellationToken);
            
                order.MarkAsPaid(request.PaymentId, request.Amount, request.PaymentMethod);
            
                await _eventStore.SaveEventsAsync(order.Id, order.GetUncommittedEvents(), order.Version - order.GetUncommittedEvents().Count(), cancellationToken);
                await _eventPublisher.PublishEventsAsync(order.GetUncommittedEvents(), cancellationToken);
            
                order.ClearUncommittedEvents();
            
                return true;
            }
            catch (Exception e)
            {
                Console.WriteLine(e);
                throw;
            }
            
        }

        public async Task<bool> Handle(ShipOrderCommand request, CancellationToken cancellationToken)
        {
            _logger.LogInformation("Shipping order {OrderId}", request.OrderId);
            
            var order = await LoadOrderAsync(request.OrderId, cancellationToken);
            
            order.Ship(request.ShipmentId, request.TrackingNumber);
            
            await _eventStore.SaveEventsAsync(order.Id, order.GetUncommittedEvents(), order.Version - order.GetUncommittedEvents().Count(), cancellationToken);
            await _eventPublisher.PublishEventsAsync(order.GetUncommittedEvents(), cancellationToken);
            
            order.ClearUncommittedEvents();
            
            return true;
        }

        public async Task<bool> Handle(CancelOrderCommand request, CancellationToken cancellationToken)
        {
            _logger.LogInformation("Cancelling order {OrderId}", request.OrderId);
            
            var order = await LoadOrderAsync(request.OrderId, cancellationToken);
            
            order.Cancel(request.Reason, order.Status == OrderStatus.Paid);
            
            await _eventStore.SaveEventsAsync(order.Id, order.GetUncommittedEvents(), order.Version - order.GetUncommittedEvents().Count(), cancellationToken);
            await _eventPublisher.PublishEventsAsync(order.GetUncommittedEvents(), cancellationToken);
            
            order.ClearUncommittedEvents();
            
            return true;
        }

        private async Task<OrderAggregate> LoadOrderAsync(Guid orderId, CancellationToken cancellationToken)
        {
            var events = await _eventStore.GetEventsAsync(orderId, cancellationToken);
            
            if (!events.Any())
                throw new ApplicationException($"Order with id {orderId} not found");
            
            // Create a new order using the first event (which should be OrderCreatedEvent)
            var firstEvent = events.First() as OrderCreatedEvent;
            if (firstEvent == null)
                throw new ApplicationException($"First event for order {orderId} is not OrderCreatedEvent");
                
            var order = OrderAggregate.CreateOrder(
                firstEvent.OrderId,
                firstEvent.CustomerId,
                firstEvent.Items,
                firstEvent.ShippingAddress);
                
            // Apply the rest of the events
            order.LoadFromHistory(events.Skip(1));
            
            return order;
        }
    }
}