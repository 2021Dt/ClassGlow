using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Text;
using System.Xml;
using System.Xml.Linq;

namespace ClassroomBoard;

public sealed record RosterSheet(string Name, string[][] Rows)
{
    public int Columns => Rows.Length == 0 ? 0 : Rows.Max(r => r.Length);
    public string[] Select(int nameColumn, int idColumn, bool header)
    {
        if (nameColumn < 0 || nameColumn >= Columns || idColumn >= Columns || idColumn == nameColumn) throw new ArgumentException("请选择姓名列，学号列可选但不能与姓名列相同。");
        var names = new List<string>();
        foreach (var row in Rows.Skip(header ? 1 : 0)) {
            string name = nameColumn < row.Length ? row[nameColumn].Trim() : "";
            string id = idColumn >= 0 && idColumn < row.Length ? row[idColumn].Trim() : "";
            if (name.Length == 0) { if (id.Length > 0) throw new ArgumentException("存在有学号但姓名为空的行，请先补齐姓名。"); continue; }
            names.Add(id.Length > 0 ? $"{id} {name}" : name);
        }
        var parsed = LocalClassroomTools.Parse(string.Join('\n', names));
        if (parsed.Length == 0) throw new ArgumentException("选定列没有可导入的名单。");
        if (parsed.Length != names.Count) throw new ArgumentException("发现完全相同的条目，请选择学号列区分同名学生，或先在原文件去重。");
        return parsed;
    }
}

