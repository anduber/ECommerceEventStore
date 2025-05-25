using System;
using System.Collections.Generic;
using System.Linq;
using ECommerceEventStore.Domain.Events;
using ECommerceEventStore.Domain.Exceptions;

namespace ECommerceEventStore.Domain.Aggregates
{
    public class OrderAggregate
    {
        private readonly List<OrderEvent> _uncommittedEvents = new();
        private readonly List<OrderItemDto> _items = new();

        public Guid Id { get; private set; }
        public Guid CustomerId { get; private set; }
        public decimal TotalAmount { get; private set; }
        public string ShippingAddress { get; private set; }
        public OrderStatus Status { get; private set; } = OrderStatus.Created;
        public int Version { get; private set; } = -1;
        public Guid? PaymentId { get; private set; }
        public Guid? ShipmentId { get; private set; }
        public string TrackingNumber { get; private set; }
        public IReadOnlyList<OrderItemDto> Items => _items.AsReadOnly();

        // For event sourcing reconstruction
        private OrderAggregate() { }

        // Factory method for creating a new order
        public static OrderAggregate CreateOrder(Guid orderId, Guid customerId, List<OrderItemDto> items, string shippingAddress)
        {
            if (items == null || !items.Any())
                throw new DomainException("Order must contain at least one item");

            var order = new OrderAggregate();
            
            var totalAmount = items.Sum(i => i.UnitPrice * i.Quantity);
            
            var @event = new OrderCreatedEvent
            {
                OrderId = orderId,
                CustomerId = customerId,
                Items = items,
                TotalAmount = totalAmount,
                ShippingAddress = shippingAddress,
                Version = 0
            };

            order.Apply(@event);
            order._uncommittedEvents.Add(@event);
            
            return order;
        }

        public void MarkAsPaid(Guid paymentId, decimal amountPaid, string paymentMethod)
        {
            if (Status == OrderStatus.Cancelled)
                throw new DomainException("Cannot pay for a cancelled order");

            if (Status == OrderStatus.Shipped)
                throw new DomainException("Order has already been shipped");

            if (Status == OrderStatus.Paid)
                throw new DomainException("Order has already been paid");

            if (amountPaid != TotalAmount)
                throw new DomainException($"Payment amount {amountPaid} does not match order total {TotalAmount}");

            var @event = new OrderPaidEvent
            {
                OrderId = Id,
                PaymentId = paymentId,
                AmountPaid = amountPaid,
                PaymentMethod = paymentMethod,
                Version = Version + 1
            };

            Apply(@event);
            _uncommittedEvents.Add(@event);
        }

        public void Ship(Guid shipmentId, string trackingNumber)
        {
            if (Status != OrderStatus.Paid)
                throw new DomainException("Cannot ship an unpaid order");

            var @event = new OrderShippedEvent
            {
                OrderId = Id,
                ShipmentId = shipmentId,
                TrackingNumber = trackingNumber,
                ShippedDate = DateTime.UtcNow,
                Version = Version + 1
            };

            Apply(@event);
            _uncommittedEvents.Add(@event);
        }

        public void Cancel(string reason, bool isRefundRequired = false)
        {
            if (Status == OrderStatus.Shipped)
                throw new DomainException("Cannot cancel an order that has been shipped");

            if (Status == OrderStatus.Cancelled)
                throw new DomainException("Order is already cancelled");

            var @event = new OrderCancelledEvent
            {
                OrderId = Id,
                CancellationReason = reason,
                IsRefundRequired = isRefundRequired && Status == OrderStatus.Paid,
                Version = Version + 1
            };

            Apply(@event);
            _uncommittedEvents.Add(@event);
        }

        public IEnumerable<OrderEvent> GetUncommittedEvents() => _uncommittedEvents.AsReadOnly();

        public void ClearUncommittedEvents() => _uncommittedEvents.Clear();

        // Apply events to reconstruct state
        public void LoadFromHistory(IEnumerable<OrderEvent> history)
        {
            foreach (var @event in history)
            {
                Apply(@event);
            }
        }

        private void Apply(OrderEvent @event)
        {
            switch (@event)
            {
                case OrderCreatedEvent created:
                    ApplyCreated(created);
                    break;
                case OrderPaidEvent paid:
                    ApplyPaid(paid);
                    break;
                case OrderShippedEvent shipped:
                    ApplyShipped(shipped);
                    break;
                case OrderCancelledEvent cancelled:
                    ApplyCancelled(cancelled);
                    break;
            }

            Version = @event.Version;
        }

        private void ApplyCreated(OrderCreatedEvent @event)
        {
            Id = @event.OrderId;
            CustomerId = @event.CustomerId;
            TotalAmount = @event.TotalAmount;
            ShippingAddress = @event.ShippingAddress;
            Status = OrderStatus.Created;
            _items.AddRange(@event.Items);
        }

        private void ApplyPaid(OrderPaidEvent @event)
        {
            PaymentId = @event.PaymentId;
            Status = OrderStatus.Paid;
        }

        private void ApplyShipped(OrderShippedEvent @event)
        {
            ShipmentId = @event.ShipmentId;
            TrackingNumber = @event.TrackingNumber;
            Status = OrderStatus.Shipped;
        }

        private void ApplyCancelled(OrderCancelledEvent @event)
        {
            Status = OrderStatus.Cancelled;
        }
    }

    public enum OrderStatus
    {
        Created,
        Paid,
        Shipped,
        Cancelled
    }
}