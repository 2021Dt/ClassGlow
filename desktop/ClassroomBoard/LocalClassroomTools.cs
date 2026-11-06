using System.Security.Cryptography;

namespace ClassroomBoard;

/// <summary>本机名单和抽取轮次。分组独立于抽签，不消耗本轮名额。</summary>
public sealed class LocalClassroomTools
{
    private string[] roster = [];
    private readonly HashSet<string> drawn = new(StringComparer.Ordinal);
    private HashSet<string> excluded = new(StringComparer.Ordinal);
    public IReadOnlyList<string> Roster => Array.AsReadOnly(roster);
    public int Remaining => roster.Count(s => !drawn.Contains(s) && !excluded.Contains(s));
    public int PresentCount => roster.Count(s => !excluded.Contains(s));
    public void SetExcluded(IEnumerable<string> names) => excluded = names.ToHashSet(StringComparer.Ordinal);
    public LocalClassroomTools(IEnumerable<string>? entries = null) => Replace(entries ?? []);

    public static string[] Parse(string text)
    {
        var entries = text.Replace("\r", "").Split('\n').Select(s => s.Trim()).Where(s => s.Length > 0).ToArray();
        if (entries.Length > 500) throw new ArgumentException("名单最多 500 行，请分批使用。");
        if (entries.Any(s => s.Length > 60 || s.Any(char.IsControl))) throw new ArgumentException("每项最多 60 个字符，不能包含制表符等控制字符。");
        return entries.Distinct(StringComparer.Ordinal).ToArray();
    }
    public void Replace(IEnumerable<string> entries)
    {
        roster = Parse(string.Join('\n', entries));
        ResetRound();
    }
    public void ResetRound() => drawn.Clear();
    internal static void Shuffle(string[] values)
    {
        for (int i = values.Length - 1; i > 0; i--) {
            int other = RandomNumberGenerator.GetInt32(i + 1);
            (values[i], values[other]) = (values[other], values[i]);
        }
    }
    public string[] Draw(int count, bool noRepeat)
    {
        var available = roster.Where(s => !excluded.Contains(s) && (!noRepeat || !drawn.Contains(s))).ToArray();
        if (count < 1 || count > 50) throw new ArgumentException("每次可抽取 1–50 项。");
        if (count > available.Length) throw new ArgumentException(noRepeat ? $"本轮剩余 {available.Length} 项，请减少数量或开始新一轮。" : $"名单只有 {available.Length} 项，请减少抽取数量。");
        Shuffle(available);
        var result = available.Take(count).ToArray();
        if (noRepeat) foreach (string entry in result) drawn.Add(entry);
        return result;
    }
    public string[][] Group(int count)
    {
        if (count < 2 || count > 50 || count > PresentCount) throw new ArgumentException("组数须为 2–50，且不能超过到场人数。");
        var shuffled = roster.Where(s => !excluded.Contains(s)).ToArray(); Shuffle(shuffled);
        var groups = Enumerable.Range(0, count).Select(_ => new List<string>()).ToArray();
        for (int i = 0; i < shuffled.Length; i++) groups[i % count].Add(shuffled[i]);
        return groups.Select(g => g.ToArray()).ToArray();
    }
}
