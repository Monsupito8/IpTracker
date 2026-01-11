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