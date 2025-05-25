using System;
using MediatR;

namespace ECommerceEventStore.Application.Commands
{
    public class ShipOrderCommand : IRequest<bool>
    {
        public Guid OrderId { get; set; }
        public Guid ShipmentId { get; set; }
        public string TrackingNumber { get; set; }
    }
}