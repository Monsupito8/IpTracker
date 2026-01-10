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
    var port = uri.Port > 0 ? uri.Port : 5432;
    
    connectionString = $"Host={uri.Host};Port={port};Database={db};Username={user};Password={passwd};SSL Mode=Require;Trust Server Certificate=true";
    
    builder.Services.AddDbContext<ApplicationDbContext>(options =>
        options.UseNpgsql(connectionString));
}
else
{
    // Для локальной разработки используем SQLite
    builder.Services.AddDbContext<ApplicationDbContext>(options =>
        options.UseSqlite("Data Source=iptracker.db"));
}

// Добавляем поддержку Razor Pages (если они есть)
builder.Services.AddRazorPages();

// Настраиваем порт
var port = Environment.GetEnvironmentVariable("PORT") ?? "8080";
builder.WebHost.UseUrls($"http://0.0.0.0:{port}");

var app = builder.Build();

// Создаем базу данных при старте (если её нет)
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
    try
    {
        db.Database.EnsureCreated();
        Console.WriteLine("✅ База данных создана/подключена");
    }
    catch (Exception ex)
    {
        Console.WriteLine($"❌ Ошибка базы данных: {ex.Message}");
    }
}

// Настройка pipeline
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error");
    app.UseHsts();
}

app.UseHttpsRedirection();
app.UseStaticFiles();
app.UseRouting();
app.UseAuthorization();

// Маршруты для API
app.MapControllers();

// Маршруты для страниц (если есть)
app.MapRazorPages();

// Главная страница (простая заглушка)
app.MapGet("/", () => Results.Content(@"
<!DOCTYPE html>
<html>
<head>
    <title>IP Tracker API</title>
    <style>
        body { font-family: Arial, sans-serif; max-width: 800px; margin: 40px auto; padding: 20px; }
        h1 { color: #333; }
        code { background: #f4f4f4; padding: 2px 5px; border-radius: 3px; }
    </style>
</head>
<body>
    <h1>🚀 IP Tracker API работает!</h1>
    <p>Доступные эндпоинты:</p>
    <ul>
        <li><code>POST /api/tracker/generate</code> - Создать ссылку</li>
        <li><code>GET /track/{id}</code> - Перейти по ссылке</li>
        <li><code>GET /api/tracker/stats/{id}</code> - Статистика</li>
    </ul>
</body>
</html>
", "text/html"));

app.Run();