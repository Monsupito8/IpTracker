using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers();
builder.Services.AddRazorPages();

var dbPath = Environment.GetEnvironmentVariable("DATABASE_PATH") ?? "/data/iptracker.db";
Console.WriteLine($"Database path: {dbPath}");

builder.Services.AddDbContext<ApplicationDbContext>(options =>
    options.UseSqlite($"Data Source={dbPath}"));

var appPort = Environment.GetEnvironmentVariable("PORT") ?? "8080";
builder.WebHost.UseUrls($"http://0.0.0.0:{appPort}");

var app = builder.Build();

using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
    
    try
    {
        db.Database.EnsureCreated();
        Console.WriteLine("Database connected successfully");

        var linksCount = db.TrackingLinks.Count();
        var visitsCount = db.LinkVisits.Count();
        Console.WriteLine($"Database stats: {linksCount} links, {visitsCount} visits");
    }
    catch (Exception ex)
    {
        Console.WriteLine($"Database error: {ex.Message}");
    }
}

if (app.Environment.IsDevelopment())
{
    app.UseDeveloperExceptionPage();
}
else
{
    app.UseExceptionHandler("/error");
    app.UseHsts();
}

app.UseHttpsRedirection();
app.UseStaticFiles();
app.UseRouting();
app.UseAuthorization();

app.MapControllers();
app.MapRazorPages();

