using System;
using MediatR;

namespace ECommerceEventStore.Application.Commands
{
    public class PayOrderCommand : IRequest<bool>
    {
        public Guid OrderId { get; set; }
        public Guid PaymentId { get; set; }
        public decimal Amount { get; set; }
        public string PaymentMethod { get; set; }
    }
}