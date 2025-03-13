// Importación del namespace de SharedModels
using SharedModels;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
// Learn more about configuring Swagger/OpenAPI at https://aka.ms/aspnetcore/swashbuckle
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

// Configurar la ruta del archivo JSON de datos
string jsonFilePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Data", "reservations.json");
Console.WriteLine($"Usando archivo de datos: {jsonFilePath}");

// Registrar el servicio de datos JSON como singleton
var jsonDataService = new JsonDataService(jsonFilePath);
builder.Services.AddSingleton<JsonDataService>(jsonDataService);

var app = builder.Build();

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseHttpsRedirection();

// Endpoint para listar todas las reservas - GET /api/reservations
app.MapGet("/api/reservations", async (JsonDataService dataService) =>
{
    var reservations = await dataService.GetAllReservationsAsync();
    return Results.Ok(reservations);
})
.WithName("GetReservations")
.WithOpenApi();

// Endpoint para obtener una reserva específica - GET /api/reservations/{id}
app.MapGet("/api/reservations/{id}", async (long id, JsonDataService dataService) =>
{
    var reservation = await dataService.GetReservationByIdAsync(id);
    if (reservation == null)
        return Results.NotFound();
    
    return Results.Ok(reservation);
})
.WithName("GetReservation")
.WithOpenApi();

// Endpoint para crear una nueva reserva - POST /api/reservations
app.MapPost("/api/reservations", async (Reservation reservation, JsonDataService dataService) =>
{
    var createdReservation = await dataService.CreateReservationAsync(reservation);
    return Results.Created($"/api/reservations/{createdReservation.Id}", createdReservation);
})
.WithName("CreateReservation")
.WithOpenApi();

// Endpoint para actualizar una reserva - PUT /api/reservations/{id}
app.MapPut("/api/reservations/{id}", async (long id, Reservation updatedReservation, JsonDataService dataService) =>
{
    var result = await dataService.UpdateReservationAsync(id, updatedReservation);
    if (result == null)
        return Results.NotFound();
    
    return Results.Ok(result);
})
.WithName("UpdateReservation")
.WithOpenApi();

// Endpoint para eliminar una reserva - DELETE /api/reservations/{id}
app.MapDelete("/api/reservations/{id}", async (long id, JsonDataService dataService) =>
{
    var success = await dataService.DeleteReservationAsync(id);
    if (!success)
        return Results.NotFound();
    
    return Results.NoContent();
})
.WithName("DeleteReservation")
.WithOpenApi();

var summaries = new[]
{
    "Freezing", "Bracing", "Chilly", "Cool", "Mild", "Warm", "Balmy", "Hot", "Sweltering", "Scorching"
};

app.MapGet("/weatherforecast", () =>
{
    var forecast =  Enumerable.Range(1, 5).Select(index =>
        new WeatherForecastDto
        (
            DateOnly.FromDateTime(DateTime.Now.AddDays(index)),
            Random.Shared.Next(-20, 55),
            summaries[Random.Shared.Next(summaries.Length)]
        ))
        .ToArray();
    return forecast;
})
.WithName("GetWeatherForecast")
.WithOpenApi();

app.Run();

// Clase de datos para el pronóstico del tiempo
record WeatherForecastDto(DateOnly Date, int TemperatureC, string? Summary)
{
    public int TemperatureF => 32 + (int)(TemperatureC / 0.5556);
}
