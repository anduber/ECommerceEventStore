using System;
using System.Collections.Generic;

namespace ECommerceEventStore.Domain.Events
{
    public abstract record OrderEvent
    {
        public Guid OrderId { get; init; }
        public int Version { get; init; }
        public DateTime Timestamp { get; init; } = DateTime.UtcNow;
    }

    public record OrderCreatedEvent : OrderEvent
    {
        public Guid CustomerId { get; init; }
        public List<OrderItemDto> Items { get; init; } = new();
        public decimal TotalAmount { get; init; }
        public string ShippingAddress { get; init; }
    }

    public record OrderPaidEvent : OrderEvent
    {
        public Guid PaymentId { get; init; }
        public decimal AmountPaid { get; init; }
        public string PaymentMethod { get; init; }
    }

    public record OrderShippedEvent : OrderEvent
    {
        public Guid ShipmentId { get; init; }
        public string TrackingNumber { get; init; }
        public DateTime ShippedDate { get; init; }
    }

    public record OrderCancelledEvent : OrderEvent
    {
        public string CancellationReason { get; init; }
        public bool IsRefundRequired { get; init; }
    }

    public record OrderItemDto
    {
        public Guid ProductId { get; init; }
        public string ProductName { get; init; }
        public int Quantity { get; init; }
        public decimal UnitPrice { get; init; }
    }
}