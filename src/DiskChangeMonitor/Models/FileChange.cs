using System;
using System.Globalization;

namespace DiskChangeMonitor.Models
{
    /// <summary>One item-level change between the latest two completed snapshots.</summary>
    public sealed record FileChange(
        ChangeKind Kind,
        string Path,
        string? OldPath,
        string? NewPath,
        long OldSize,
        long NewSize,
        long OldAllocated,
        long NewAllocated,
        bool IsDirectory)
    {
        public string KindText =>
            Kind switch
            {
                ChangeKind.New => "新增",
                ChangeKind.Deleted => "删除",
                ChangeKind.Enlarged => "变大",
                ChangeKind.Reduced => "变小",
                ChangeKind.Moved => "移动",
                _ => Kind.ToString()
            };

        public static string FormatBytes(long value)
        {
            if (value == 0)
            {
                return "0 B";
            }

            var sign = value < 0 ? "-" : "+";
            var n = Math.Abs((double)value);
            string[] units = { "B", "KB", "MB", "GB", "TB" };
            var i = 0;
            while (n >= 1024 && i < units.Length - 1)
            {
                n /= 1024;
                i++;
            }

            var number = n.ToString(i == 0 ? "0" : "0.00", CultureInfo.InvariantCulture);
            return sign + number + " " + units[i];
        }
    }
}
