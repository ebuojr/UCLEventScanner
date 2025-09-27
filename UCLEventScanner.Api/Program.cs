using Microsoft.EntityFrameworkCore;
using UCLEventScanner.Api.Data;
using UCLEventScanner.Api.Hubs;
using UCLEventScanner.Api.Services;

var builder = WebApplication.CreateBuilder(args);

// Configure logging
builder.Logging.ClearProviders();
builder.Logging.AddConsole();
builder.Logging.AddDebug();

// Add services to the container
builder.Services.AddControllers();

// Entity Framework with SQLite
builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseSqlite(builder.Configuration.GetConnectionString("DefaultConnection") ?? "Data Source=ucleventscanner.db"));

// RabbitMQ Services
builder.Services.AddSingleton<IRabbitMqConnectionService, RabbitMqConnectionService>();
builder.Services.AddSingleton<IDynamicQueueManager, DynamicQueueManager>();
builder.Services.AddScoped<IScanService, ScanService>();

// Hosted Services (Background Services)
builder.Services.AddHostedService<ValidationConsumer>();

// Register ResultBroadcaster as both hosted service and interface
builder.Services.AddSingleton<ResultBroadcaster>();
builder.Services.AddSingleton<IResultBroadcaster>(provider => provider.GetRequiredService<ResultBroadcaster>());
builder.Services.AddHostedService<ResultBroadcaster>(provider => provider.GetRequiredService<ResultBroadcaster>());

// Initialize queues on startup
builder.Services.AddHostedService<QueueInitializationService>();

// SignalR
builder.Services.AddSignalR(options =>
{
    options.EnableDetailedErrors = true;
});

// CORS - Allow all origins for development
builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowAll", policy =>
    {
        policy.AllowAnyOrigin()
              .AllowAnyHeader()
              .AllowAnyMethod();
    });
    
    options.AddPolicy("SignalRPolicy", policy =>
    {
        policy.SetIsOriginAllowed(_ => true) // Allow any origin
              .AllowAnyHeader()
              .AllowAnyMethod()
              .AllowCredentials();
    });
});

// Add API Explorer for Swagger/OpenAPI
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

var app = builder.Build();

// Configure the HTTP request pipeline
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
    app.UseDeveloperExceptionPage();
}

// Add logging middleware for debugging
app.Use(async (context, next) =>
{
    app.Logger.LogInformation("Request: {Method} {Path} from {Origin}", 
        context.Request.Method, 
        context.Request.Path, 
        context.Request.Headers.Origin.FirstOrDefault() ?? "No Origin");
    await next();
});

app.UseHttpsRedirection();

app.UseWebSockets();

// Use CORS before routing
app.UseCors("AllowAll");

app.UseAuthorization();

app.MapControllers();

// Map SignalR Hub with CORS
app.MapHub<ValidationHub>("/validationHub")
   .RequireCors("SignalRPolicy");

// Ensure database is created and seeded
using (var scope = app.Services.CreateScope())
{
    var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    try
    {
        context.Database.EnsureCreated();
        app.Logger.LogInformation("Database initialized successfully");
    }
    catch (Exception ex)
    {
        app.Logger.LogError(ex, "Error initializing database");
    }
}

app.Logger.LogInformation("UCL Event Scanner API starting...");

app.Run();

/// <summary>
/// Background service to initialize RabbitMQ queues on startup
/// EIP: Infrastructure setup service
/// </summary>
public class QueueInitializationService : BackgroundService
{
    private readonly IDynamicQueueManager _queueManager;
    private readonly ILogger<QueueInitializationService> _logger;

    public QueueInitializationService(IDynamicQueueManager queueManager, 
                                    ILogger<QueueInitializationService> logger)
    {
        _queueManager = queueManager;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            // Wait for other services to initialize
            await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken);
            
            // Initialize all queues based on active scanners
            await _queueManager.InitializeQueuesAsync();
            
            _logger.LogInformation("Queue initialization completed");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Queue initialization failed");
        }
    }
}