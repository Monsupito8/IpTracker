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

                _logger.LogInformation($"Создана ссылка: {linkId}");

                var baseUrl = $"{Request.Scheme}://{Request.Host}";

                return Ok(new
                {
                    success = true,
                    trackingUrl = $"{baseUrl}/track/{linkId}",
                    adminUrl = $"{baseUrl}/admin/{linkId}",
                    targetUrl = trackingLink.TargetUrl,  // ← ДОБАВЬ ЭТУ СТРОКУ!
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
                    return NotFound(new { success = false, message = "Ссылка не найдена" });
                }

                var clientIp = await GetRealIpAsync();
                var userAgent = Request.Headers["User-Agent"].ToString();
                var referer = Request.Headers["Referer"].ToString();

                _logger.LogInformation($"Переход по ссылке {id} с IP: {clientIp}");

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

                return Redirect(link.TargetUrl ?? "https://google.com");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Ошибка при обработке перехода по ссылке {id}");
                return Redirect("https://google.com");
            }
        }

        // Просмотр статистики в JSON формате
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
                        targetUrl = link.TargetUrl,  // ← УЖЕ ЕСТЬ
                        trackingUrl = $"{Request.Scheme}://{Request.Host}/track/{link.Id}"  // ← ДОБАВЬ
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

        // Получение информации о конкретном посещении (для админки)
        [HttpGet("visit/{id}")]
        public async Task<IActionResult> GetVisit(int id)
        {
            try
            {
                var visit = await _context.LinkVisits
                    .Include(v => v.Link)
                    .FirstOrDefaultAsync(v => v.Id == id);

                if (visit == null)
                {
                    return NotFound(new { success = false, message = "Посещение не найдено" });
                }

                return Ok(new
                {
                    success = true,
                    id = visit.Id,
                    visitorIp = visit.VisitorIp,
                    userAgent = visit.UserAgent,
                    browserName = GetBrowserName(visit.UserAgent),
                    osName = GetOSName(visit.UserAgent),
                    deviceType = GetDeviceType(visit.UserAgent),
                    referer = visit.Referer,
                    visitedAt = visit.VisitedAt,
                    linkId = visit.LinkId,
                    linkNote = visit.Link?.Note
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Ошибка при получении посещения {id}");
                return StatusCode(500, new { success = false, message = "Ошибка сервера" });
            }
        }

        // Удаление посещения
        [HttpDelete("visit/{id}")]
        public async Task<IActionResult> DeleteVisit(int id)
        {
            try
            {
                var visit = await _context.LinkVisits.FindAsync(id);
                if (visit == null)
                {
                    return NotFound(new { success = false, message = "Посещение не найдено" });
                }

                _context.LinkVisits.Remove(visit);
                await _context.SaveChangesAsync();

                _logger.LogInformation($"Удалено посещение ID: {id}");

                return Ok(new
                {
                    success = true,
                    message = "Посещение удалено"
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Ошибка при удалении посещения {id}");
                return StatusCode(500, new { success = false, message = "Ошибка при удалении" });
            }
        }

        // Получение всех ссылок
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

        // Получение всех посещений
        [HttpGet("visits")]
        public async Task<IActionResult> GetAllVisits([FromQuery] int limit = 500)
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

        // Получение информации о IP
        [HttpGet("ipinfo/{ip}")]
        public async Task<IActionResult> GetIpInfo(string ip)
        {
            try
            {
                if (string.IsNullOrEmpty(ip) || ip == "Unknown" || ip == "::1" || ip == "127.0.0.1")
                {
                    return Ok(new
                    {
                        success = true,
                        ip = ip,
                        type = "Локальный IP",
                        message = "Это локальный IP адрес (ваш компьютер или сервер)"
                    });
                }

                using var httpClient = new HttpClient();
                httpClient.Timeout = TimeSpan.FromSeconds(3);

                try
                {
                    var response = await httpClient.GetAsync($"http://ipwho.is/{ip}");
                    if (response.IsSuccessStatusCode)
                    {
                        var content = await response.Content.ReadAsStringAsync();
                        var json = System.Text.Json.JsonDocument.Parse(content);

                        return Ok(new
                        {
                            success = true,
                            ip = ip,
                            country = json.RootElement.GetProperty("country").GetString(),
                            region = json.RootElement.GetProperty("region").GetString(),
                            city = json.RootElement.GetProperty("city").GetString(),
                            isp = json.RootElement.GetProperty("connection").GetProperty("isp").GetString(),
                            org = json.RootElement.GetProperty("connection").GetProperty("org").GetString(),
                            latitude = json.RootElement.GetProperty("latitude").GetDouble(),
                            longitude = json.RootElement.GetProperty("longitude").GetDouble(),
                            timezone = json.RootElement.GetProperty("timezone").GetProperty("id").GetString(),
                            source = "ipwho.is"
                        });
                    }
                }
                catch { }

                return Ok(new
                {
                    success = true,
                    ip = ip,
                    message = "Информация об IP ограничена",
                    type = GetIpType(ip)
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Ошибка при получении информации об IP {ip}");
                return Ok(new
                {
                    success = true,
                    ip = ip,
                    message = "Ошибка при получении информации"
                });
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
            if (userAgent.Contains("Chrome")) return "Chrome";
            if (userAgent.Contains("Firefox")) return "Firefox";
            if (userAgent.Contains("Safari") && !userAgent.Contains("Chrome")) return "Safari";
            if (userAgent.Contains("Edge")) return "Edge";
            if (userAgent.Contains("Opera")) return "Opera";
            return "Other";
        }

        private string GetOSName(string userAgent)
        {
            if (userAgent.Contains("Windows")) return "Windows";
            if (userAgent.Contains("Mac OS")) return "macOS";
            if (userAgent.Contains("Linux")) return "Linux";
            if (userAgent.Contains("Android")) return "Android";
            if (userAgent.Contains("iOS") || userAgent.Contains("iPhone")) return "iOS";
            return "Unknown OS";
        }

        private string GetDeviceType(string userAgent)
        {
            if (userAgent.Contains("Mobile")) return "Mobile";
            if (userAgent.Contains("Tablet")) return "Tablet";
            return "Desktop";
        }
    }

    // Модель для запроса генерации ссылки
    public class GenerateRequest
    {
        public string? Note { get; set; }
        public string? TargetUrl { get; set; }
    }
}