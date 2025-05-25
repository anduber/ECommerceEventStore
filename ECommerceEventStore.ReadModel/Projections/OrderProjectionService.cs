using System;
using System.Diagnostics;
using System.Diagnostics.Metrics;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Confluent.Kafka;
using ECommerceEventStore.Domain.Events;
using ECommerceEventStore.ReadModel.Data;
using ECommerceEventStore.ReadModel.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using OpenTelemetry;
using OpenTelemetry.Context.Propagation;
using OpenTelemetry.Metrics;

namespace ECommerceEventStore.ReadModel.Projections
{
    public class OrderProjectionService : BackgroundService
    {
        private readonly IConsumer<string, string> _consumer;
        private readonly IServiceScopeFactory _scopeFactory;
        private readonly ILogger<OrderProjectionService> _logger;
        private readonly KafkaConsumerSettings _settings;
        private static readonly ActivitySource ActivitySource = new("ECommerceEventStore.Projections");
        private static readonly Meter Meter = new("ECommerceEventStore.Projections");
        private readonly Counter<long> _processedEventsCounter;
        private readonly Histogram<double> _projectionLatencyHistogram;
        private readonly UpDownCounter<long> _projectionLagCounter;

        public OrderProjectionService(
            IOptions<KafkaConsumerSettings> settings,
            IServiceScopeFactory scopeFactory,
            ILogger<OrderProjectionService> logger)
        {
            _settings = settings.Value;
            _scopeFactory = scopeFactory;
            _logger = logger;

            var config = new ConsumerConfig
            {
                BootstrapServers = _settings.BootstrapServers,
                GroupId = _settings.GroupId,
                AutoOffsetReset = AutoOffsetReset.Earliest,
                EnableAutoCommit = false
            };

            _consumer = new ConsumerBuilder<string, string>(config).Build();
            
            // Create metrics
            _processedEventsCounter = Meter.CreateCounter<long>("processed_events", "events", "Number of events processed");
            _projectionLatencyHistogram = Meter.CreateHistogram<double>("projection_latency", "ms", "Time taken to process an event");
            _projectionLagCounter = Meter.CreateUpDownCounter<long>("projection_lag", "events", "Number of events waiting to be processed");
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            _consumer.Subscribe(new[] { "orders.created", "orders.paid", "orders.shipped", "orders.cancelled" });

            try
            {
                while (!stoppingToken.IsCancellationRequested)
                {
                    try
                    {
                        var consumeResult = _consumer.Consume(stoppingToken);
                        
                        if (consumeResult == null) continue;
                        
                        using var activity = ActivitySource.StartActivity(
                            $"Process{consumeResult.Topic.Replace("orders.", "").ToTitleCase()}Event", 
                            ActivityKind.Consumer);
                        
                        activity?.SetTag("kafka.topic", consumeResult.Topic);
                        activity?.SetTag("kafka.partition", consumeResult.Partition.Value);
                        activity?.SetTag("kafka.offset", consumeResult.Offset.Value);
                        activity?.SetTag("order.id", consumeResult.Message.Key);
                        
                        var startTime = DateTime.UtcNow;
                        
                        await ProcessEventAsync(consumeResult.Topic, consumeResult.Message.Value, stoppingToken);
                        
                        _consumer.Commit(consumeResult);
                        _processedEventsCounter.Add(1);
                        
                        var latency = (DateTime.UtcNow - startTime).TotalMilliseconds;
                        _projectionLatencyHistogram.Record(latency);
                        
                        _logger.LogInformation(
                            "Processed event from topic {Topic} at offset {Offset} in {Latency}ms",
                            consumeResult.Topic, consumeResult.Offset.Value, latency);
                    }
                    catch (ConsumeException ex)
                    {
                        _logger.LogError(ex, "Error consuming message");
                    }
                }
            }
            finally
            {
                _consumer.Close();
            }
        }

        private async Task ProcessEventAsync(string topic, string eventJson, CancellationToken cancellationToken)
        {
            using var scope = _scopeFactory.CreateScope();
            var dbContext = scope.ServiceProvider.GetRequiredService<OrderDbContext>();

            switch (topic)
            {
                case "orders.created":
                    await HandleOrderCreatedAsync(eventJson, dbContext, cancellationToken);
                    break;
                case "orders.paid":
                    await HandleOrderPaidAsync(eventJson, dbContext, cancellationToken);
                    break;
                case "orders.shipped":
                    await HandleOrderShippedAsync(eventJson, dbContext, cancellationToken);
                    break;
                case "orders.cancelled":
                    await HandleOrderCancelledAsync(eventJson, dbContext, cancellationToken);
                    break;
            }
        }

