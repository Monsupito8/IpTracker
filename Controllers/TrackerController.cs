using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using IpTracker.Data;
using IpTracker.Models;
using System;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;

namespace IpTracker.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class TrackerController : ControllerBase
    {
        private readonly ApplicationDbContext _context;
        private readonly ILogger<TrackerController> _logger;
        private static string? _realPublicIp; // Кэшируем публичный IP

        public TrackerController(ApplicationDbContext context, ILogger<TrackerController> logger)
        {
            _context = context;
            _logger = logger;
        }

        // Генерация новой отслеживаемой ссылки
        [HttpPost("generate")]
        public async Task<IActionResult> GenerateLink([FromBody] GenerateRequest request)
        {
            try
            {
                // Валидация URL
                if (string.IsNullOrEmpty(request?.TargetUrl))
                {
                    return BadRequest(new { success = false, message = "URL не может быть пустым" });
                }

                // Добавляем https:// если нет протокола
                if (!request.TargetUrl.StartsWith("http://") && !request.TargetUrl.StartsWith("https://"))
                {
                    request.TargetUrl = "https://" + request.TargetUrl;
                }

                // Создаем уникальный ID для ссылки
                var linkId = Guid.NewGuid().ToString("N").Substring(0, 8);

                var trackingLink = new TrackingLink
                {
                    Id = linkId,
                    CreatedAt = DateTime.UtcNow,
                    CreatorIp = await GetRealIpAsync(true), // Получаем реальный IP создателя
                    Note = request.Note?.Trim(),
                    TargetUrl = request.TargetUrl.Trim()
                };

                _context.TrackingLinks.Add(trackingLink);
                await _context.SaveChangesAsync();

                _logger.LogInformation($"Создана новая ссылка: {linkId}");

                // Формируем URL для отслеживания
                var baseUrl = $"{Request.Scheme}://{Request.Host}";

                return Ok(new
                {
                    success = true,
                    trackingUrl = $"{baseUrl}/track/{linkId}",
                    adminUrl = $"{baseUrl}/admin/{linkId}",
                    linkId = linkId,
                    createdAt = trackingLink.CreatedAt,
                    message = "Ссылка успешно создана"
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Ошибка при создании ссылки");
                return StatusCode(500, new { success = false, message = "Внутренняя ошибка сервера" });
            }
        }

        // Обработка перехода по ссылке
        [HttpGet("track/{id}")]
        public async Task<IActionResult> Track(string id)
        {
            try
            {
                var link = await _context.TrackingLinks.FindAsync(id);
                if (link == null)
                {
                    return NotFound(new { 
                        success = false, 
                        message = "Ссылка не найдена"
                    });
                }

                // Получаем РЕАЛЬНЫЙ IP посетителя
                var clientIp = await GetRealIpAsync();
                var userAgent = Request.Headers["User-Agent"].ToString();
                var referer = Request.Headers["Referer"].ToString();

                _logger.LogInformation($"Переход по ссылке {id} с IP: {clientIp}");

                // Сохраняем информацию о посещении
                var visit = new LinkVisit
                {
                    LinkId = id,
                    VisitorIp = clientIp,
                    UserAgent = userAgent,
                    Referer = string.IsNullOrEmpty(referer) ? null : referer,
                    VisitedAt = DateTime.UtcNow
                };

                _context.LinkVisits.Add(visit);
                await _context.SaveChangesAsync();

                // Перенаправляем на целевую страницу
                return Redirect(link.TargetUrl ?? "https://google.com");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Ошибка при обработке перехода по ссылке {id}");
                return Redirect("https://google.com");
            }
        }

        // Просмотр статистики в JSON формате
        [HttpGet("stats/{id}")]
        public async Task<IActionResult> GetStats(string id)
        {
            try
            {
                var link = await _context.TrackingLinks
                    .Include(l => l.Visits)
                    .FirstOrDefaultAsync(l => l.Id == id);

                if (link == null)
                {
                    return NotFound(new { success = false, message = "Ссылка не найдена" });
                }

                // Группируем по IP для уникальных посещений
                var uniqueVisitors = link.Visits
                    .GroupBy(v => v.VisitorIp)
                    .Select(g => new
                    {
                        ip = g.Key,
                        visits = g.Count(),
                        firstVisit = g.Min(v => v.VisitedAt),
                        lastVisit = g.Max(v => v.VisitedAt)
                    })
                    .ToList();

                return Ok(new
                {
                    success = true,
                    link = new
                    {
                        id = link.Id,
                        createdAt = link.CreatedAt,
                        creatorIp = link.CreatorIp,
                        note = link.Note,
                        targetUrl = link.TargetUrl
                    },
                    statistics = new
                    {
                        totalVisits = link.Visits.Count,
                        uniqueVisitors = uniqueVisitors.Count,
                        visitsToday = link.Visits.Count(v => v.VisitedAt.Date == DateTime.UtcNow.Date),
                        lastVisit = link.Visits.Max(v => (DateTime?)v.VisitedAt)
                    },
                    visits = link.Visits
                        .OrderByDescending(v => v.VisitedAt)
                        .Select(v => new
                        {
                            id = v.Id,
                            visitorIp = v.VisitorIp,
                            userAgent = v.UserAgent,
                            referer = v.Referer,
                            visitedAt = v.VisitedAt,
                            ipType = GetIpType(v.VisitorIp)
                        })
                        .ToList()
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Ошибка при получении статистики для ссылки {id}");
                return StatusCode(500, new { success = false, message = "Ошибка при получении статистики" });
            }
        }

        // Получение РЕАЛЬНОГО IP адреса
        private async Task<string> GetRealIpAsync(bool forceUpdate = false)
        {
            try
            {
                // 1. Пробуем получить IP из заголовков (для прокси/балансировщиков)
                var ip = Request.Headers["X-Forwarded-For"].FirstOrDefault();

                if (!string.IsNullOrEmpty(ip))
                {
                    // Может быть несколько IP через запятую, берем первый
                    var ips = ip.Split(',');
                    return ips[0].Trim();
                }

                // 2. Другие возможные заголовки
                ip = Request.Headers["X-Real-IP"].FirstOrDefault();
                if (!string.IsNullOrEmpty(ip)) return ip;

                // 3. Стандартный способ
                ip = HttpContext.Connection.RemoteIpAddress?.ToString();

                // 4. Обработка IPv6 localhost
                if (ip == "::1") ip = "127.0.0.1";

                // 5. Если это localhost - пытаемся определить публичный IP
                if (ip == "127.0.0.1" || string.IsNullOrEmpty(ip))
                {
                    // Кэшируем публичный IP чтобы не делать запрос каждый раз
                    if (_realPublicIp == null || forceUpdate)
                    {
                        try
                        {
                            using var httpClient = new HttpClient();
                            httpClient.Timeout = TimeSpan.FromSeconds(3);

                            // Пробуем несколько сервисов
                            var services = new[]
                            {
                                "https://api.ipify.org",
                                "https://icanhazip.com",
                                "https://checkip.amazonaws.com"
                            };

                            foreach (var service in services)
                            {
                                try
                                {
                                    var publicIp = await httpClient.GetStringAsync(service);
                                    if (!string.IsNullOrEmpty(publicIp))
                                    {
                                        _realPublicIp = publicIp.Trim();
                                        _logger.LogInformation($"Определен публичный IP: {_realPublicIp}");
                                        break;
                                    }
                                }
                                catch { }
                            }

                            if (string.IsNullOrEmpty(_realPublicIp))
                            {
                                _realPublicIp = "Не удалось определить";
                            }
                        }
                        catch (Exception ex)
                        {
                            _logger.LogError(ex, "Ошибка при получении публичного IP");
                            _realPublicIp = "Ошибка определения";
                        }
                    }

                    // Если это localhost, но есть публичный IP - показываем его
                    if (ip == "127.0.0.1" && _realPublicIp != "Не удалось определить" && _realPublicIp != "Ошибка определения")
                    {
                        return _realPublicIp + " (ваш публичный IP)";
                    }
                }

                // 6. Очищаем от порта если есть
                if (ip != null && ip.Contains(":"))
                {
                    ip = ip.Split(':')[0];
                }

                return ip ?? "Unknown";
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Ошибка в GetRealIpAsync");
                return "Unknown";
            }
        }

        // Определение типа IP
        private string GetIpType(string ip)
        {
            if (string.IsNullOrEmpty(ip)) return "Неизвестно";

            if (ip == "127.0.0.1" || ip.Contains("ваш публичный IP"))
                return "Локальный/Ваш";

            if (ip.StartsWith("192.168.") || ip.StartsWith("10.") || 
                (ip.StartsWith("172.") && int.TryParse(ip.Split('.')[1], out var second) && second >= 16 && second <= 31))
                return "Локальная сеть";

            if (ip.Contains(":"))
                return "IPv6";

            return "Публичный IP";
        }

        // Удаление ссылки и всех данных
        [HttpDelete("delete/{id}")]
        public async Task<IActionResult> DeleteLink(string id)
        {
            try
            {
                var link = await _context.TrackingLinks
                    .Include(l => l.Visits)
                    .FirstOrDefaultAsync(l => l.Id == id);

                if (link == null)
                {
                    return NotFound(new { success = false, message = "Ссылка не найдена" });
                }

                int visitsCount = link.Visits.Count;

                // Удаляем все посещения
                _context.LinkVisits.RemoveRange(link.Visits);
                // Удаляем саму ссылку
                _context.TrackingLinks.Remove(link);

                await _context.SaveChangesAsync();

                _logger.LogInformation($"Удалена ссылка {id} с {visitsCount} посещениями");

                return Ok(new 
                { 
                    success = true, 
                    message = $"Ссылка удалена. Удалено {visitsCount} посещений.",
                    deletedVisits = visitsCount
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Ошибка при удалении ссылки {id}");
                return StatusCode(500, new { success = false, message = "Ошибка при удалении" });
            }
        }

        // Экспорт данных в CSV
        [HttpGet("export/{id}/csv")]
        public async Task<IActionResult> ExportToCsv(string id)
        {
            try
            {
                var visits = await _context.LinkVisits
                    .Where(v => v.LinkId == id)
                    .OrderByDescending(v => v.VisitedAt)
                    .ToListAsync();

                if (visits.Count == 0)
                {
                    return NotFound(new { success = false, message = "Нет данных для экспорта" });
                }

                // Создаем CSV
                var csv = "IP Address;Время (UTC);Браузер;Откуда пришел\n";

                foreach (var visit in visits)
                {
                    var safeUserAgent = (visit.UserAgent ?? "")
                        .Replace("\"", "'")
                        .Replace(";", ",");

                    var safeReferer = (visit.Referer ?? "")
                        .Replace("\"", "'")
                        .Replace(";", ",");

                    csv += $"\"{visit.VisitorIp}\";"
                         + $"\"{visit.VisitedAt:yyyy-MM-dd HH:mm:ss}\";"
                         + $"\"{safeUserAgent}\";"
                         + $"\"{safeReferer}\"\n";
                }

                var bytes = System.Text.Encoding.UTF8.GetBytes(csv);

                return File(bytes,
                    "text/csv",
                    $"visits_{id}_{DateTime.UtcNow:yyyyMMdd}.csv");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Ошибка при экспорте CSV для ссылки {id}");
                return StatusCode(500, new { success = false, message = "Ошибка при экспорте" });
            }
        }
    }

    // Модель для запроса генерации ссылки
    public class GenerateRequest
    {
        public string? Note { get; set; }
        public string? TargetUrl { get; set; }
    }
}