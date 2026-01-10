using Microsoft.EntityFrameworkCore;
using IpTracker.Data;

var builder = WebApplication.CreateBuilder(args);

// 1. Добавляем контроллеры (для API)
builder.Services.AddControllers();

// 2. Добавляем Razor Pages (для админки)
builder.Services.AddRazorPages();

// 3. Настройка базы данных
builder.Services.AddDbContext<ApplicationDbContext>(options =>
    options.UseSqlite("Data Source=iptracker.db"));

// 4. Настраиваем порт для Railway
var appPort = Environment.GetEnvironmentVariable("PORT") ?? "8080";
builder.WebHost.UseUrls($"http://0.0.0.0:{appPort}");

var app = builder.Build();

// 5. Создаем базу данных при старте
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
    db.Database.EnsureCreated();
    Console.WriteLine("✅ База данных подключена");
}

// 6. Для продакшена используем HTTPS и обработку ошибок
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error");
    app.UseHsts();
}

// 7. Middleware
app.UseHttpsRedirection();
app.UseStaticFiles();
app.UseRouting();
app.UseAuthorization();

// 8. Маршруты для API
app.MapControllers();

// 9. Маршруты для страниц (админка)
app.MapRazorPages();

// 10. Главная страница с перенаправлением
app.MapGet("/", () => Results.Content(@"
<!DOCTYPE html>
<html>
<head>
    <meta http-equiv='refresh' content='0; url=/admin'>
    <title>IP Tracker</title>
</head>
<body>
    <p>Перенаправление в админ-панель...</p>
</body>
</html>", "text/html"));

app.Run();