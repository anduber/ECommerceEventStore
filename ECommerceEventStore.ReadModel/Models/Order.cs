using System;
using System.Collections.Generic;

namespace ECommerceEventStore.ReadModel.Models
{
    public class Order
    {
        public Guid Id { get; set; }
        public Guid? CustomerId { get; set; }
        public decimal? TotalAmount { get; set; }
        public string? ShippingAddress { get; set; }
        public string? Status { get; set; }
        public DateTime? CreatedAt { get; set; }
        public DateTime? UpdatedAt { get; set; }
        public Guid? PaymentId { get; set; }
        public string? PaymentMethod { get; set; }
        public Guid? ShipmentId { get; set; }
        public string? TrackingNumber { get; set; }
        
        // Navigation properties
        public List<OrderItem> Items { get; set; } = new();
        public List<OrderStatusHistory> StatusHistory { get; set; } = new();
    }
}