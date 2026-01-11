using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;

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
if (app.Environment.IsDevelopment())
{
    app.UseDeveloperExceptionPage();
}
else
{
    // Убедись что есть этот маршрут
    app.UseExceptionHandler("/Home/Error");
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

// Обработка формы создания ссылки
app.MapPost("/api/tracker/generate", async (HttpContext context, ApplicationDbContext db) =>
{
    var form = await context.Request.ReadFormAsync();
    var targetUrl = form["TargetUrl"].ToString();
    var note = form["Note"].ToString();

    if (string.IsNullOrEmpty(targetUrl))
    {
        context.Response.Redirect("/admin?error=Введите+URL");
        return;
    }

    try
    {
        if (!targetUrl.StartsWith("http://") && !targetUrl.StartsWith("https://"))
        {
            targetUrl = "https://" + targetUrl;
        }

        var linkId = Guid.NewGuid().ToString("N").Substring(0, 8);

        var trackingLink = new TrackingLink
        {
            Id = linkId,
            CreatedAt = DateTime.UtcNow,
            CreatorIp = context.Connection.RemoteIpAddress?.ToString() ?? "Unknown",
            Note = note?.Trim(),
            TargetUrl = targetUrl.Trim()
        };

        db.TrackingLinks.Add(trackingLink);
        await db.SaveChangesAsync();

        var baseUrl = $"{context.Request.Scheme}://{context.Request.Host}";
        var trackingUrl = $"{baseUrl}/track/{linkId}";

        context.Response.Redirect($"/admin?message=Ссылка+создана&newLink={trackingUrl}&targetUrl={Uri.EscapeDataString(targetUrl)}");
    }
    catch (Exception ex)
    {
        Console.WriteLine($"Ошибка создания ссылки: {ex.Message}");
        context.Response.Redirect("/admin?error=Ошибка+создания+ссылки");
    }
});

// Удаление ссылки
app.MapGet("/api/tracker/delete/{id}", async (string id, ApplicationDbContext db, HttpContext context) =>
{
    try
    {
        var link = await db.TrackingLinks
            .Include(l => l.Visits)
            .FirstOrDefaultAsync(l => l.Id == id);

        if (link == null)
        {
            context.Response.Redirect("/admin?error=Ссылка+не+найдена");
            return;
        }

        int visitsCount = link.Visits.Count;

        db.LinkVisits.RemoveRange(link.Visits);
        db.TrackingLinks.Remove(link);
        await db.SaveChangesAsync();

        context.Response.Redirect($"/admin?message=Ссылка+удалена.+Удалено+{visitsCount}+посещений");
    }
    catch (Exception ex)
    {
        Console.WriteLine($"Ошибка удаления ссылки: {ex.Message}");
        context.Response.Redirect("/admin?error=Ошибка+удаления");
    }
});

// Маршрут для трекинга - добавляем перед app.Run()
app.MapGet("/track/{id}", async (string id, ApplicationDbContext db, HttpContext context) =>
{
    try
    {
        Console.WriteLine($"🔗 Попытка перехода по ссылке: {id}");

        var link = await db.TrackingLinks.FindAsync(id);
        if (link == null)
        {
            Console.WriteLine($"❌ Ссылка не найдена: {id}");
            return Results.Redirect("https://google.com");
        }

        // Получаем IP
        var ip = context.Connection.RemoteIpAddress?.ToString();
        if (ip == "::1") ip = "127.0.0.1";

        // Проверяем заголовки для реального IP
        var forwardedFor = context.Request.Headers["X-Forwarded-For"].FirstOrDefault();
        if (!string.IsNullOrEmpty(forwardedFor))
        {
            ip = forwardedFor.Split(',')[0].Trim();
        }

        var userAgent = context.Request.Headers["User-Agent"].ToString();
        var referer = context.Request.Headers["Referer"].ToString();

        Console.WriteLine($"📝 Данные посещения: IP={ip}, UserAgent={userAgent}");

        // Сохраняем посещение
        var visit = new LinkVisit
        {
            LinkId = id,
            VisitorIp = ip ?? "Unknown",
            UserAgent = userAgent,
            Referer = string.IsNullOrEmpty(referer) ? null : referer,
            VisitedAt = DateTime.UtcNow
        };

        db.LinkVisits.Add(visit);
        await db.SaveChangesAsync();

        Console.WriteLine($"✅ Посещение сохранено для ссылки {id}, ID посещения: {visit.Id}");

        // Перенаправляем
        return Results.Redirect(link.TargetUrl);
    }
    catch (Exception ex)
    {
        Console.WriteLine($"❌ Ошибка при трекинге: {ex.Message}");
        Console.WriteLine($"❌ StackTrace: {ex.StackTrace}");
        return Results.Redirect("https://google.com");
    }
});

// Страница для отладки трекинга
app.MapGet("/debug/track", async (ApplicationDbContext db, HttpContext context) =>
{
    var ip = context.Connection.RemoteIpAddress?.ToString();
    var userAgent = context.Request.Headers["User-Agent"].ToString();
    var headers = string.Join("<br>", context.Request.Headers.Select(h => $"{h.Key}: {h.Value}"));

    return Results.Content($@"
        <h1>Отладка трекинга</h1>
        <p><strong>Ваш IP:</strong> {ip}</p>
        <p><strong>User-Agent:</strong> {userAgent}</p>
        <h3>Все заголовки:</h3>
        <pre>{headers}</pre>
        <h3>Тестовые ссылки:</h3>
        <ul>
            <li><a href='/track/test123'>/track/test123</a> (несуществующая)</li>
            <li><a href='/debug/createtest'>Создать тестовую ссылку</a></li>
        </ul>
        <a href='/admin'>Админка</a>
    ", "text/html");
});

// Создание тестовой ссылки
app.MapGet("/debug/createtest", async (ApplicationDbContext db) =>
{
    var linkId = "test_" + Guid.NewGuid().ToString("N").Substring(0, 6);

    var link = new TrackingLink
    {
        Id = linkId,
        CreatedAt = DateTime.UtcNow,
        CreatorIp = "debug",
        Note = "Тестовая ссылка",
        TargetUrl = "https://google.com"
    };

    db.TrackingLinks.Add(link);
    await db.SaveChangesAsync();

    return Results.Content($@"
        <h1>Тестовая ссылка создана</h1>
        <p>ID: <strong>{linkId}</strong></p>
        <p>Ссылка для тестирования: <a href='/track/{linkId}'>/track/{linkId}</a></p>
        <p>Она перенаправляет на: https://google.com</p>
        <a href='/debug/track'>Вернуться к отладке</a>
    ", "text/html");
});

// Обработка формы создания ссылки
app.MapPost("/api/tracker/generate", async (HttpContext context, ApplicationDbContext db) =>
{
    var form = await context.Request.ReadFormAsync();
    var targetUrl = form["TargetUrl"].ToString();
    var note = form["Note"].ToString();

    if (string.IsNullOrEmpty(targetUrl))
    {
        context.Response.Redirect("/admin?error=Введите+URL");
        return;
    }

    try
    {
        if (!targetUrl.StartsWith("http://") && !targetUrl.StartsWith("https://"))
        {
            targetUrl = "https://" + targetUrl;
        }

        var linkId = Guid.NewGuid().ToString("N").Substring(0, 8);

        var trackingLink = new TrackingLink
        {
            Id = linkId,
            CreatedAt = DateTime.UtcNow,
            CreatorIp = context.Connection.RemoteIpAddress?.ToString() ?? "Unknown",
            Note = note?.Trim(),
            TargetUrl = targetUrl.Trim()
        };

        db.TrackingLinks.Add(trackingLink);
        await db.SaveChangesAsync();

        var baseUrl = $"{context.Request.Scheme}://{context.Request.Host}";
        var trackingUrl = $"{baseUrl}/track/{linkId}";

        // Генерируем красивый домен
        var prettyDomains = new[] { "go.link", "click.pro", "redirect.me", "url.short", "lnk.to" };
        var random = new Random();
        var prettyDomain = prettyDomains[random.Next(prettyDomains.Length)];
        var prettyLink = $"https://{prettyDomain}/{linkId}";

        context.Response.Redirect($"/admin?message=Ссылка+создана&newLink={Uri.EscapeDataString(trackingUrl)}&targetUrl={Uri.EscapeDataString(targetUrl)}&linkId={linkId}");
    }
    catch (Exception ex)
    {
        Console.WriteLine($"Ошибка создания ссылки: {ex.Message}");
        context.Response.Redirect("/admin?error=Ошибка+создания+ссылки");
    }
});

// Удаление ссылки
app.MapGet("/api/tracker/delete/{id}", async (string id, ApplicationDbContext db, HttpContext context) =>
{
    try
    {
        var link = await db.TrackingLinks
            .Include(l => l.Visits)
            .FirstOrDefaultAsync(l => l.Id == id);

        if (link == null)
        {
            context.Response.Redirect("/admin?error=Ссылка+не+найдена");
            return;
        }

        int visitsCount = link.Visits.Count;

        db.LinkVisits.RemoveRange(link.Visits);
        db.TrackingLinks.Remove(link);
        await db.SaveChangesAsync();

        context.Response.Redirect($"/admin?message=Ссылка+удалена.+Удалено+{visitsCount}+посещений");
    }
    catch (Exception ex)
    {
        Console.WriteLine($"Ошибка удаления ссылки: {ex.Message}");
        context.Response.Redirect("/admin?error=Ошибка+удаления");
    }
});

// Маршрут для страницы ошибки
app.MapGet("/error", () => Results.Content(@"
<!DOCTYPE html>
<html>
<head>
    <title>Ошибка - IP Tracker</title>
    <style>
        body {
            font-family: Arial, sans-serif;
            background: #f8f9fa;
            color: #333;
            text-align: center;
            padding: 50px;
        }
        .error-box {
            background: white;
            padding: 40px;
            border-radius: 10px;
            box-shadow: 0 0 20px rgba(0,0,0,0.1);
            max-width: 600px;
            margin: 0 auto;
        }
        h1 {
            color: #dc3545;
        }
        .btn {
            display: inline-block;
            padding: 10px 20px;
            background: #007bff;
            color: white;
            text-decoration: none;
            border-radius: 5px;
            margin: 10px;
        }
    </style>
</head>
<body>
    <div class='error-box'>
        <h1>⚠️ Что-то пошло не так</h1>
        <p>Произошла ошибка при обработке вашего запроса.</p>
        <p>Попробуйте вернуться на главную страницу.</p>
        <a href='/' class='btn'>На главную</a>
        <a href='/admin' class='btn' style='background:#28a745;'>В админку</a>
    </div>
</body>
</html>", "text/html"));

// Или лучше перенаправление на главную при ошибках
app.MapGet("/Home/Error", () => Results.Redirect("/error"));

// Страница Privacy
app.MapGet("/Privacy", () => Results.Content(@"
<!DOCTYPE html>
<html>
<head>
    <title>Политика конфиденциальности - IP Tracker</title>
    <style>
        body {
            font-family: Arial, sans-serif;
            padding: 20px;
            max-width: 800px;
            margin: 0 auto;
        }
        h1 {
            color: #007bff;
        }
        .back {
            margin-bottom: 20px;
        }
    </style>
</head>
<body>
    <div class='back'>
        <a href='/' style='color:#007bff; text-decoration:none;'>← Назад</a>
    </div>
    <h1>Политика конфиденциальности</h1>
    <p>IP Tracker собирает только необходимую информацию для работы сервиса.</p>
    <p>Все данные защищены и не передаются третьим лицам.</p>
</body>
</html>", "text/html"));

// Главная страница (Home)
app.MapGet("/Home", () => Results.Redirect("/"));
app.MapGet("/Home/Index", () => Results.Redirect("/"));

app.Run();

// ========== МОДЕЛИ И КЛАССЫ В ОДНОМ ФАЙЛЕ ==========

public class TrackingLink
{
    public string Id { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
    public string CreatorIp { get; set; } = string.Empty;
    public string? Note { get; set; }
    public string TargetUrl { get; set; } = string.Empty;

    public List<LinkVisit> Visits { get; set; } = new();
}

public class LinkVisit
{
    public int Id { get; set; }
    public string LinkId { get; set; } = string.Empty;
    public string VisitorIp { get; set; } = string.Empty;
    public string UserAgent { get; set; } = string.Empty;
    public string? Referer { get; set; }
    public DateTime VisitedAt { get; set; }

    public TrackingLink? Link { get; set; }
}

public class ApplicationDbContext : DbContext
{
    public ApplicationDbContext(DbContextOptions<ApplicationDbContext> options)
        : base(options)
    {
    }

    public DbSet<TrackingLink> TrackingLinks { get; set; }
    public DbSet<LinkVisit> LinkVisits { get; set; }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.Entity<TrackingLink>()
            .HasKey(t => t.Id);

        modelBuilder.Entity<LinkVisit>()
            .HasKey(v => v.Id);

        modelBuilder.Entity<LinkVisit>()
            .HasOne(v => v.Link)
            .WithMany(l => l.Visits)
            .HasForeignKey(v => v.LinkId);
    }
}

// Добавьте этот статический класс в Program.cs после всех других классов
public static class StringExtensions
{
    public static string Truncate(this string value, int maxLength)
    {
        if (string.IsNullOrEmpty(value)) return value;
        return value.Length <= maxLength ? value : value.Substring(0, maxLength) + "...";
    }
}