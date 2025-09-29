using Microsoft.EntityFrameworkCore;
using UCLEventScanner.Api.Data;
using UCLEventScanner.Api.Hubs;
using UCLEventScanner.Api.Services;

var builder = WebApplication.CreateBuilder(args);

builder.Logging.ClearProviders();
builder.Logging.AddConsole();
builder.Logging.AddDebug();

builder.Services.AddControllers();

builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseSqlite(builder.Configuration.GetConnectionString("DefaultConnection") ?? "Data Source=ucleventscanner.db"));

builder.Services.AddSingleton<IRabbitMqConnectionService, RabbitMqConnectionService>();
builder.Services.AddSingleton<IDynamicQueueManager, DynamicQueueManager>();
builder.Services.AddScoped<IScanService, ScanService>();

builder.Services.AddHostedService<ValidationConsumer>();

builder.Services.AddSingleton<IResultBroadcaster, ResultBroadcaster>();

builder.Services.AddSignalR(options =>
{
    options.EnableDetailedErrors = true;
});

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
        policy.SetIsOriginAllowed(_ => true)
              .AllowAnyHeader()
              .AllowAnyMethod()
              .AllowCredentials();
    });
});

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
    app.UseDeveloperExceptionPage();
}

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
app.UseCors("AllowAll");
app.UseAuthorization();
app.MapControllers();

app.MapHub<ValidationHub>("/validationHub")
   .RequireCors("SignalRPolicy");

using (var scope = app.Services.CreateScope())
{
    var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    var queueManager = scope.ServiceProvider.GetRequiredService<IDynamicQueueManager>();
    try
    {
        context.Database.EnsureCreated();
        app.Logger.LogInformation("Database initialized successfully");
        
        await queueManager.InitializeExchanges();
        app.Logger.LogInformation("RabbitMQ exchanges initialized");
        
        var activeScanners = await context.Scanners.Where(s => s.IsActive).ToListAsync();
        foreach (var scanner in activeScanners)
        {
            await queueManager.SetupQueuesForScanner(scanner.Id);
        }
        app.Logger.LogInformation("Setup queues for {Count} active scanners", activeScanners.Count);
    }
    catch (Exception ex)
    {
        app.Logger.LogError(ex, "Error during initialization");
    }
}

app.Logger.LogInformation("UCL Event Scanner API starting...");
app.Run();