using System;
using MediatR;

namespace ECommerceEventStore.Application.Commands
{
    public class CancelOrderCommand : IRequest<bool>
    {
        public Guid OrderId { get; set; }
        public string Reason { get; set; }
    }
}