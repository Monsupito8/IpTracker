using Microsoft.EntityFrameworkCore;
using IpTracker.Data;

var builder = WebApplication.CreateBuilder(args);

// 1. ТОЛЬКО API
builder.Services.AddControllers();

// 2. База данных
builder.Services.AddDbContext<ApplicationDbContext>(options =>
    options.UseSqlite("Data Source=iptracker.db"));

// 3. Порт
var appPort = Environment.GetEnvironmentVariable("PORT") ?? "8080";
builder.WebHost.UseUrls($"http://0.0.0.0:{appPort}");

var app = builder.Build();

// 4. База
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
    db.Database.EnsureCreated();
}

// 5. API
app.MapControllers();

// 6. ПРОСТАЯ HTML страница (без Razor)
app.MapGet("/", () => Results.Content(@"
<!DOCTYPE html>
<html lang='ru'>
<head>
    <meta charset='UTF-8'>
    <meta name='viewport' content='width=device-width, initial-scale=1.0'>
    <title>🔗 IP Tracker</title>
    <style>
        * { margin: 0; padding: 0; box-sizing: border-box; }
        body { 
            font-family: 'Segoe UI', Arial, sans-serif; 
            background: linear-gradient(135deg, #667eea 0%, #764ba2 100%);
            color: white;
            min-height: 100vh;
            display: flex;
            justify-content: center;
            align-items: center;
            padding: 20px;
        }
        .container {
            max-width: 800px;
            width: 100%;
            background: rgba(255,255,255,0.95);
            border-radius: 20px;
            padding: 40px;
            box-shadow: 0 20px 60px rgba(0,0,0,0.3);
            color: #333;
        }
        h1 {
            color: #4f46e5;
            margin-bottom: 20px;
            text-align: center;
            font-size: 2.5rem;
        }
        .card {
            background: #f8fafc;
            border-radius: 15px;
            padding: 30px;
            margin: 20px 0;
            border: 2px solid #e2e8f0;
        }
        .api-endpoint {
            background: white;
            padding: 15px;
            border-radius: 10px;
            margin: 10px 0;
            border-left: 4px solid #4f46e5;
        }
        code {
            background: #e0e7ff;
            color: #4f46e5;
            padding: 2px 6px;
            border-radius: 4px;
            font-family: monospace;
        }
        .btn {
            display: inline-block;
            padding: 12px 30px;
            background: linear-gradient(135deg, #4f46e5 0%, #7c3aed 100%);
            color: white;
            text-decoration: none;
            border-radius: 50px;
            font-weight: 600;
            margin: 10px;
            border: none;
            cursor: pointer;
        }
        .btn:hover {
            transform: scale(1.05);
            box-shadow: 0 10px 20px rgba(79, 70, 229, 0.3);
        }
        .form-group {
            margin: 15px 0;
        }
        input {
            width: 100%;
            padding: 12px;
            border: 2px solid #e2e8f0;
            border-radius: 10px;
            font-size: 1rem;
        }
        #result {
            margin-top: 20px;
            padding: 20px;
            border-radius: 10px;
            background: #d1fae5;
            display: none;
        }
    </style>
</head>
<body>
    <div class='container'>
        <h1>🔗 IP Tracker</h1>
        <p style='text-align: center; color: #64748b; margin-bottom: 30px;'>
            Создавайте отслеживаемые ссылки и получайте статистику переходов
        </p>
        
        <div class='card'>
            <h2 style='color: #4f46e5; margin-bottom: 20px;'>➕ Создать новую ссылку</h2>
            <div class='form-group'>
                <label style='display: block; margin-bottom: 5px; font-weight: 600;'>Целевой URL:</label>
                <input type='url' id='targetUrl' placeholder='https://example.com'>
            </div>
            <div class='form-group'>
                <label style='display: block; margin-bottom: 5px; font-weight: 600;'>Примечание (необязательно):</label>
                <input type='text' id='note' placeholder='Описание ссылки'>
            </div>
            <button class='btn' onclick='createLink()'>🚀 Создать ссылку</button>
            <div id='result'></div>
        </div>
        
        <div class='card'>
            <h2 style='color: #4f46e5; margin-bottom: 20px;'>📚 API Эндпоинты</h2>
            <div class='api-endpoint'>
                <strong>POST</strong> <code>/api/tracker/generate</code>
                <p style='margin-top: 5px; color: #64748b;'>Создать отслеживаемую ссылку</p>
            </div>
            <div class='api-endpoint'>
                <strong>GET</strong> <code>/track/&#123;id&#125;</code>
                <p style='margin-top: 5px; color: #64748b;'>Перейти по отслеживаемой ссылке</p>
            </div>
            <div class='api-endpoint'>
                <strong>GET</strong> <code>/api/tracker/stats/&#123;id&#125;</code>
                <p style='margin-top: 5px; color: #64748b;'>Получить статистику в JSON</p>
            </div>
        </div>
        
        <div style='text-align: center; margin-top: 30px; color: #94a3b8;'>
            <p>IP Tracker API работает на Railway</p>
        </div>
    </div>
    
    <script>
        async function createLink() {
            const url = document.getElementById('targetUrl').value;
            const note = document.getElementById('note').value;
            
            if (!url) {
                alert('Введите URL');
                return;
            }
            
            const response = await fetch('/api/tracker/generate', {
                method: 'POST',
                headers: { 'Content-Type': 'application/json' },
                body: JSON.stringify({ TargetUrl: url, Note: note })
            });
            
            const result = await response.json();
            const resultDiv = document.getElementById('result');
            
            if (result.success) {
                resultDiv.innerHTML = `
                    <h3 style='color: #059669;'>✅ Ссылка создана!</h3>
                    <p><strong>Отслеживаемая ссылка:</strong></p>
                    <input type='text' value='${window.location.origin}/track/${result.linkId}' 
                           style='width: 100%; padding: 10px; margin: 10px 0; border: 2px solid #10b981; border-radius: 5px;' 
                           readonly>
                    <p><strong>Статистика:</strong> <code>${window.location.origin}/api/tracker/stats/${result.linkId}</code></p>
                    <button onclick='copyToClipboard(\`${window.location.origin}/track/${result.linkId}\`)' class='btn'>
                        📋 Копировать ссылку
                    </button>
                `;
                resultDiv.style.display = 'block';
            } else {
                resultDiv.innerHTML = `<p style='color: #dc2626;'>❌ Ошибка: ${result.message}</p>`;
                resultDiv.style.display = 'block';
            }
        }
        
        function copyToClipboard(text) {
            navigator.clipboard.writeText(text);
            alert('Ссылка скопирована в буфер обмена!');
        }
    </script>
</body>
</html>
", "text/html"));

app.Run();