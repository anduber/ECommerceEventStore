using System;
using MongoDB.Bson;
using MongoDB.Bson.Serialization;
using MongoDB.Bson.Serialization.Serializers;
using ECommerceEventStore.Domain.Events;
using System.Linq;

namespace ECommerceEventStore.Infrastructure.EventStore
{
    public class OrderEventSerializer : SerializerBase<OrderEvent>
    {
        public override OrderEvent Deserialize(BsonDeserializationContext context, BsonDeserializationArgs args)
        {
            // Read the document
            var document = BsonDocumentSerializer.Instance.Deserialize(context);
            
            // Get the event type from the document
            string eventType = document["_t"].AsString;
            
            // Create the appropriate event type based on the EventType field
            OrderEvent orderEvent = eventType switch
            {
                nameof(OrderCreatedEvent) => BsonSerializer.Deserialize<OrderCreatedEvent>(document),
                nameof(OrderPaidEvent) => BsonSerializer.Deserialize<OrderPaidEvent>(document),
                nameof(OrderShippedEvent) => BsonSerializer.Deserialize<OrderShippedEvent>(document),
                nameof(OrderCancelledEvent) => BsonSerializer.Deserialize<OrderCancelledEvent>(document),
                _ => throw new NotSupportedException($"Event type {eventType} is not supported.")
            };
            
            return orderEvent;
        }

        public override void Serialize(BsonSerializationContext context, BsonSerializationArgs args, OrderEvent value)
        {
            // Add the type discriminator
            var document = new BsonDocument();
            document["EventType"] = value.GetType().Name;
            
            // Instead of trying to serialize directly, we'll create a new document for each type
            // and copy the properties manually
            switch (value)
            {
                case OrderCreatedEvent created:
                    document.Add("OrderId", created.OrderId.ToString());
                    document.Add("Version", created.Version);
                    document.Add("Timestamp", created.Timestamp);
                    document.Add("CustomerId", created.CustomerId.ToString());
                    
                    // For the Items collection, create a BsonArray manually
                    var itemsArray = new BsonArray();
                    foreach (var item in created.Items)
                    {
                        var itemDoc = new BsonDocument
                        {
                            { "ProductId", item.ProductId.ToString() },
                            { "ProductName", item.ProductName },
                            { "Quantity", item.Quantity },
                            { "UnitPrice", item.UnitPrice }
                        };
                        itemsArray.Add(itemDoc);
                    }
                    document.Add("Items", itemsArray);
                    
                    document.Add("TotalAmount", created.TotalAmount);
                    document.Add("ShippingAddress", created.ShippingAddress);
                    break;
                case OrderPaidEvent paid:
                    document.Add("OrderId", paid.OrderId.ToString());
                    document.Add("Version", paid.Version);
                    document.Add("Timestamp", paid.Timestamp);
                    document.Add("PaymentId", paid.PaymentId.ToString());
                    document.Add("AmountPaid", paid.AmountPaid);
                    document.Add("PaymentMethod", paid.PaymentMethod);
                    break;
                case OrderShippedEvent shipped:
                    document.Add("OrderId", shipped.OrderId.ToString());
                    document.Add("Version", shipped.Version);
                    document.Add("Timestamp", shipped.Timestamp);
                    document.Add("ShipmentId", shipped.ShipmentId.ToString());
                    document.Add("TrackingNumber", shipped.TrackingNumber);
                    document.Add("ShippedDate", shipped.ShippedDate);
                    break;
                case OrderCancelledEvent cancelled:
                    document.Add("OrderId", cancelled.OrderId.ToString());
                    document.Add("Version", cancelled.Version);
                    document.Add("Timestamp", cancelled.Timestamp);
                    document.Add("CancellationReason", cancelled.CancellationReason);
                    document.Add("IsRefundRequired", cancelled.IsRefundRequired);
                    break;
                default:
                    throw new NotSupportedException($"Event type {value.GetType().Name} is not supported.");
            }
            
            // Write the document
            BsonDocumentSerializer.Instance.Serialize(context, document);
        }
    }
}