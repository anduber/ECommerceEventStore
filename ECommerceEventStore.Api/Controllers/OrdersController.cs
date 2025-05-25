 using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using ECommerceEventStore.Application.Commands;
using ECommerceEventStore.ReadModel.Data;
using ECommerceEventStore.ReadModel.Models;
using MediatR;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace ECommerceEventStore.Api.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class OrdersController(
        IMediator mediator,
        OrderDbContext dbContext,
        ILogger<OrdersController> logger)
        : ControllerBase
    {
        private readonly ILogger<OrdersController> _logger = logger;

        [HttpPost]
        public async Task<IActionResult> CreateOrder([FromBody] CreateOrderRequest request, CancellationToken cancellationToken)
        {
            var command = new CreateOrderCommand
            {
                CustomerId = request.CustomerId,
                ShippingAddress = request.ShippingAddress,
                Items = request.Items.ConvertAll(i => new OrderItemDto
                {
                    ProductId = i.ProductId,
                    ProductName = i.ProductName,
                    Quantity = i.Quantity,
                    UnitPrice = i.UnitPrice
                })
            };

            var orderId = await mediator.Send(command, cancellationToken);
            return CreatedAtAction(nameof(GetOrder), new { id = orderId }, new { OrderId = orderId });
        }

        [HttpGet("{id}")]
        public async Task<IActionResult> GetOrder(Guid id, CancellationToken cancellationToken)
        {
            var order = await dbContext.Orders
                .Include(o => o.Items)
                .FirstOrDefaultAsync(o => o.Id == id, cancellationToken);

            if (order == null)
                return NotFound();

            return Ok(order);
        }

        [HttpGet("customer/{customerId}")]
        public async Task<IActionResult> GetOrdersByCustomer(Guid customerId, CancellationToken cancellationToken)
        {
            var orders = await dbContext.Orders
                .Where(o => o.CustomerId == customerId)
                .ToListAsync(cancellationToken);

            return Ok(orders);
        }

        [HttpPost("{id}/pay")]
        public async Task<IActionResult> PayOrder(Guid id, [FromBody] PayOrderRequest request, CancellationToken cancellationToken)
        {
            var command = new PayOrderCommand
            {
                OrderId = id,
                PaymentId = request.PaymentId,
                Amount = request.Amount,
                PaymentMethod = request.PaymentMethod
            };

            var result = await mediator.Send(command, cancellationToken);
            return Ok(new { Success = result });
        }

        [HttpPost("{id}/ship")]
        public async Task<IActionResult> ShipOrder(Guid id, [FromBody] ShipOrderRequest request, CancellationToken cancellationToken)
        {
            var command = new ShipOrderCommand
            {
                OrderId = id,
                ShipmentId = request.ShipmentId,
                TrackingNumber = request.TrackingNumber
            };

            var result = await mediator.Send(command, cancellationToken);
            return Ok(new { Success = result });
        }

        [HttpPost("{id}/cancel")]
        public async Task<IActionResult> CancelOrder(Guid id, [FromBody] CancelOrderRequest request, CancellationToken cancellationToken)
        {
            var command = new CancelOrderCommand
            {
                OrderId = id,
                Reason = request.Reason
            };

            var result = await mediator.Send(command, cancellationToken);
            return Ok(new { Success = result });
        }

        [HttpGet("{id}/history")]
        public async Task<IActionResult> GetOrderHistory(Guid id, CancellationToken cancellationToken)
        {
            var history = await dbContext.OrderStatusHistory
                .Where(h => h.OrderId == id)
                .OrderBy(h => h.Timestamp)
                .ToListAsync(cancellationToken);

            if (!history.Any())
                return NotFound();

            return Ok(history);
        }
    }

    public class CreateOrderRequest
    {
        public Guid CustomerId { get; set; }
        public List<OrderItemRequest> Items { get; set; } = new();
        public string ShippingAddress { get; set; }
    }

    public class OrderItemRequest
    {
        public Guid ProductId { get; set; }
        public string ProductName { get; set; }
        public int Quantity { get; set; }
        public decimal UnitPrice { get; set; }
    }

    public class PayOrderRequest
    {
        public Guid PaymentId { get; set; }
        public decimal Amount { get; set; }
        public string PaymentMethod { get; set; }
    }

    public class ShipOrderRequest
    {
        public Guid ShipmentId { get; set; }
        public string TrackingNumber { get; set; }
    }

    public class CancelOrderRequest
    {
        public string Reason { get; set; }
    }
}