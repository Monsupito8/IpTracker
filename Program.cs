using Microsoft.EntityFrameworkCore;
using IpTracker.Data;
using IpTracker.Models; // ← ДОБАВЬТЕ ЭТУ СТРОКУ!

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
    
    // Выводим количество записей для отладки
    var linksCount = db.TrackingLinks.Count();
    var visitsCount = db.LinkVisits.Count();
    Console.WriteLine($"📊 В базе: {linksCount} ссылок, {visitsCount} посещений");
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

// 10. Важно: Маршрут для трекинга должен быть перед главной страницей
app.MapGet("/track/{id}", async (string id, ApplicationDbContext db, HttpContext context) =>
{
    try
    {
        var link = await db.TrackingLinks.FindAsync(id);
        if (link == null)
        {
            return Results.NotFound("Ссылка не найдена");
        }

        // Сохраняем посещение
        var clientIp = context.Connection.RemoteIpAddress?.ToString() ?? "Unknown";
        var userAgent = context.Request.Headers["User-Agent"].ToString();
        var referer = context.Request.Headers["Referer"].ToString();

        var visit = new LinkVisit
        {
            LinkId = id,
            VisitorIp = clientIp,
            UserAgent = userAgent,
            Referer = string.IsNullOrEmpty(referer) ? null : referer,
            VisitedAt = DateTime.UtcNow
        };

        db.LinkVisits.Add(visit);
        await db.SaveChangesAsync();

        Console.WriteLine($"🔗 Переход по ссылке {id} от IP: {clientIp}");

        // Перенаправляем
        return Results.Redirect(link.TargetUrl);
    }
    catch (Exception ex)
    {
        Console.WriteLine($"❌ Ошибка при обработке перехода: {ex.Message}");
        return Results.Redirect("https://google.com");
    }
});

// 11. Главная страница с перенаправлением
app.MapGet("/", () => Results.Content(@"
<!DOCTYPE html>
<html>
<head>
    <meta http-equiv='refresh' content='0; url=/admin'>
    <title>IP Tracker</title>
    <style>
        body { 
            font-family: Arial, sans-serif; 
            display: flex; 
            justify-content: center; 
            align-items: center; 
            height: 100vh; 
            margin: 0; 
            background: linear-gradient(135deg, #667eea 0%, #764ba2 100%);
            color: white;
        }
        .loading {
            text-align: center;
        }
        .spinner {
            border: 8px solid rgba(255,255,255,0.3);
            border-radius: 50%;
            border-top: 8px solid white;
            width: 60px;
            height: 60px;
            animation: spin 1s linear infinite;
            margin: 0 auto 20px;
        }
        @@keyframes spin {
            0% { transform: rotate(0deg); }
            100% { transform: rotate(360deg); }
        }
    </style>
</head>
<body>
    <div class='loading'>
        <div class='spinner'></div>
        <h2>Перенаправление в админ-панель...</h2>
        <p>IP Tracker запущен</p>
    </div>
</body>
</html>", "text/html"));

// 12. Тестовая страница для проверки
app.MapGet("/test", async (ApplicationDbContext db) =>
{
    var links = await db.TrackingLinks.ToListAsync();
    var visits = await db.LinkVisits.ToListAsync();
    
    return Results.Content($@"
        <h1>Тест базы данных</h1>
        <p>Ссылок: {links.Count}</p>
        <p>Посещений: {visits.Count}</p>
        <h3>Последние 5 посещений:</h3>
        <ul>
            {string.Join("", visits.Take(5).Select(v => 
                $"<li>ID: {v.Id}, Link: {v.LinkId}, IP: {v.VisitorIp}, Time: {v.VisitedAt}</li>"))}
        </ul>
        <a href='/admin'>Админка</a>
    ", "text/html");
});

app.Run();