using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers();
builder.Services.AddRazorPages();

var dbPath = "/data/iptracker.db";
builder.Services.AddDbContext<ApplicationDbContext>(options =>
    options.UseSqlite($"Data Source={dbPath}"));

var appPort = Environment.GetEnvironmentVariable("PORT") ?? "8080";
builder.WebHost.UseUrls($"http://0.0.0.0:{appPort}");

var app = builder.Build();

using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
    db.Database.EnsureCreated();
    Console.WriteLine("Database ready");
}

app.UseStaticFiles();
app.UseRouting();

app.MapRazorPages();

app.MapGet("/", () => Results.Redirect("/admin"));

app.MapGet("/test", () => Results.Content("App is working!", "text/plain"));

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
    public double? Latitude { get; set; }
    public double? Longitude { get; set; }
    public double? Accuracy { get; set; }
    public TrackingLink? Link { get; set; }
}

public class ApplicationDbContext : DbContext
{
    public ApplicationDbContext(DbContextOptions<ApplicationDbContext> options) : base(options) { }
    public DbSet<TrackingLink> TrackingLinks { get; set; }
    public DbSet<LinkVisit> LinkVisits { get; set; }
}