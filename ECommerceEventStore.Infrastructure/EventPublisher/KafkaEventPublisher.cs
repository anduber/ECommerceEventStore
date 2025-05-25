using System.Collections.Generic;
using System.Diagnostics;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Confluent.Kafka;
using ECommerceEventStore.Application.Interfaces;
using ECommerceEventStore.Domain.Events;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using OpenTelemetry;
using OpenTelemetry.Context.Propagation;

namespace ECommerceEventStore.Infrastructure.EventPublisher
{
    public class KafkaEventPublisher : IEventPublisher
    {
        private readonly IProducer<string, string> _producer;
        private readonly KafkaSettings _settings;
        private readonly ILogger<KafkaEventPublisher> _logger;
        private static readonly ActivitySource ActivitySource = new("ECommerceEventStore.EventPublisher");

        public KafkaEventPublisher(
            IOptions<KafkaSettings> settings,
            ILogger<KafkaEventPublisher> logger)
        {
            _settings = settings.Value;
            _logger = logger;

            var config = new ProducerConfig
            {
                BootstrapServers = _settings.BootstrapServers,
                ClientId = _settings.ClientId
            };

            _producer = new ProducerBuilder<string, string>(config).Build();
        }

        public async Task PublishEventsAsync(IEnumerable<OrderEvent> events, CancellationToken cancellationToken)
        {
            using var activity = ActivitySource.StartActivity("PublishEvents", ActivityKind.Producer);
            
            foreach (var @event in events)
            {
                var eventType = @event.GetType().Name;
                var topic = $"orders.{eventType.Replace("Event", "").ToLower()}";
                
                activity?.SetTag("event.type", eventType);
                activity?.SetTag("kafka.topic", topic);
                activity?.SetTag("order.id", @event.OrderId);
                
                _logger.LogInformation("Publishing {EventType} to topic {Topic}", eventType, topic);
                
                var message = new Message<string, string>
                {
                    Key = @event.OrderId.ToString(),
                    Value = JsonSerializer.Serialize(@event, @event.GetType())
                };
                
                try 
                {
                    var result = await _producer.ProduceAsync(topic, message, cancellationToken);
                    _logger.LogInformation("Published event to {Topic} at offset {Offset}", 
                        result.Topic, result.Offset.Value);
                }
                catch (ProduceException<string, string> ex)
                {
                    _logger.LogError(ex, "Failed to publish {EventType} for order {OrderId}", 
                        eventType, @event.OrderId);
                    throw;
                }
            }
        }
    }

    public class KafkaSettings
    {
        public string BootstrapServers { get; set; } = "localhost:9092";
        public string ClientId { get; set; } = "ecommerce-event-store";
    }
}