using System;
using System.Collections.Generic;
using ECommerceEventStore.Domain.Events;
using MediatR;

namespace ECommerceEventStore.Application.Commands
{
    public class CreateOrderCommand : IRequest<Guid>
    {
        public Guid CustomerId { get; set; }
        public List<OrderItemDto> Items { get; set; } = new();
        public string ShippingAddress { get; set; }
    }

    public class OrderItemDto
    {
        public Guid ProductId { get; set; }
        public string ProductName { get; set; }
        public int Quantity { get; set; }
        public decimal UnitPrice { get; set; }

        public Domain.Events.OrderItemDto ToEventDto() => new()
        {
            ProductId = ProductId,
            ProductName = ProductName,
            Quantity = Quantity,
            UnitPrice = UnitPrice
        };
    }
}