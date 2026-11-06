using System.IO;
using System.IO.Compression;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using ButtonBase = System.Windows.Controls.Primitives.ButtonBase;
using TabControl = System.Windows.Controls.TabControl;

namespace ClassroomBoard;

public static partial class ToolsChecks
{
    private static async Task ToolkitCases(string output, JsonArray results)
    {
        void Case(string name, Action test) {
            try { test(); results.Add(new JsonObject { ["name"] = name, ["passed"] = true }); }
            catch (Exception error) { results.Add(new JsonObject { ["name"] = name, ["passed"] = false, ["detail"] = error.ToString() }); }
        }
        ClassroomBook Book(int people = 8) => new(new(), Enumerable.Range(1, people).Select(i => $"{i:00} 同学"), _ => { });
        Case("legacy-roster-migration-and-class-persistence", () => {
            var store = new StateStore(Path.Combine(output, "legacy-classes")); var state = new ClientState { LocalRoster = ["01 林同学", "02 林同学"] };
            store.Save(state);
            using (var engine = new Engine(store, desktop: new Desktop(), notices: new Notices(), transport: new Transport(), listenForSignals: false)) {
                Require(engine.Book.Active.Members.SequenceEqual(state.LocalRoster), "旧名单未迁移"); string first = engine.Book.Active.Id;
                engine.Book.Rename("一班"); engine.Tools.Draw(1, true); engine.Book.SetAbsent([state.LocalRoster[1]]);
                engine.Book.Add("二班"); engine.SaveLocalRoster("甲\n乙\n丙"); engine.Book.SetTeamNames("甲队\n乙队"); engine.Book.AddScore(0, 5); engine.Book.MakeSeats(2, 2); engine.Book.RotateDuties("讲台\n黑板");
                string second = engine.Book.Active.Id; engine.Book.Switch(first); Require(engine.Tools.PresentCount == 1 && engine.Book.Absent.Count == 1, "切回班级丢失缺席");
                engine.Book.Switch(second); Require(engine.Book.Active.Teams[0].Score == 5 && engine.Book.Active.Seats.Count == 4 && engine.Book.Active.Duties.Count == 2, "班级数据串班");
                string report = engine.Report().ToJsonString(); Require(!report.Contains("同学") && !report.Contains("甲队") && !report.Contains("讲台"), "本地工具数据出现在回报中");
            }
            var saved = store.Load(); Require(saved.LocalWorkspace.Classes.Count == 2 && saved.LocalWorkspace.Classes.Single(c => c.Name == "二班").Teams[0].Score == 5, "加密保存失败");
            Require(!Encoding.UTF8.GetString(File.ReadAllBytes(Path.Combine(store.DirectoryPath, "state.bin"))).Contains("林同学"), "名单明文落盘");
            var restarted = new ClassroomBook(saved.LocalWorkspace, saved.LocalRoster, _ => { }); Require(restarted.Absent.Count == 0, "重启未清空临时缺席");
        });
        Case("class-switch-preserves-independent-draw-rounds", () => {
            using var f = new Fixture(output, "class-rounds"); f.Engine.SaveLocalRoster("甲\n乙\n丙"); string first = f.Engine.Book.Active.Id;
            f.Engine.Tools.Draw(1, true); f.Engine.Book.Add("第二班"); f.Engine.SaveLocalRoster("丁\n戊"); f.Engine.Tools.Draw(1, true); string second = f.Engine.Book.Active.Id;
            f.Engine.Book.Switch(first); Require(f.Engine.Tools.Remaining == 2, "第一班轮次丢失"); f.Engine.Book.Switch(second); Require(f.Engine.Tools.Remaining == 1, "第二班轮次丢失");
        });
        Case("workspace-save-failure-is-atomic", () => {
            bool fail = false; var book = new ClassroomBook(new(), ["甲", "乙"], _ => { if (fail) throw new IOException("isolated failure"); }); book.SetTeamNames("第一组\n第二组"); book.AddScore(0, 3);
            string before = JsonSerializer.Serialize(book.Data); fail = true;
            foreach (Action action in new Action[] { () => book.AddScore(0, 9), () => book.Add("新班"), () => book.SaveRoster("新名单"), () => book.MakeSeats(1, 3), () => book.RotateDuties("擦黑板") }) {
                try { action(); throw new InvalidOperationException("保存失败被吞掉"); } catch (IOException) { }
                Require(JsonSerializer.Serialize(book.Data) == before, "失败操作改变内存数据");
            }
        });
        Case("class-limits-delete-and-name-validation", () => {
            var book = Book(); Reject(book.DeleteActive); Reject(() => book.Rename("\n")); Reject(() => book.Add(book.Active.Name));
            string first = book.Active.Id; for (int i = 1; i < 30; i++) book.Add("班级" + i); Reject(() => book.Add("超限")); book.DeleteActive(); Require(book.Data.Classes.Count == 29 && book.Active.Id == first, "删除后未切换到可用班级");
        });
        Case("absence-excludes-draws-groups-duties", () => {
            using var f = new Fixture(output, "absence"); f.Engine.SaveLocalRoster("甲\n乙\n丙\n丁\n戊"); f.Engine.Book.SetAbsent(["乙", "戊"]);
            Require(f.Engine.Tools.Draw(3, true).Order().SequenceEqual(new[] { "甲", "丙", "丁" }.Order()), "缺席者被抽到");
            Require(f.Engine.Tools.Group(2).SelectMany(g => g).All(n => !f.Engine.Book.Absent.Contains(n)), "缺席者进入分组");
            f.Engine.Book.RotateDuties("黑板\n讲台\n门窗"); Require(f.Engine.Book.Active.Duties.All(d => !f.Engine.Book.Absent.Contains(d.Person)), "缺席者被轮值");
            f.Engine.Book.SetAbsent(f.Engine.Book.Active.Members); Reject(() => f.Engine.Tools.Draw(1, false)); Reject(() => f.Engine.Tools.Group(2)); Reject(() => f.Engine.Book.RotateDuties("任务"));
        });
        Case("score-add-reset-undo-and-bounds", () => {
            var book = Book(); book.SetTeams(book.Active.Members.Chunk(4)); book.AddScore(0, 10); book.AddScore(1, -1); book.UndoScore(); Require(book.Active.Teams[1].Score == 0, "加减撤销错误");
            book.ResetScores(); book.UndoScore(); Require(book.Active.Teams[0].Score == 10, "清零无法撤销");
            string before = JsonSerializer.Serialize(book.Data); Reject(() => book.SetGoal(0)); Reject(() => book.AddScore(0, 101)); Reject(() => book.SetTeams([new[] { book.Active.Members[0] }, new[] { book.Active.Members[0] }])); Require(JsonSerializer.Serialize(book.Data) == before, "无效积分操作产生副作用");
            for (int i = 0; i < 105; i++) book.AddScore(0, 1); Require(book.Active.ScoreUndo.Count == 100, "撤销历史没有上限");
        });
        Case("seating-coverage-locks-aisles-swaps", () => {
            var book = Book(17); book.MakeSeats(4, 5); book.ToggleSeatLock(0); string locked = book.Active.Seats[0]; book.ToggleAisle(1);
            Require(book.Active.Seats[1] == "" && book.Active.Aisles.Contains(1), "过道没有腾空");
            for (int i = 0; i < 35; i++) {
                book.ShuffleSeats(); var seats = book.Active.Seats.Where(s => s.Length > 0).ToArray();
                Require(seats.Length == 17 && seats.Distinct().Count() == 17 && book.Active.Seats[0] == locked && book.Active.Seats[1] == "", "随机排座遗漏、重复或移动锁定席");
            }
            Reject(() => book.SwapSeats(0, 2)); Reject(() => book.SwapSeats(1, 2)); book.SwapSeats(2, 3); Reject(() => book.MakeSeats(2, 2));
            var image = ToolUi.Render(SeatsPanel.SeatImage(book.Active), 840, 520); var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(image)); using var stream = File.Create(Path.Combine(output, "seats-export.png")); encoder.Save(stream);
        });
        Case("seating-full-capacity-and-roster-edit", () => {
            var book = Book(4); book.MakeSeats(2, 2); string before = JsonSerializer.Serialize(book.Data); Reject(() => book.ToggleAisle(0)); Require(JsonSerializer.Serialize(book.Data) == before, "没有空位时仍丢失成员");
            book.RotateDuties("任务"); book.SetTeamNames("组一"); book.AddScore(0, 5); book.SaveRoster("新甲\n新乙"); Require(book.Active.Seats.Count == 0 && book.Active.Duties.Count == 0 && book.Active.Teams[0].Score == 5, "改名单未清理失效分配或错误清空积分");
        });
        Case("duty-rotation-fairness-and-replacement", () => {
            var book = Book(6); var seen = new List<string>(); for (int i = 0; i < 3; i++) { book.RotateDuties("黑板\n讲台"); seen.AddRange(book.Active.Duties.Select(d => d.Person)); }
            Require(seen.Distinct().Count() == 6, "完整轮换有重复或遗漏"); string first = book.Active.Duties[0].Person, second = book.Active.Duties[1].Person; int cursor = book.Active.DutyCursor;
            book.ReplaceDuty(0, second); Require(book.Active.Duties[0].Person == second && book.Active.Duties[1].Person == first && book.Active.DutyCursor == cursor, "临时换人没有交换或改变了轮换进度");
        });
        Case("csv-quotes-bom-and-chinese-encoding", () => {
            var parsed = RosterImport.ParseDelimited("学号,姓名\r\n001,\"王,明\"\r\n002,\"李\"\"同学\"\r\n");
            Require(new RosterSheet("csv", parsed).Select(1, 0, true).SequenceEqual(new[] { "001 王,明", "002 李\"同学" }), "CSV 转义或学号丢失");
            foreach (var (name, encoding) in new[] { ("utf8.csv", (Encoding)new UTF8Encoding(true)), ("unicode.csv", Encoding.Unicode) }) {
                string path = Path.Combine(output, name); File.WriteAllText(path, "学号,姓名\n001,王同学", encoding); Require(RosterImport.Read(path)[0].Select(1, 0, true)[0] == "001 王同学", "文件编码识别错误");
            }
            Encoding.RegisterProvider(CodePagesEncodingProvider.Instance); string gb = Path.Combine(output, "gb18030.csv"); File.WriteAllText(gb, "姓名;学号\n陈同学;003", Encoding.GetEncoding("GB18030")); Require(RosterImport.Read(gb)[0].Select(0, 1, true)[0] == "003 陈同学", "中文 CSV 解码错误");
            Reject(() => RosterImport.ParseDelimited("\"未结束")); Reject(() => RosterImport.ParseDelimited("\"甲\"错误,乙"));
        });
        Case("import-duplicates-empty-name-and-limits", () => {
            var sheet = new RosterSheet("同名", [["姓名", "学号"], ["王同学", "01"], ["王同学", "02"]]); Reject(() => sheet.Select(0, -1, true)); Require(sheet.Select(0, 1, true).Length == 2, "学号未区分同名");
            Reject(() => new RosterSheet("空姓名", [["", "01"]]).Select(0, 1, false)); Reject(() => new RosterSheet("空表", []).Select(0, -1, false)); Reject(() => sheet.Select(0, 0, true));
            Reject(() => new RosterSheet("超限", Enumerable.Range(0, 501).Select(i => new[] { "姓名" + i }).ToArray()).Select(0, -1, false));
        });
        Case("xlsx-shared-inline-sparse-style-and-multiple-sheets", () => {
            string path = Path.Combine(output, "roster.xlsx"); WriteRosterXlsx(path);
            var sheets = RosterImport.Read(path); Require(sheets.Length == 2 && sheets[0].Name == "一班", "工作表读取错误");
            Require(sheets[0].Select(2, 0, true).SequenceEqual(new[] { "001 王同学", "002 李同学" }), "共享文字、富文本、稀疏列或学号零丢失");
            Require(sheets[1].Select(0, -1, false).Single() == "备用同学", "第二工作表读取错误");
            string missing = Path.Combine(output, "uncalculated.xlsx"); WriteRosterXlsx(missing);
            using (var zip = ZipFile.Open(missing, ZipArchiveMode.Update)) {
                zip.GetEntry("xl/worksheets/sheet2.xml")!.Delete();
                using var writer = new StreamWriter(zip.CreateEntry("xl/worksheets/sheet2.xml").Open());
                writer.Write("<worksheet><sheetData><row><c><f>CONCAT(1,2)</f></c></row></sheetData></worksheet>");
            }
            Reject(() => RosterImport.Read(missing));
        });
        Case("xlsx-dtd-and-decompression-limits", () => {
            string path = Path.Combine(output, "unsafe.xlsx");
            if (File.Exists(path)) File.Delete(path);
            using (var zip = ZipFile.Open(path, ZipArchiveMode.Create)) { using var writer = new StreamWriter(zip.CreateEntry("xl/workbook.xml").Open()); writer.Write("<!DOCTYPE x [<!ENTITY secret SYSTEM 'file:///not-read'>]><x>&secret;</x>"); }
            bool rejected = false; try { RosterImport.Read(path); } catch (System.Xml.XmlException) { rejected = true; } Require(rejected, "XML 外部实体未禁止");
            string huge = Path.Combine(output, "oversize.xlsx"); if (File.Exists(huge)) File.Delete(huge); using (var zip = ZipFile.Open(huge, ZipArchiveMode.Create)) { using var writer = new StreamWriter(zip.CreateEntry("xl/workbook.xml").Open()); writer.Write(new string(' ', 8 * 1024 * 1024 + 1)); } Reject(() => RosterImport.Read(huge));
        });
        Case("ink-arrow-undo-mask-and-screen-tool-lifecycle", () => {
            var overlay = new TeachingOverlay("whiteboard", new() { Right = 800, Bottom = 600 }); overlay.AddArrow(new(30, 80), new(200, 150)); Require(overlay.Ink.Strokes.Count == 1, "箭头未绘制"); overlay.Undo(); Require(overlay.Ink.Strokes.Count == 0, "批注撤销失败");
            overlay.AddMask(new Rect(30, 100, 200, 100)); overlay.Close();
            using var f = new Fixture(output, "floats"); int captures = 0; f.Engine.ScreenTools.Capture = _ => { captures++; throw new InvalidOperationException(); };
            var fake = new Window(); f.Engine.ScreenTools.Track(fake); f.Engine.SessionChanged(true); Require(f.Engine.ScreenTools.WindowCount == 0 && captures == 0, "锁屏没有关闭浮层或后台截图");
            f.Engine.ScreenTools.Track(new Window()); f.Engine.StopAsync().GetAwaiter().GetResult(); Require(f.Engine.ScreenTools.WindowCount == 0, "退出未关闭浮层");
        });
        Case("ui-group-to-score-seats-and-duties", () => {
            using var f = new Fixture(output, "toolkit-panel"); f.Engine.SaveLocalRoster("01 林同学\n02 陈同学\n03 王同学\n04 李同学\n05 何同学\n06 张同学");
            var panel = new ClassroomToolsPanel(f.Engine); ((System.Windows.Controls.TextBox)panel.FindName("GroupCountInput")).Text = "2";
            ((ButtonBase)panel.FindName("GroupButton")).RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent)); ((ButtonBase)panel.FindName("SendGroupsButton")).RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
            Require(f.Engine.Book.Active.Teams.Count == 2 && f.Engine.Book.Active.Teams.All(t => t.Members.Count == 3), "分组没有进入积分板");
            var tabs = (TabControl)panel.FindName("ToolTabs"); Capture(panel, Path.Combine(output, "toolkit-scores.png"));
            f.Engine.Book.MakeSeats(2, 4); tabs.SelectedIndex = 4; Capture(panel, Path.Combine(output, "toolkit-seats.png"));
            f.Engine.Book.RotateDuties("黑板\n讲台\n门窗"); tabs.SelectedIndex = 5; Capture(panel, Path.Combine(output, "toolkit-duties.png"));
            Require(f.Transport.Requests == 0 && f.Desktop.Writes == 0, "本地工具改写壁纸或发起网络请求");
        });
        await Task.CompletedTask;
    }
    private static void WriteRosterXlsx(string path)
    {
        if (File.Exists(path)) File.Delete(path);
        using var zip = ZipFile.Open(path, ZipArchiveMode.Create);
        void Put(string name, string xml) { using var writer = new StreamWriter(zip.CreateEntry(name).Open()); writer.Write(xml); }
        Put("xl/workbook.xml", """<workbook xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main" xmlns:r="http://schemas.openxmlformats.org/officeDocument/2006/relationships"><sheets><sheet name="一班" sheetId="1" r:id="s1"/><sheet name="二班" sheetId="2" r:id="s2"/></sheets></workbook>""");
        Put("xl/_rels/workbook.xml.rels", """<Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships"><Relationship Id="s1" Target="worksheets/sheet1.xml"/><Relationship Id="s2" Target="/xl/worksheets/sheet2.xml"/></Relationships>""");
        Put("xl/sharedStrings.xml", """<sst><si><t>学号</t></si><si><t>姓名</t></si><si><r><t>王</t></r><r><t>同学</t></r></si></sst>""");
        Put("xl/styles.xml", """<styleSheet><numFmts><numFmt numFmtId="164" formatCode="000"/></numFmts><cellXfs><xf numFmtId="0"/><xf numFmtId="164"/></cellXfs></styleSheet>""");
        Put("xl/worksheets/sheet1.xml", """<worksheet><sheetData><row r="1"><c r="A1" t="s"><v>0</v></c><c r="C1" t="s"><v>1</v></c></row><row r="2"><c r="A2" s="1"><v>1</v></c><c r="C2" t="s"><v>2</v></c></row><row r="3"><c r="A3" s="1"><f>1+1</f><v>2</v></c><c r="C3" t="inlineStr"><is><t>李同学</t></is></c></row></sheetData></worksheet>""");
        Put("xl/worksheets/sheet2.xml", """<worksheet><sheetData><row><c t="inlineStr"><is><t>备用同学</t></is></c></row></sheetData></worksheet>""");
    }
}
