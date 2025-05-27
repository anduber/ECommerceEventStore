using System.Reflection;
using ECommerceEventStore.Application.Commands;
using ECommerceEventStore.Application.Handlers;
using ECommerceEventStore.Application.Interfaces;
using ECommerceEventStore.Domain.Aggregates;
using ECommerceEventStore.Domain.Events;
using ECommerceEventStore.Infrastructure.EventPublisher;
using ECommerceEventStore.Infrastructure.EventStore;
using ECommerceEventStore.ReadModel.Data;
using ECommerceEventStore.ReadModel.Projections;
using MediatR;
using Microsoft.EntityFrameworkCore;
using MongoDB.Bson;
using MongoDB.Bson.Serialization;
using MongoDB.Bson.Serialization.Serializers;
using MongoDB.Driver;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;

var builder = WebApplication.CreateBuilder(args);

// Add controllers
builder.Services.AddControllers();

// Add OpenAPI/Swagger
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

// Add MediatR
builder.Services.AddMediatR(cfg => 
    cfg.RegisterServicesFromAssembly(typeof(CreateOrderCommand).Assembly));

// Configure MongoDB for Event Store
// Configure MongoDB serialization
BsonSerializer.RegisterSerializer(new GuidSerializer(GuidRepresentation.Standard));

// Register custom serializer for OrderEvent
BsonSerializer.RegisterSerializer(typeof(OrderEvent), new OrderEventSerializer());

builder.Services.Configure<MongoEventStoreSettings>(builder.Configuration.GetSection("MongoEventStore"));
builder.Services.AddSingleton<IMongoClient>(serviceProvider => new MongoClient(builder.Configuration["MongoEventStore:ConnectionString"]));
builder.Services.AddSingleton<IMongoDatabase>(serviceProvider => serviceProvider.GetRequiredService<IMongoClient>()
    .GetDatabase(builder.Configuration["MongoEventStore:DatabaseName"]));
builder.Services.AddSingleton<IEventStore, MongoEventStore>();

// Configure Kafka for Event Publishing
builder.Services.Configure<KafkaSettings>(builder.Configuration.GetSection("Kafka"));
builder.Services.AddSingleton<IEventPublisher, KafkaEventPublisher>();

// Configure SQL Server for Read Model
builder.Services.AddDbContext<OrderDbContext>(options =>
    options.UseSqlServer(builder.Configuration.GetConnectionString("OrderDatabase")));

// Register Command Handlers
builder.Services.AddScoped<OrderCommandHandlers>();

// Configure Kafka Consumer for Projections
builder.Services.Configure<KafkaConsumerSettings>(builder.Configuration.GetSection("KafkaConsumer"));
builder.Services.AddHostedService<OrderProjectionService>();

// Configure OpenTelemetry
builder.Services.AddOpenTelemetry()
    .ConfigureResource(resource => resource.AddService(
        serviceName: "ECommerceEventStore",
        serviceVersion: Assembly.GetExecutingAssembly().GetName().Version?.ToString() ?? "1.0.0"))
    .WithTracing(tracing => tracing
        .AddAspNetCoreInstrumentation()
        .AddHttpClientInstrumentation()
        .AddEntityFrameworkCoreInstrumentation()
        .AddSource("ECommerceEventStore.EventStore")
        .AddSource("ECommerceEventStore.EventPublisher")
        .AddSource("ECommerceEventStore.Projections")
        .AddOtlpExporter())
    .WithMetrics(metrics => metrics
        .AddAspNetCoreInstrumentation()
        .AddHttpClientInstrumentation()
        .AddMeter("ECommerceEventStore.EventStore")
        .AddMeter("ECommerceEventStore.EventPublisher")
        .AddMeter("ECommerceEventStore.Projections")
        .AddOtlpExporter());

var app = builder.Build();

// Configure the HTTP request pipeline
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseHttpsRedirection();
app.UseAuthorization();
app.MapControllers();

// Ensure database is created
// using (var scope = app.Services.CreateScope())
// {
//     var dbContext = scope.ServiceProvider.GetRequiredService<OrderDbContext>();
//     dbContext.Database.EnsureCreated();
// }

app.Run();
