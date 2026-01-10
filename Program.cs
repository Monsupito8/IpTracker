using Microsoft.EntityFrameworkCore;
using IpTracker.Data;

var builder = WebApplication.CreateBuilder(args);

// Добавляем поддержку контроллеров
builder.Services.AddControllers();

// Настройка базы данных
var connectionString = builder.Configuration.GetConnectionString("DefaultConnection");

// Если на Railway есть PostgreSQL, используем его
var railwayDbUrl = Environment.GetEnvironmentVariable("DATABASE_URL");
if (!string.IsNullOrEmpty(railwayDbUrl))
{
    // Конвертируем URL из Railway в строку подключения Npgsql
    var uri = new Uri(railwayDbUrl);
    var db = uri.AbsolutePath.Trim('/');
    var user = uri.UserInfo.Split(':')[0];
    var passwd = uri.UserInfo.Split(':')[1];
    var dbPort = uri.Port > 0 ? uri.Port : 5432; // ИЗМЕНИЛ НА dbPort
    
    connectionString = $"Host={uri.Host};Port={dbPort};Database={db};Username={user};Password={passwd};SSL Mode=Require;Trust Server Certificate=true";
    
    builder.Services.AddDbContext<ApplicationDbContext>(options =>
        options.UseNpgsql(connectionString));
}
else
{
    // Для локальной разработки используем SQLite
    builder.Services.AddDbContext<ApplicationDbContext>(options =>
        options.UseSqlite("Data Source=iptracker.db"));
}

// Настраиваем порт
var appPort = Environment.GetEnvironmentVariable("PORT") ?? "8080"; // ИЗМЕНИЛ НА appPort
builder.WebHost.UseUrls($"http://0.0.0.0:{appPort}");

var app = builder.Build();

// Создаем базу данных
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
    db.Database.EnsureCreated();
}

app.MapControllers();
app.MapGet("/", () => "IP Tracker работает!");
app.Run();
