using System.Text;

namespace Metreja.IntegrationTests.Infrastructure;

public static class TraceNormalizer
{
    public static string Normalize(List<TraceEvent> events, bool collapseLoops = true)
    {
        var sb = new StringBuilder();
        var threadMap = new Dictionary<int, string>();
        var threadCounter = 0;

        var lines = new List<string>();

        foreach (var evt in events)
        {
            var line = evt switch
            {
                SessionMetadataEvent meta => $"session_metadata scenario={Quote(meta.Scenario)}",
                LeaveEvent leave => FormatEnterLeave("leave", leave, ResolveThread(leave.Tid, threadMap, ref threadCounter)),
                EnterEvent enter => FormatEnterLeave("enter", enter, ResolveThread(enter.Tid, threadMap, ref threadCounter)),
                ExceptionEvent ex => FormatException(ex, ResolveThread(ex.Tid, threadMap, ref threadCounter)),
                _ => $"unknown event={evt.Event}"
            };

            lines.Add(line);
        }

        if (collapseLoops)
            lines = CollapseConsecutivePairs(lines);

        foreach (var line in lines)
            sb.AppendLine(line);

        return sb.ToString().TrimEnd();
    }

    private static string FormatEnterLeave(string type, EnterEvent evt, string threadId)
    {
        var indent = new string(' ', evt.Depth * 2);
        var asyncFlag = evt.Async ? " [async]" : "";
        return $"[{threadId}] {indent}{type} {evt.Cls}.{evt.M} ({evt.Asm}/{evt.Ns}) depth={evt.Depth}{asyncFlag}";
    }

    private static string FormatException(ExceptionEvent evt, string threadId)
    {
        return $"[{threadId}] exception {evt.ExType} in {evt.Cls}.{evt.M} ({evt.Asm}/{evt.Ns})";
    }

    private static string ResolveThread(int tid, Dictionary<int, string> map, ref int counter)
    {
        if (!map.TryGetValue(tid, out var name))
        {
            counter++;
            name = $"Thread-{counter}";
            map[tid] = name;
        }
        return name;
    }

    private static string Quote(string value) => $"\"{value}\"";

    private static List<string> CollapseConsecutivePairs(List<string> lines)
    {
        var result = new List<string>();
        var i = 0;

        while (i < lines.Count)
        {
            // Try to detect a consecutive enter+leave pair pattern
            if (i + 1 < lines.Count && IsEnterLeavePair(lines[i], lines[i + 1]))
            {
                var enterLine = lines[i];
                var leaveLine = lines[i + 1];
                var count = 1;
                var j = i + 2;

                while (j + 1 < lines.Count &&
                       lines[j] == enterLine &&
                       lines[j + 1] == leaveLine)
                {
                    count++;
                    j += 2;
                }

                if (count > 1)
                {
                    // Extract the key info from the enter line for the collapsed representation
                    result.Add($"{CollapseLabel(enterLine, count)}");
                    i = j;
                    continue;
                }
            }

            result.Add(lines[i]);
            i++;
        }

        return result;
    }

    private static bool IsEnterLeavePair(string line1, string line2)
    {
        // Check if line1 is an enter and line2 is the matching leave
        // Format: [Thread-N] <indent>enter Cls.M (...) depth=D
        // Format: [Thread-N] <indent>leave Cls.M (...) depth=D
        if (!line1.Contains(" enter ") || !line2.Contains(" leave "))
            return false;

        // They should be identical except for enter/leave
        var normalized1 = line1.Replace(" enter ", " VERB ");
        var normalized2 = line2.Replace(" leave ", " VERB ");
        return normalized1 == normalized2;
    }

    private static string CollapseLabel(string enterLine, int count)
    {
        // Transform "[Thread-1]   enter Cls.M (...) depth=D" into
        // "[Thread-1]   [enter+leave x1000] Cls.M (...) depth=D"
        var enterIdx = enterLine.IndexOf(" enter ", StringComparison.Ordinal);
        var prefix = enterLine[..enterIdx];
        var suffix = enterLine[(enterIdx + " enter ".Length)..];
        return $"{prefix} [enter+leave x{count}] {suffix}";
    }
}