        private async Task HandleOrderCreatedAsync(string eventJson, OrderDbContext dbContext, CancellationToken cancellationToken)
        {
            var @event = JsonSerializer.Deserialize<OrderCreatedEvent>(eventJson);
            
            var order = new Order
            {
                Id = @event.OrderId,
                CustomerId = @event.CustomerId,
                TotalAmount = @event.TotalAmount,
                ShippingAddress = @event.ShippingAddress,
                Status = "Created",
                CreatedAt = @event.Timestamp
            };

            var items = @event.Items.Select(item => new OrderItem
            {
                Id = Guid.NewGuid(),
                OrderId = @event.OrderId,
                ProductId = item.ProductId,
                ProductName = item.ProductName,
                Quantity = item.Quantity,
                UnitPrice = item.UnitPrice
            }).ToList();

            var statusHistory = new OrderStatusHistory
            {
                Id = Guid.NewGuid(),
                OrderId = @event.OrderId,
                Status = "Created",
                Timestamp = @event.Timestamp
            };

            await dbContext.Orders.AddAsync(order, cancellationToken);
            await dbContext.OrderItems.AddRangeAsync(items, cancellationToken);
            await dbContext.OrderStatusHistory.AddAsync(statusHistory, cancellationToken);
            await dbContext.SaveChangesAsync(cancellationToken);
        }

        private async Task HandleOrderPaidAsync(string eventJson, OrderDbContext dbContext, CancellationToken cancellationToken)
        {
            var @event = JsonSerializer.Deserialize<OrderPaidEvent>(eventJson);
            
            var order = await dbContext.Orders.FindAsync(new object[] { @event.OrderId }, cancellationToken);
            if (order == null) return;

            order.Status = "Paid";
            order.UpdatedAt = @event.Timestamp;
            order.PaymentId = @event.PaymentId;
            order.PaymentMethod = @event.PaymentMethod;

            var statusHistory = new OrderStatusHistory
            {
                Id = Guid.NewGuid(),
                OrderId = @event.OrderId,
                Status = "Paid",
                Timestamp = @event.Timestamp
            };

            await dbContext.OrderStatusHistory.AddAsync(statusHistory, cancellationToken);
            await dbContext.SaveChangesAsync(cancellationToken);
        }

        private async Task HandleOrderShippedAsync(string eventJson, OrderDbContext dbContext, CancellationToken cancellationToken)
        {
            var @event = JsonSerializer.Deserialize<OrderShippedEvent>(eventJson);
            
            var order = await dbContext.Orders.FindAsync(new object[] { @event.OrderId }, cancellationToken);
            if (order == null) return;

            order.Status = "Shipped";
            order.UpdatedAt = @event.Timestamp;
            order.ShipmentId = @event.ShipmentId;
            order.TrackingNumber = @event.TrackingNumber;

            var statusHistory = new OrderStatusHistory
            {
                Id = Guid.NewGuid(),
                OrderId = @event.OrderId,
                Status = "Shipped",
                Timestamp = @event.Timestamp
            };

            await dbContext.OrderStatusHistory.AddAsync(statusHistory, cancellationToken);
            await dbContext.SaveChangesAsync(cancellationToken);
        }

        private async Task HandleOrderCancelledAsync(string eventJson, OrderDbContext dbContext, CancellationToken cancellationToken)
        {
            var @event = JsonSerializer.Deserialize<OrderCancelledEvent>(eventJson);
            
            var order = await dbContext.Orders.FindAsync(new object[] { @event.OrderId }, cancellationToken);
            if (order == null) return;

            order.Status = "Cancelled";
            order.UpdatedAt = @event.Timestamp;

            var statusHistory = new OrderStatusHistory
            {
                Id = Guid.NewGuid(),
                OrderId = @event.OrderId,
                Status = "Cancelled",
                Timestamp = @event.Timestamp,
                Reason = @event.CancellationReason
            };

            await dbContext.OrderStatusHistory.AddAsync(statusHistory, cancellationToken);
            await dbContext.SaveChangesAsync(cancellationToken);
        }
    }

    public class KafkaConsumerSettings
    {
        public string BootstrapServers { get; set; } = "localhost:9092";
        public string GroupId { get; set; } = "order-projections";
    }

    public static class StringExtensions
    {
        public static string ToTitleCase(this string str)
        {
            if (string.IsNullOrEmpty(str))
                return string.Empty;

            return char.ToUpper(str[0]) + str.Substring(1);
        }
    }
}