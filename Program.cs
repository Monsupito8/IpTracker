using Microsoft.EntityFrameworkCore;
using IpTracker.Data;

var builder = WebApplication.CreateBuilder(args);

// Добавляем поддержку контроллеров
builder.Services.AddControllers();

// ТОЛЬКО SQLite - убрал весь PostgreSQL код
builder.Services.AddDbContext<ApplicationDbContext>(options =>
    options.UseSqlite("Data Source=iptracker.db"));

// Настраиваем порт
var appPort = Environment.GetEnvironmentVariable("PORT") ?? "8080";
builder.WebHost.UseUrls($"http://0.0.0.0:{appPort}");

var app = builder.Build();

// Создаем базу данных
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
    db.Database.EnsureCreated();
}

app.MapControllers();

// Главная страница
app.MapGet("/", () => @"<!DOCTYPE html>
<html>
<head><title>IP Tracker</title></head>
<body>
<h1>✅ IP Tracker работает!</h1>
<p>API доступно по адресу: /api/tracker/generate</p>
</body>
</html>");

app.Run();
