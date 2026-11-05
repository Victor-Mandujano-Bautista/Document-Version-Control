using DocControl.Api.Services;

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

// Registrar el servicio de workspace documental como Singleton (en memoria)
builder.Services.AddSingleton<DocumentWorkspaceService>();

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