/// <summary>有限大小的表格读取。仅提取文本/缓存值，不执行公式、宏、外部链接或 Office。</summary>
public static class RosterImport
{
    private const long MaxXml = 8 * 1024 * 1024;
    public static RosterSheet[] Read(string path)
    {
        var file = new FileInfo(path);
        if (file.Length > 16 * 1024 * 1024) throw new ArgumentException("名单文件须小于 16 MB。");
        string extension = file.Extension.ToLowerInvariant();
        if (extension == ".xlsx") return ReadXlsx(path);
        if (extension is not (".csv" or ".txt")) throw new ArgumentException("支持 .xlsx、.csv 和 .txt；旧 .xls 请先另存为 .xlsx。");
        byte[] bytes = File.ReadAllBytes(path); string text;
        if (bytes.Length >= 2 && (bytes[0] == 0xff && bytes[1] == 0xfe || bytes[0] == 0xfe && bytes[1] == 0xff)) {
            text = (bytes[0] == 0xff ? Encoding.Unicode : Encoding.BigEndianUnicode).GetString(bytes, 2, bytes.Length - 2);
        } else {
            try { text = new UTF8Encoding(false, true).GetString(bytes).TrimStart('\ufeff'); }
            catch (DecoderFallbackException) { Encoding.RegisterProvider(CodePagesEncodingProvider.Instance); text = Encoding.GetEncoding("GB18030", EncoderFallback.ExceptionFallback, DecoderFallback.ExceptionFallback).GetString(bytes); }
        }
        return [new(file.Name, ParseDelimited(text, extension == ".txt"))];
    }
    internal static string[][] ParseDelimited(string text, bool plainText = false)
    {
        if (text.Length > MaxXml) throw new ArgumentException("名单文本过大。");
        string first = text.Split('\n', 2)[0]; bool inside = false; var counts = new Dictionary<char, int> { [','] = 0, ['\t'] = 0, [';'] = 0 };
        foreach (char ch in first) { if (ch == '"') inside = !inside; else if (!inside && counts.ContainsKey(ch)) counts[ch]++; }
        char delimiter = counts.OrderByDescending(p => p.Value).First().Key;
        if (plainText && counts['\t'] == 0) delimiter = '\0';
        var rows = new List<string[]>(); var fields = new List<string>(); var cell = new StringBuilder(); bool quoted = false, closed = false;
        void Field() { if (fields.Count >= 64 || cell.Length > 32767) throw new ArgumentException("表格最多读取 64 列，单元格内容过长。"); fields.Add(cell.ToString()); cell.Clear(); closed = false; }
        void Row() { Field(); if (fields.Any(s => !string.IsNullOrWhiteSpace(s))) rows.Add(fields.ToArray()); fields.Clear(); if (rows.Count > 2001) throw new ArgumentException("表格超过 2000 行，请先筛选为所需班级。"); }
        for (int i = 0; i < text.Length; i++) {
            char ch = text[i];
            if (quoted) { if (ch == '"') { if (i + 1 < text.Length && text[i + 1] == '"') { cell.Append('"'); i++; } else { quoted = false; closed = true; } } else cell.Append(ch); }
            else if (ch == delimiter) Field();
            else if (ch is '\r' or '\n') { if (ch == '\r' && i + 1 < text.Length && text[i + 1] == '\n') i++; Row(); }
            else if (ch == '"' && cell.Length == 0 && !closed) quoted = true;
            else { if (closed && !char.IsWhiteSpace(ch)) throw new ArgumentException("CSV 引号后的格式有误。"); if (!closed) cell.Append(ch); }
        }
        if (quoted) throw new ArgumentException("CSV 存在未闭合的引号。");
        if (cell.Length > 0 || fields.Count > 0 || closed) Row();
        return rows.ToArray();
    }
    private static XDocument Xml(ZipArchive zip, string path)
    {
        var entry = zip.GetEntry(path) ?? throw new ArgumentException("Excel 文件缺少必要的工作表结构。");
        if (entry.Length > MaxXml) throw new ArgumentException("Excel 工作表过大，请另存所需名单后导入。");
        using var stream = entry.Open(); using var reader = XmlReader.Create(stream, new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null, MaxCharactersInDocument = MaxXml }); return XDocument.Load(reader);
    }
    private static string Attr(XElement node, string key) => node.Attributes().FirstOrDefault(a => a.Name.LocalName == key)?.Value ?? "";
    private static IEnumerable<XElement> Nodes(XContainer node, string name) => node.Descendants().Where(e => e.Name.LocalName == name);
    private static string RichText(XElement node) => string.Concat(Nodes(node, "t").Where(t => !t.Ancestors().Any(a => a.Name.LocalName == "rPh")).Select(t => t.Value));
    private static RosterSheet[] ReadXlsx(string path)
    {
        using var zip = ZipFile.OpenRead(path);
        if (zip.Entries.Count > 2048 || zip.Entries.Sum(e => e.Length) > 64 * 1024 * 1024) throw new ArgumentException("Excel 解压后过大，请只保留名单工作表。");
        var workbook = Xml(zip, "xl/workbook.xml"); var relations = Xml(zip, "xl/_rels/workbook.xml.rels");
        var shared = zip.GetEntry("xl/sharedStrings.xml") is null ? [] : Nodes(Xml(zip, "xl/sharedStrings.xml"), "si").Select(RichText).ToArray();
        var zeroFormats = new Dictionary<string, int>(); var styles = new List<int>();
        if (zip.GetEntry("xl/styles.xml") is not null) {
            var xml = Xml(zip, "xl/styles.xml");
            foreach (var format in Nodes(xml, "numFmt")) { string code = Attr(format, "formatCode"); if (code.Length is > 1 and <= 20 && code.All(c => c == '0')) zeroFormats[Attr(format, "numFmtId")] = code.Length; }
            var xfs = Nodes(xml, "cellXfs").FirstOrDefault(); if (xfs is not null) styles.AddRange(xfs.Elements().Select(x => zeroFormats.GetValueOrDefault(Attr(x, "numFmtId"))));
        }
        var result = new List<RosterSheet>();
        foreach (var sheet in Nodes(workbook, "sheet")) {
            if (result.Count >= 30) throw new ArgumentException("工作表超过 30 个，请只保留所需班级。");
            var rel = Nodes(relations, "Relationship").FirstOrDefault(r => Attr(r, "Id") == Attr(sheet, "id")) ?? throw new ArgumentException("工作表关系缺失。");
            if (Attr(rel, "TargetMode") == "External") throw new ArgumentException("不导入外部链接的工作表。");
            string target = new Uri(new Uri("https://xlsx.local/xl/workbook.xml"), Attr(rel, "Target")).AbsolutePath.TrimStart('/');
            var xml = Xml(zip, Uri.UnescapeDataString(target)); var rows = new List<string[]>();
            foreach (var row in Nodes(xml, "row")) {
                var values = new Dictionary<int, string>(); int sequential = 0;
                foreach (var cell in row.Elements().Where(e => e.Name.LocalName == "c")) {
                    string reference = Attr(cell, "r"); int column = 0;
                    foreach (char letter in reference.TakeWhile(char.IsLetter)) column = checked(column * 26 + char.ToUpperInvariant(letter) - 'A' + 1);
                    column = column == 0 ? sequential : column - 1; sequential = column + 1;
                    string type = Attr(cell, "t"), value = cell.Elements().FirstOrDefault(e => e.Name.LocalName == "v")?.Value ?? "";
                    if (value.Length == 0 && cell.Elements().Any(e => e.Name.LocalName == "f")) throw new ArgumentException("工作表含未计算的公式，请用 Excel 保存计算结果，或另存为纯值名单后导入。");
                    if (type == "s") { if (!int.TryParse(value, out int index) || index < 0 || index >= shared.Length) throw new ArgumentException("Excel 共享文本索引无效。"); value = shared[index]; }
                    else if (type == "inlineStr") value = RichText(cell);
                    else if (type == "e" && value.Length > 0) throw new ArgumentException("工作表含公式错误，请修正或另存为纯值名单。");
                    else if (int.TryParse(Attr(cell, "s"), out int style) && style >= 0 && style < styles.Count && styles[style] > 0 && long.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out long number) && number >= 0) value = number.ToString(new string('0', styles[style]), CultureInfo.InvariantCulture);
                    if (value.Length == 0) continue;
                    if (column is < 0 or >= 64) throw new ArgumentException("工作表有内容的列超过 64 列，请先筛选名单。");
                    values[column] = value;
                }
                if (values.Count > 0) { rows.Add(Enumerable.Range(0, values.Keys.Max() + 1).Select(i => values.GetValueOrDefault(i, "")).ToArray()); if (rows.Count > 2001) throw new ArgumentException("工作表超过 2000 行，请先筛选名单。"); }
            }
            result.Add(new(Attr(sheet, "name"), rows.ToArray()));
        }
        if (result.Count == 0) throw new ArgumentException("Excel 没有可读取的工作表。");
        return result.ToArray();
    }
}
