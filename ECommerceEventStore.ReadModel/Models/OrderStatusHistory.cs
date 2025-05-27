using System;

namespace ECommerceEventStore.ReadModel.Models
{
    public class OrderStatusHistory
    {
        public Guid Id { get; set; }
        public Guid? OrderId { get; set; }
        public string? Status { get; set; }
        public DateTime? Timestamp { get; set; }
        public string? Reason { get; set; }
    }
}