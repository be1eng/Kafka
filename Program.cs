using Confluent.Kafka;
using Microsoft.AspNetCore.Mvc;

var builder = WebApplication.CreateBuilder(args);

// Configuración del servidor Kestrel para escuchar en todas las interfaces
builder.WebHost.ConfigureKestrel(serverOptions =>
{
    serverOptions.ListenAnyIP(5000); 
});

// Añadir CORS
builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowAll", // Puedes cambiar "AllowAll", por otro cuando sea produccion
        builder =>
        {
            builder.AllowAnyOrigin()
                   .AllowAnyMethod()
                   .AllowAnyHeader();
        });
});

// Add services to the container.
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

// Singleton Consumer Factory
builder.Services.AddSingleton<IKafkaConsumerFactory, KafkaConsumerFactory>();

var app = builder.Build();

// Aplicar CORS
app.UseCors("AllowAll");

app.UseSwaggerUI(c =>
{
    c.SwaggerEndpoint("/swagger/v1/swagger.json", "My API V1");
    c.RoutePrefix = "api-docs";
});

// Implementar un Middleware simple para autenticación básica
app.Use(async (context, next) =>
{
    string authHeader = context.Request.Headers["Authorization"];
    if (authHeader != null && authHeader.StartsWith("Basic"))
    {
        // Implementa aquí la lógica de verificación de tus credenciales
        await next();
    }
    else
    {
        context.Response.Headers["WWW-Authenticate"] = "Basic";
        context.Response.StatusCode = 401; // No autorizado
        await context.Response.WriteAsync("Access Denied");
    }
});

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment() || app.Environment.IsProduction())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

// Redirection from root to Swagger UI
app.MapGet("/", () => Results.Redirect("/swagger")).ExcludeFromDescription();

// Endpoint to consume messages
app.MapGet("/consume", async (HttpContext context, IKafkaConsumerFactory consumerFactory, string bootstrapServers) =>
{
    try
    {
        var consumer = consumerFactory.CreateConsumer(bootstrapServers, "hello-world-topic");
        var result = consumer.Consume(context.RequestAborted);
        return Results.Ok(new
        {
            Message = result.Message.Value,
            Key = result.Message.Key,
            Offset = result.Offset.Value,
            Partition = result.Partition.Value,
            Timestamp = result.Timestamp.UtcDateTime
        });
    }
    catch (ConsumeException e)
    {
        app.Logger.LogError($"Kafka consume error: {e.Error.Reason}");
        return Results.Problem("Error consuming from Kafka.");
    }
    catch (OperationCanceledException)
    {
        return Results.Problem("Consuming was cancelled.");
    }
});

// Endpoint to send messages
app.MapPost("/send", async (string topic, string key, string value, string bootstrapServers) =>
{
    var config = new ProducerConfig { BootstrapServers = bootstrapServers };
    using var producer = new ProducerBuilder<string, string>(config).Build();
    try
    {
        var result = await producer.ProduceAsync(topic, new Message<string, string> { Key = key, Value = value });
        return Results.Ok($"Message sent. Key: {key}, Value: {value}, to {result.TopicPartitionOffset}");
    }
    catch (ProduceException<string, string> e)
    {
        return Results.Problem($"An error occurred: {e.Error.Reason}");
    }
});

app.Run();

// Interface for Kafka Consumer
public interface IKafkaConsumer
{
    ConsumeResult<Ignore, string> Consume(CancellationToken cancellationToken);
}

// Interface for Kafka Consumer Factory
public interface IKafkaConsumerFactory
{
    IKafkaConsumer CreateConsumer(string bootstrapServers, string topic);
}

// Implementation of Kafka Consumer
public class KafkaConsumer : IKafkaConsumer
{
    private readonly IConsumer<Ignore, string> _consumer;

    public KafkaConsumer(IConsumer<Ignore, string> consumer)
    {
        _consumer = consumer;
    }

    public ConsumeResult<Ignore, string> Consume(CancellationToken cancellationToken)
    {
        return _consumer.Consume(cancellationToken);
    }
}

// Implementation of Kafka Consumer Factory
public class KafkaConsumerFactory : IKafkaConsumerFactory
{
    public IKafkaConsumer CreateConsumer(string bootstrapServers, string topic)
    {
        var config = new ConsumerConfig
        {
            BootstrapServers = bootstrapServers,
            GroupId = "test-consumer-group",
            AutoOffsetReset = AutoOffsetReset.Earliest,
            EnableAutoCommit = true, 
            SecurityProtocol = SecurityProtocol.Plaintext 
        };
        var consumerBuilder = new ConsumerBuilder<Ignore, string>(config).Build();
        consumerBuilder.Subscribe(topic);
        return new KafkaConsumer(consumerBuilder);
    }
}

