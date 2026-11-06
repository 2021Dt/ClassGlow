using System.Text.Json;

namespace ClassroomBoard;

public sealed class ClassroomWorkspace
{
    public string ActiveId { get; set; } = "";
    public List<LocalClassProfile> Classes { get; set; } = [];
}
public sealed class LocalClassProfile
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Name { get; set; } = "我的班级";
    public List<string> Members { get; set; } = [];
    public List<LocalTeam> Teams { get; set; } = [];
    public int ScoreGoal { get; set; } = 10;
    public List<int[]> ScoreUndo { get; set; } = [];
    public int SeatRows { get; set; }
    public int SeatColumns { get; set; }
    public List<string> Seats { get; set; } = [];
    public List<int> LockedSeats { get; set; } = [];
    public List<int> Aisles { get; set; } = [];
    public List<DutyAssignment> Duties { get; set; } = [];
    public int DutyCursor { get; set; }
}
public sealed class LocalTeam
{
    public string Name { get; set; } = "";
    public List<string> Members { get; set; } = [];
    public int Score { get; set; }
}
public sealed class DutyAssignment
{
    public string Task { get; set; } = "";
    public string Person { get; set; } = "";
}

/// <summary>本地课堂数据事务；名单、积分、座位与轮值共用存储，缺席只保留在本次运行。</summary>
public sealed class ClassroomBook
{
    private readonly Action<ClassroomWorkspace> persist;
    private readonly Dictionary<string, HashSet<string>> absent = [];
    public ClassroomWorkspace Data { get; private set; }
    public LocalClassProfile Active => Data.Classes.Single(c => c.Id == Data.ActiveId);
    public IReadOnlySet<string> Absent => absent.GetValueOrDefault(Data.ActiveId) ?? [];
    public string[] Present => Active.Members.Where(m => !Absent.Contains(m)).ToArray();
    public event Action? Changed;
    public ClassroomBook(ClassroomWorkspace data, IEnumerable<string> legacy, Action<ClassroomWorkspace> save)
    {
        persist = save;
        Data = Clone(data);
        if (Data.Classes.Count == 0) { var first = new LocalClassProfile { Members = LocalClassroomTools.Parse(string.Join('\n', legacy)).ToList() }; Data.Classes.Add(first); Data.ActiveId = first.Id; }
        if (!Data.Classes.Any(c => c.Id == Data.ActiveId)) Data.ActiveId = Data.Classes[0].Id;
        Validate(Data);
    }
    private static ClassroomWorkspace Clone(ClassroomWorkspace value) => JsonSerializer.Deserialize<ClassroomWorkspace>(JsonSerializer.Serialize(value))!;
    private static string Label(string value, int limit = 40)
    {
        string text = value.Trim();
        if (text.Length == 0 || text.Length > limit || text.Any(char.IsControl)) throw new ArgumentException($"名称须为 1–{limit} 个字符，不能包含换行或制表符。");
        return text;
    }
    private static void Validate(ClassroomWorkspace data)
    {
        if (data.Classes.Count is < 1 or > 30 || data.Classes.Select(c => c.Id).Distinct().Count() != data.Classes.Count || !data.Classes.Any(c => c.Id == data.ActiveId)) throw new ArgumentException("本机可保存 1–30 份班级名单。");
        foreach (var c in data.Classes) {
            c.Name = Label(c.Name); c.Members = LocalClassroomTools.Parse(string.Join('\n', c.Members)).ToList();
            if (c.Teams.Count > 50 || c.ScoreGoal is < 1 or > 9999) throw new ArgumentException("积分板最多 50 组，目标分须为 1–9999。");
            foreach (var t in c.Teams) { t.Name = Label(t.Name); if (t.Score is < -99999 or > 99999 || t.Members.Any(m => !c.Members.Contains(m))) throw new ArgumentException("积分或成员超出范围。"); }
            var teamMembers = c.Teams.SelectMany(t => t.Members).ToArray();
            if (teamMembers.Distinct().Count() != teamMembers.Length) throw new ArgumentException("同一成员不能重复加入多个积分队伍。");
            if (c.ScoreUndo.Count > 100 || c.ScoreUndo.Any(s => s.Length != c.Teams.Count)) throw new ArgumentException("积分撤销记录无效。");
            if (c.Seats.Count > 0) {
                if (c.SeatRows is < 1 or > 30 || c.SeatColumns is < 1 or > 20 || c.Seats.Count != c.SeatRows * c.SeatColumns) throw new ArgumentException("座位行列无效。");
                var seated = c.Seats.Where(s => s.Length > 0).ToArray();
                if (seated.Length != c.Members.Count || seated.Distinct().Count() != seated.Length || seated.Any(s => !c.Members.Contains(s))) throw new ArgumentException("座位表须包含完整名单，每人一个座位。");
                if (c.Aisles.Concat(c.LockedSeats).Any(i => i < 0 || i >= c.Seats.Count) || c.Aisles.Any(i => c.Seats[i].Length > 0) || c.LockedSeats.Any(i => c.Seats[i].Length == 0)) throw new ArgumentException("过道或锁定座位无效。");
            }
            if (c.Duties.Count > 20 || c.Duties.Select(d => d.Person).Distinct().Count() != c.Duties.Count) throw new ArgumentException("轮值最多 20 项，同一轮每人一项。");
            foreach (var d in c.Duties) { d.Task = Label(d.Task); if (!c.Members.Contains(d.Person)) throw new ArgumentException("轮值成员不在名单中。"); }
        }
    }
    private void Change(Action<ClassroomWorkspace> edit)
    {
        var next = Clone(Data); edit(next); Validate(next); persist(next); Data = next;
        foreach (var key in absent.Keys.Where(id => !Data.Classes.Any(c => c.Id == id)).ToArray()) absent.Remove(key);
        if (absent.TryGetValue(Data.ActiveId, out var excluded)) excluded.IntersectWith(Active.Members);
        Changed?.Invoke();
    }
    private void Edit(Action<LocalClassProfile> action) => Change(d => action(d.Classes.Single(c => c.Id == d.ActiveId)));
    public void Switch(string id) => Change(d => { if (!d.Classes.Any(c => c.Id == id)) throw new ArgumentException("名单不存在。"); d.ActiveId = id; });
    public void Add(string name) => Change(d => { var c = new LocalClassProfile { Name = Label(name) }; if (d.Classes.Any(p => p.Name == c.Name)) throw new ArgumentException("已有同名班级。"); d.Classes.Add(c); d.ActiveId = c.Id; });
    public void Rename(string name) => Edit(c => { string next = Label(name); if (Data.Classes.Any(p => p.Id != c.Id && p.Name == next)) throw new ArgumentException("已有同名班级。"); c.Name = next; });
    public void DeleteActive() => Change(d => { if (d.Classes.Count == 1) throw new ArgumentException("请至少保留一份名单，可使用“清空名单”。"); d.Classes.RemoveAll(c => c.Id == d.ActiveId); d.ActiveId = d.Classes[0].Id; });
    public void SaveRoster(string text) => Edit(c => {
        var members = LocalClassroomTools.Parse(text).ToList();
        if (!c.Members.SequenceEqual(members)) {
            c.Seats.Clear(); c.LockedSeats.Clear(); c.Aisles.Clear(); c.Duties.Clear(); c.DutyCursor = 0;
            foreach (var team in c.Teams) team.Members.RemoveAll(m => !members.Contains(m));
        }
        c.Members = members;
    });
    public void SetAbsent(IEnumerable<string> names)
    {
        var entries = names.ToHashSet(StringComparer.Ordinal);
        if (entries.Any(n => !Active.Members.Contains(n))) throw new ArgumentException("缺席成员不在当前名单中。");
        absent[Data.ActiveId] = entries; Changed?.Invoke();
    }
    public void SetTeams(IEnumerable<IEnumerable<string>> groups) => Edit(c => {
        c.Teams = groups.Select((g, i) => new LocalTeam { Name = $"第 {i + 1} 组", Members = g.ToList() }).ToList(); c.ScoreUndo.Clear();
    });
    public void SetTeamNames(string text) => Edit(c => {
        var names = LocalClassroomTools.Parse(text);
        if (names.Length is < 1 or > 50) throw new ArgumentException("请输入 1–50 个组名，每行一个。");
        c.Teams = names.Select(n => new LocalTeam { Name = Label(n) }).ToList(); c.ScoreUndo.Clear();
    });
    public void SetGoal(int value) => Edit(c => c.ScoreGoal = value);
    private static void RememberScores(LocalClassProfile c) { c.ScoreUndo.Add(c.Teams.Select(t => t.Score).ToArray()); if (c.ScoreUndo.Count > 100) c.ScoreUndo.RemoveAt(0); }
    public void AddScore(int index, int delta) => Edit(c => { if (index < 0 || index >= c.Teams.Count || delta is < -100 or > 100) throw new ArgumentException("积分操作无效。"); RememberScores(c); c.Teams[index].Score = checked(c.Teams[index].Score + delta); });
    public void ResetScores() => Edit(c => { RememberScores(c); c.Teams.ForEach(t => t.Score = 0); });
    public void UndoScore() => Edit(c => { if (c.ScoreUndo.Count == 0) throw new ArgumentException("没有可撤销的积分操作。"); var before = c.ScoreUndo[^1]; for (int i = 0; i < before.Length; i++) c.Teams[i].Score = before[i]; c.ScoreUndo.RemoveAt(c.ScoreUndo.Count - 1); });
    public void MakeSeats(int rows, int columns) => Edit(c => {
        if (rows is < 1 or > 30 || columns is < 1 or > 20 || rows * columns < c.Members.Count) throw new ArgumentException("座位须容纳所有人；最多 30 行、20 列。");
        c.SeatRows = rows; c.SeatColumns = columns; c.Seats = c.Members.Concat(Enumerable.Repeat("", rows * columns - c.Members.Count)).ToList(); c.Aisles.Clear(); c.LockedSeats.Clear();
    });
    private static void SeatIndex(LocalClassProfile c, int index) { if (index < 0 || index >= c.Seats.Count) throw new ArgumentException("请先选择一个座位。"); }
    public void SwapSeats(int from, int to) => Edit(c => { SeatIndex(c, from); SeatIndex(c, to); if (c.LockedSeats.Contains(from) || c.LockedSeats.Contains(to) || c.Aisles.Contains(from) || c.Aisles.Contains(to)) throw new ArgumentException("请先解除锁定或取消过道。"); (c.Seats[from], c.Seats[to]) = (c.Seats[to], c.Seats[from]); });
    public void ToggleSeatLock(int index) => Edit(c => { SeatIndex(c, index); if (c.Seats[index].Length == 0) throw new ArgumentException("空位不能锁定。"); if (!c.LockedSeats.Remove(index)) c.LockedSeats.Add(index); });
    public void ToggleAisle(int index) => Edit(c => {
        SeatIndex(c, index); if (c.LockedSeats.Contains(index)) throw new ArgumentException("请先解除座位锁定。");
        if (c.Aisles.Remove(index)) return;
        if (c.Seats[index].Length > 0) { int empty = Enumerable.Range(0, c.Seats.Count).FirstOrDefault(i => i != index && c.Seats[i].Length == 0 && !c.Aisles.Contains(i), -1); if (empty < 0) throw new ArgumentException("没有可移动到的空位，请先增加行列。"); c.Seats[empty] = c.Seats[index]; c.Seats[index] = ""; }
        c.Aisles.Add(index);
    });
    public void ShuffleSeats() => Edit(c => {
        var slots = Enumerable.Range(0, c.Seats.Count).Where(i => !c.LockedSeats.Contains(i) && !c.Aisles.Contains(i)).ToArray();
        var values = slots.Select(i => c.Seats[i]).ToArray(); LocalClassroomTools.Shuffle(values);
        for (int i = 0; i < slots.Length; i++) c.Seats[slots[i]] = values[i];
    });
    public void RotateDuties(string text) => Edit(c => {
        var tasks = LocalClassroomTools.Parse(text); var present = c.Members.Where(m => !Absent.Contains(m)).ToHashSet();
        if (tasks.Length is < 1 or > 20 || tasks.Length > present.Count) throw new ArgumentException("任务须为 1–20 项，且不能多于当前到场人数。");
        var next = new List<DutyAssignment>(); int cursor = c.DutyCursor % c.Members.Count;
        while (next.Count < tasks.Length) { string person = c.Members[cursor]; cursor = (cursor + 1) % c.Members.Count; if (present.Contains(person)) next.Add(new() { Task = Label(tasks[next.Count]), Person = person }); }
        c.Duties = next; c.DutyCursor = cursor;
    });
    public void ReplaceDuty(int index, string person) => Edit(c => {
        if (index < 0 || index >= c.Duties.Count || !c.Members.Contains(person) || Absent.Contains(person)) throw new ArgumentException("请选择当前到场成员。");
        int other = c.Duties.FindIndex(d => d.Person == person); if (other >= 0) c.Duties[other].Person = c.Duties[index].Person; c.Duties[index].Person = person;
    });
}
