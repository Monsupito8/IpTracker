using Microsoft.EntityFrameworkCore;
using IpTracker.Data;

var builder = WebApplication.CreateBuilder(args);

// 1. Добавляем поддержку контроллеров
builder.Services.AddControllers();

// 2. ДОБАВЬ ЭТУ СТРОКУ для Razor Pages
builder.Services.AddRazorPages();

// 3. Настройка базы данных
builder.Services.AddDbContext<ApplicationDbContext>(options =>
    options.UseSqlite("Data Source=iptracker.db"));

// 4. Настраиваем порт
var appPort = Environment.GetEnvironmentVariable("PORT") ?? "8080";
builder.WebHost.UseUrls($"http://0.0.0.0:{appPort}");

var app = builder.Build();

// 5. Создаем базу данных
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
    db.Database.EnsureCreated();
}

// 6. ДОБАВЬ ЭТУ СТРОКУ для Razor Pages
app.MapRazorPages();

// 7. API маршруты
app.MapControllers();

// 8. Главная страница (если нет Index.cshtml)
app.MapGet("/", () => @"<!DOCTYPE html>
<html>
<head>
    <meta http-equiv='refresh' content='0; url=/Index'>
    <title>IP Tracker</title>
</head>
<body>
    <p>Перенаправление на главную страницу...</p>
</body>
</html>");

app.Run();