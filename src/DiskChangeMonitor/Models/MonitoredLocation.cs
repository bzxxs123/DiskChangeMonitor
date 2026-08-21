using System;

namespace DiskChangeMonitor.Models
{
    /// <summary>A user-monitored disk root or folder whose WizTree exports are tracked.</summary>
    public sealed record MonitoredLocation(string Id, string RootPath, string DisplayName)
    {
        public static MonitoredLocation FromPath(string rootPath)
        {
            var normalized = NormalizeRoot(rootPath);
            return new MonitoredLocation(normalized, normalized, normalized);
        }

        public static string NormalizeRoot(string rootPath)
        {
            var trimmed = (rootPath ?? string.Empty).Trim().TrimEnd('\\', '/');
            if (trimmed.Length == 0)
            {
                throw new ArgumentException("监控根目录不能为空。", nameof(rootPath));
            }

            if (trimmed.Length == 2 && trimmed[1] == ':')
            {
                return trimmed.ToUpperInvariant() + "\\";
            }

            return trimmed + "\\";
        }
    }
}
