using System;

namespace IpTracker.Models
{
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
}