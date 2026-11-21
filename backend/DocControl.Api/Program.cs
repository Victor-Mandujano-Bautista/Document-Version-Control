using DocControl.Api.Data;
using DocControl.Api.Services;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Services.AddControllers()
    .AddJsonOptions(options =>
    {
        // Serializar enums como strings en las respuestas JSON
        options.JsonSerializerOptions.Converters.Add(
            new System.Text.Json.Serialization.JsonStringEnumConverter());
    });

builder.Services.AddOpenApi();

// Conectar con SQL Server
builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseSqlServer(builder.Configuration.GetConnectionString("DefaultConnection")));

// Servicios en segundo plano y servicios de negocio
builder.Services.AddHostedService<LockExpirationWorker>();
builder.Services.AddHostedService<RecycleBinCleanupWorker>();
builder.Services.AddHostedService<ScheduledBackupWorker>();
builder.Services.AddScoped<NotificationService>();
builder.Services.AddScoped<FolderPermissionService>();
builder.Services.AddScoped<ContentIndexingService>();
builder.Services.AddScoped<BackupService>();

var app = builder.Build();

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.UseAuthorization();

app.MapControllers();

// Endpoint de verificación de conexión (existente)
app.MapGet("/api/conexion", () => Results.Ok(new
{
    Mensaje = "¡Hola desde el Backend de .NET!",
    Proyecto = "Control de Versiones Documental",
    Fecha = DateTime.Now.ToString("dd/MM/yyyy HH:mm:ss")
}));

app.Run();
