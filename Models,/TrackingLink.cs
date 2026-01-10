using System;
using System.Collections.Generic;

namespace IpTracker.Models
{
    public class TrackingLink
    {
        public string Id { get; set; } = string.Empty;
        public DateTime CreatedAt { get; set; }
        public string CreatorIp { get; set; } = string.Empty;
        public string? Note { get; set; }
        public string TargetUrl { get; set; } = string.Empty;
        
        public List<LinkVisit> Visits { get; set; } = new();
    }
}