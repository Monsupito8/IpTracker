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
        
        // Geolocation fields
        public double? Latitude { get; set; }
        public double? Longitude { get; set; }
        public double? Accuracy { get; set; }
        
        public TrackingLink? Link { get; set; }
    }
}