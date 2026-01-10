using System;
using System.Collections.Generic;

namespace IpTracker.Models
{
    public class TrackingLink
    {
        public string Id { get; set; }
        public DateTime CreatedAt { get; set; }
        public string CreatorIp { get; set; }
        public string Note { get; set; }
        public string TargetUrl { get; set; }
        
        public List<LinkVisit> Visits { get; set; } = new();
    }
    
    public class LinkVisit
    {
        public int Id { get; set; }
        public string LinkId { get; set; }
        public string VisitorIp { get; set; }
        public string UserAgent { get; set; }
        public string Referer { get; set; }
        public DateTime VisitedAt { get; set; }
        
        public TrackingLink Link { get; set; }
    }
}