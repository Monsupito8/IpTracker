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
        private static string? _realPublicIp;

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
                if (string.IsNullOrEmpty(request?.TargetUrl))
                {
                    return BadRequest(new { success = false, message = "URL не может быть пустым" });
                }

                if (!request.TargetUrl.StartsWith("http://") && !request.TargetUrl.StartsWith("https://"))
                {
                    request.TargetUrl = "https://" + request.TargetUrl;
                }

                var linkId = Guid.NewGuid().ToString("N").Substring(0, 8);

                var trackingLink = new TrackingLink
                {
                    Id = linkId,
                    CreatedAt = DateTime.UtcNow,
                    CreatorIp = await GetRealIpAsync(true),
                    Note = request.Note?.Trim(),
                    TargetUrl = request.TargetUrl.Trim()
                };

                _context.TrackingLinks.Add(trackingLink);
                await _context.SaveChangesAsync();

                _logger.LogInformation($"Создана ссылка: {linkId} для URL: {request.TargetUrl}");

                var baseUrl = $"{Request.Scheme}://{Request.Host}";

                return Ok(new
                {
                    success = true,
                    trackingUrl = $"{baseUrl}/track/{linkId}",
                    adminUrl = $"{baseUrl}/admin/{linkId}",
                    targetUrl = trackingLink.TargetUrl,
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

        // Обработка перехода по ссылке - ИСПРАВЛЕННАЯ ВЕРСИЯ
        [HttpGet("track/{id}")]
        [Route("track/{id}")]  // Добавляем явный маршрут
        public async Task<IActionResult> Track(string id)
        {
            try
            {
                _logger.LogInformation($"Начало обработки перехода по ссылке: {id}");
                
                var link = await _context.TrackingLinks.FindAsync(id);
                if (link == null)
                {
                    _logger.LogWarning($"Ссылка не найдена: {id}");
                    return NotFound("Ссылка не найдена");
                }

                // Получаем данные о посещении
                var clientIp = await GetRealIpAsync();
                var userAgent = Request.Headers["User-Agent"].ToString();
                var referer = Request.Headers["Referer"].ToString();
                var visitedAt = DateTime.UtcNow;

                _logger.LogInformation($"Переход по ссылке {id} с IP: {clientIp}, User-Agent: {userAgent}");

                // Сохраняем посещение в базу
                var visit = new LinkVisit
                {
                    LinkId = id,
                    VisitorIp = clientIp,
                    UserAgent = userAgent,
                    Referer = string.IsNullOrEmpty(referer) ? null : referer,
                    VisitedAt = visitedAt
                };

                _context.LinkVisits.Add(visit);
                await _context.SaveChangesAsync();

                _logger.LogInformation($"Сохранено посещение для ссылки {id}. ID посещения: {visit.Id}");

                // Перенаправляем пользователя
                return Redirect(link.TargetUrl ?? "https://google.com");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"ОШИБКА при обработке перехода по ссылке {id}");
                return Redirect("https://google.com");
            }
        }

        // ОБНОВЛЕННЫЙ метод GetStats - теперь показывает правильные данные
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

                // Логируем для отладки
                _logger.LogInformation($"Статистика для ссылки {id}: {link.Visits.Count} посещений");

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
                        targetUrl = link.TargetUrl,
                        trackingUrl = $"{Request.Scheme}://{Request.Host}/track/{link.Id}"
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
                            browser = GetBrowserName(v.UserAgent),
                            os = GetOSName(v.UserAgent),
                            device = GetDeviceType(v.UserAgent),
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

        // Получение всех посещений (исправленная версия)
        [HttpGet("visits")]
        public async Task<IActionResult> GetAllVisits([FromQuery] int limit = 50)
        {
            try
            {
                var visits = await _context.LinkVisits
                    .Include(v => v.Link)
                    .OrderByDescending(v => v.VisitedAt)
                    .Take(limit)
                    .Select(v => new
                    {
                        id = v.Id,
                        linkId = v.LinkId,
                        visitorIp = v.VisitorIp,
                        userAgent = v.UserAgent,
                        browser = GetBrowserName(v.UserAgent),
                        os = GetOSName(v.UserAgent),
                        device = GetDeviceType(v.UserAgent),
                        referer = v.Referer,
                        visitedAt = v.VisitedAt,
                        linkNote = v.Link != null ? v.Link.Note : null
                    })
                    .ToListAsync();

                // Логируем для отладки
                _logger.LogInformation($"Получено {visits.Count} посещений из базы данных");

                return Ok(new
                {
                    success = true,
                    visits = visits,
                    total = visits.Count
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Ошибка при получении списка посещений");
                return StatusCode(500, new { success = false, message = "Ошибка сервера" });
            }
        }

        // Получение всех ссылок (исправленная версия)
        [HttpGet("links")]
        public async Task<IActionResult> GetAllLinks()
        {
            try
            {
                var links = await _context.TrackingLinks
                    .Include(l => l.Visits)
                    .OrderByDescending(l => l.CreatedAt)
                    .Select(l => new
                    {
                        id = l.Id,
                        createdAt = l.CreatedAt,
                        creatorIp = l.CreatorIp,
                        note = l.Note,
                        targetUrl = l.TargetUrl,
                        visitsCount = l.Visits.Count,
                        uniqueVisitors = l.Visits.GroupBy(v => v.VisitorIp).Count(),
                        lastVisit = l.Visits.Max(v => (DateTime?)v.VisitedAt)
                    })
                    .ToListAsync();

                // Логируем для отладки
                foreach (var link in links)
                {
                    _logger.LogInformation($"Ссылка {link.id}: {link.visitsCount} посещений");
                }

                return Ok(new
                {
                    success = true,
                    links = links,
                    total = links.Count
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Ошибка при получении списка ссылок");
                return StatusCode(500, new { success = false, message = "Ошибка сервера" });
            }
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

                _context.LinkVisits.RemoveRange(link.Visits);
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

        // Тестовый метод для проверки базы данных
        [HttpGet("test")]
        public async Task<IActionResult> Test()
        {
            try
            {
                var linksCount = await _context.TrackingLinks.CountAsync();
                var visitsCount = await _context.LinkVisits.CountAsync();
                
                var lastVisits = await _context.LinkVisits
                    .OrderByDescending(v => v.Id)
                    .Take(5)
                    .ToListAsync();

                return Ok(new
                {
                    success = true,
                    database = "Работает",
                    linksCount = linksCount,
                    visitsCount = visitsCount,
                    lastVisits = lastVisits.Select(v => new
                    {
                        id = v.Id,
                        linkId = v.LinkId,
                        ip = v.VisitorIp,
                        time = v.VisitedAt
                    })
                });
            }
            catch (Exception ex)
            {
                return Ok(new
                {
                    success = false,
                    error = ex.Message,
                    innerError = ex.InnerException?.Message
                });
            }
        }

        // ========== ВСПОМОГАТЕЛЬНЫЕ МЕТОДЫ ==========

        private async Task<string> GetRealIpAsync(bool forceUpdate = false)
        {
            try
            {
                var ip = Request.Headers["X-Forwarded-For"].FirstOrDefault();

                if (!string.IsNullOrEmpty(ip))
                {
                    var ips = ip.Split(',');
                    return ips[0].Trim();
                }

                ip = Request.Headers["X-Real-IP"].FirstOrDefault();
                if (!string.IsNullOrEmpty(ip)) return ip;

                ip = HttpContext.Connection.RemoteIpAddress?.ToString();

                if (ip == "::1") ip = "127.0.0.1";

                if (ip == "127.0.0.1" || string.IsNullOrEmpty(ip))
                {
                    if (_realPublicIp == null || forceUpdate)
                    {
                        try
                        {
                            using var httpClient = new HttpClient();
                            httpClient.Timeout = TimeSpan.FromSeconds(3);

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

                    if (ip == "127.0.0.1" && _realPublicIp != "Не удалось определить" && _realPublicIp != "Ошибка определения")
                    {
                        return _realPublicIp + " (ваш публичный IP)";
                    }
                }

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

        private string GetBrowserName(string userAgent)
        {
            if (string.IsNullOrEmpty(userAgent)) return "Неизвестно";
            if (userAgent.Contains("Chrome")) return "Chrome";
            if (userAgent.Contains("Firefox")) return "Firefox";
            if (userAgent.Contains("Safari") && !userAgent.Contains("Chrome")) return "Safari";
            if (userAgent.Contains("Edge")) return "Edge";
            if (userAgent.Contains("Opera")) return "Opera";
            return "Other";
        }

        private string GetOSName(string userAgent)
        {
            if (string.IsNullOrEmpty(userAgent)) return "Unknown OS";
            if (userAgent.Contains("Windows")) return "Windows";
            if (userAgent.Contains("Mac OS")) return "macOS";
            if (userAgent.Contains("Linux")) return "Linux";
            if (userAgent.Contains("Android")) return "Android";
            if (userAgent.Contains("iOS") || userAgent.Contains("iPhone")) return "iOS";
            return "Unknown OS";
        }

        private string GetDeviceType(string userAgent)
        {
            if (string.IsNullOrEmpty(userAgent)) return "Неизвестно";
            if (userAgent.Contains("Mobile")) return "Мобильный";
            if (userAgent.Contains("Tablet")) return "Планшет";
            return "Компьютер";
        }
    }

    // Модель для запроса генерации ссылки
    public class GenerateRequest
    {
        public string? Note { get; set; }
        public string? TargetUrl { get; set; }
    }
}