app.MapGet("/", () => Results.Content(@"
<!DOCTYPE html>
<html>
<head>
    <meta charset='utf-8'>
    <meta http-equiv='refresh' content='0; url=/admin'>
    <title>IP Tracker</title>
</head>
<body>
    <p>Redirecting to admin panel...</p>
</body>
</html>", "text/html; charset=utf-8"));

app.MapPost("/api/tracker/generate", async (HttpContext context, ApplicationDbContext db) =>
{
    try
    {
        var form = await context.Request.ReadFormAsync();
        var targetUrl = form["TargetUrl"].ToString();
        var note = form["Note"].ToString();

        Console.WriteLine($"Creating link: URL={targetUrl}, Note={note}");

        if (string.IsNullOrEmpty(targetUrl))
        {
            Console.WriteLine("Empty URL provided");
            context.Response.Redirect("/admin?error=Please+enter+URL");
            return;
        }

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

        Console.WriteLine($"Link created: ID={linkId}");

        var baseUrl = $"{context.Request.Scheme}://{context.Request.Host}";
        var trackingUrl = $"{baseUrl}/track/{linkId}";

        context.Response.Redirect($"/admin?message=Link+created&newLink={Uri.EscapeDataString(trackingUrl)}&targetUrl={Uri.EscapeDataString(targetUrl)}&linkId={linkId}");
    }
    catch (Exception ex)
    {
        Console.WriteLine($"Error creating link: {ex.Message}");
        Console.WriteLine($"StackTrace: {ex.StackTrace}");
        context.Response.Redirect("/admin?error=Error+creating+link");
    }
});

app.MapGet("/api/tracker/delete/{id}", async (string id, ApplicationDbContext db, HttpContext context) =>
{
    try
    {
        var link = await db.TrackingLinks
            .Include(l => l.Visits)
            .FirstOrDefaultAsync(l => l.Id == id);

        if (link == null)
        {
            context.Response.Redirect("/admin?error=Link+not+found");
            return;
        }

        int visitsCount = link.Visits.Count;

        db.LinkVisits.RemoveRange(link.Visits);
        db.TrackingLinks.Remove(link);
        await db.SaveChangesAsync();

        Console.WriteLine($"Link deleted: {id}, visits: {visitsCount}");

        context.Response.Redirect($"/admin?message=Link+deleted+with+{visitsCount}+visits");
    }
    catch (Exception ex)
    {
        Console.WriteLine($"Error deleting link: {ex.Message}");
        context.Response.Redirect("/admin?error=Error+deleting+link");
    }
});

app.MapGet("/track/{id}", async (string id, ApplicationDbContext db, HttpContext context) =>
{
    try
    {
        Console.WriteLine($"Track request for link: {id}");

        var link = await db.TrackingLinks.FindAsync(id);
        if (link == null)
        {
            Console.WriteLine($"Link not found: {id}");
            return Results.Redirect("https://google.com");
        }

        var ip = context.Connection.RemoteIpAddress?.ToString();
        
        var forwardedFor = context.Request.Headers["X-Forwarded-For"].FirstOrDefault();
        var realIp = context.Request.Headers["X-Real-IP"].FirstOrDefault();
        
        if (!string.IsNullOrEmpty(forwardedFor))
        {
            ip = forwardedFor.Split(',')[0].Trim();
        }
        else if (!string.IsNullOrEmpty(realIp))
        {
            ip = realIp.Trim();
        }
        
        if (ip == "::1" || ip == "127.0.0.1")
        {
            ip = "127.0.0.1 (localhost)";
        }

        var userAgent = context.Request.Headers["User-Agent"].ToString();
        var referer = context.Request.Headers["Referer"].ToString();

        Console.WriteLine($"Visit data: IP={ip}, UserAgent={userAgent}");

        var visit = new LinkVisit
        {
            LinkId = id,
            VisitorIp = ip ?? "Unknown",
            UserAgent = userAgent,
            Referer = string.IsNullOrEmpty(referer) ? null : referer,
            VisitedAt = DateTime.UtcNow
        };

        db.LinkVisits.Add(visit);
        int saved = await db.SaveChangesAsync();

        Console.WriteLine($"Visit saved for link {id}, Visit ID: {visit.Id}, Records saved: {saved}");
        
        var checkVisit = await db.LinkVisits.FindAsync(visit.Id);
        Console.WriteLine($"Verification: visit {(checkVisit != null ? "found" : "NOT FOUND")} in DB");

        return Results.Redirect(link.TargetUrl);
    }
    catch (Exception ex)
    {
        Console.WriteLine($"Error tracking: {ex.Message}");
        Console.WriteLine($"StackTrace: {ex.StackTrace}");
        return Results.Redirect("https://google.com");
    }
});

app.MapGet("/debug/track", async (ApplicationDbContext db, HttpContext context) =>
{
    var ip = context.Connection.RemoteIpAddress?.ToString();
    var forwardedFor = context.Request.Headers["X-Forwarded-For"].FirstOrDefault();
    var realIp = context.Request.Headers["X-Real-IP"].FirstOrDefault();
    var userAgent = context.Request.Headers["User-Agent"].ToString();
    var headers = string.Join("<br>", context.Request.Headers.Select(h => $"{h.Key}: {h.Value}"));
    
    var linksCount = await db.TrackingLinks.CountAsync();
    var visitsCount = await db.LinkVisits.CountAsync();

    return Results.Content($@"
<!DOCTYPE html>
<html>
<head>
    <meta charset='utf-8'>
    <title>Debug - IP Tracker</title>
    <style>
        body {{ font-family: Arial, sans-serif; padding: 20px; }}
        h1 {{ color: #333; }}
        h3 {{ color: #666; margin-top: 20px; }}
        p {{ margin: 5px 0; }}
        pre {{ background: #f5f5f5; padding: 10px; border-radius: 5px; }}
        a {{ color: #4285f4; text-decoration: none; }}
        a:hover {{ text-decoration: underline; }}
    </style>
</head>
<body>
    <h1>Tracking Debug</h1>
    <h3>IP Addresses:</h3>
    <p><strong>RemoteIpAddress:</strong> {ip}</p>
    <p><strong>X-Forwarded-For:</strong> {forwardedFor ?? "not set"}</p>
    <p><strong>X-Real-IP:</strong> {realIp ?? "not set"}</p>
    <p><strong>User-Agent:</strong> {userAgent}</p>
    
    <h3>Database:</h3>
    <p><strong>Links:</strong> {linksCount}</p>
    <p><strong>Visits:</strong> {visitsCount}</p>
    
    <h3>All Headers:</h3>
    <pre>{headers}</pre>
    
    <h3>Test Links:</h3>
    <ul>
        <li><a href='/track/test123'>/track/test123</a> (non-existent)</li>
        <li><a href='/debug/createtest'>Create test link</a></li>
    </ul>
    <a href='/admin'>Admin Panel</a>
</body>
</html>", "text/html; charset=utf-8");
});

app.MapGet("/debug/createtest", async (ApplicationDbContext db, HttpContext context) =>
{
    var linkId = "test_" + Guid.NewGuid().ToString("N").Substring(0, 6);

    var link = new TrackingLink
    {
        Id = linkId,
        CreatedAt = DateTime.UtcNow,
        CreatorIp = "debug",
        Note = "Test link",
        TargetUrl = "https://google.com"
    };

    db.TrackingLinks.Add(link);
    await db.SaveChangesAsync();
    
    var baseUrl = $"{context.Request.Scheme}://{context.Request.Host}";

    return Results.Content($@"
<!DOCTYPE html>
<html>
<head>
    <meta charset='utf-8'>
    <title>Test Link Created</title>
    <style>
        body {{ font-family: Arial, sans-serif; padding: 20px; }}
        h1 {{ color: #333; }}
        a {{ color: #4285f4; text-decoration: none; }}
        a:hover {{ text-decoration: underline; }}
    </style>
</head>
<body>
    <h1>Test Link Created</h1>
    <p>ID: <strong>{linkId}</strong></p>
    <p>Test link: <a href='/track/{linkId}'>{baseUrl}/track/{linkId}</a></p>
    <p>Redirects to: https://google.com</p>
    <a href='/debug/track'>Back to debug</a>
</body>
</html>", "text/html; charset=utf-8");
});

app.MapGet("/error", () => Results.Content(@"
<!DOCTYPE html>
<html>
<head>
    <meta charset='utf-8'>
    <title>Error - IP Tracker</title>
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
        <h1>Something went wrong</h1>
        <p>An error occurred while processing your request.</p>
        <p>Try returning to the home page.</p>
        <a href='/' class='btn'>Home</a>
        <a href='/admin' class='btn' style='background:#28a745;'>Admin</a>
    </div>
</body>
</html>", "text/html; charset=utf-8"));

app.MapGet("/Home/Error", () => Results.Redirect("/error"));

app.MapGet("/Privacy", () => Results.Content(@"
<!DOCTYPE html>
<html>
<head>
    <meta charset='utf-8'>
    <title>Privacy Policy - IP Tracker</title>
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
        <a href='/' style='color:#007bff; text-decoration:none;'>Back</a>
    </div>
    <h1>Privacy Policy</h1>
    <p>IP Tracker collects only necessary information for service operation.</p>
    <p>All data is protected and not shared with third parties.</p>
</body>
</html>", "text/html; charset=utf-8"));

app.MapGet("/Home", () => Results.Redirect("/"));
app.MapGet("/Home/Index", () => Results.Redirect("/"));

app.Run();

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

public static class StringExtensions
{
    public static string Truncate(this string value, int maxLength)
    {
        if (string.IsNullOrEmpty(value)) return value;
        return value.Length <= maxLength ? value : value.Substring(0, maxLength) + "...";
    }
}