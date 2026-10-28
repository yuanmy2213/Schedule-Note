using System.Globalization;
using System.IO.Compression;
using System.Text;
using System.Xml;

namespace JinRiJian.Core;

public static class MonthlyExporter
{
    private const string Ns="http://schemas.openxmlformats.org/spreadsheetml/2006/main";
    private sealed record Cell(object? Value,int Style=0);

    public static void Export(IReadOnlyList<WorkItem> source,int year,int month,DateTimeOffset exportedAt,string path)
    {
        _=new DateOnly(year,month,1);
        var items=source.Where(t=>t.DeletedAt is null&&t.Date.Year==year&&t.Date.Month==month).OrderBy(t=>t.Date).ThenBy(t=>t.SortOrder).ThenBy(t=>t.CreatedAt).ToArray();
        path=Path.GetFullPath(path);Directory.CreateDirectory(Path.GetDirectoryName(path)!);var temporary=path+"."+Guid.NewGuid().ToString("N")+".tmp";
        try
        {
            using(var stream=new FileStream(temporary,FileMode.CreateNew,FileAccess.ReadWrite,FileShare.None))
            using(var zip=new ZipArchive(stream,ZipArchiveMode.Create,true,Encoding.UTF8))
            {
                Text(zip,"[Content_Types].xml","""
                    <?xml version="1.0" encoding="utf-8"?><Types xmlns="http://schemas.openxmlformats.org/package/2006/content-types"><Default Extension="rels" ContentType="application/vnd.openxmlformats-package.relationships+xml"/><Default Extension="xml" ContentType="application/xml"/><Override PartName="/xl/workbook.xml" ContentType="application/vnd.openxmlformats-officedocument.spreadsheetml.sheet.main+xml"/><Override PartName="/xl/styles.xml" ContentType="application/vnd.openxmlformats-officedocument.spreadsheetml.styles+xml"/><Override PartName="/xl/worksheets/sheet1.xml" ContentType="application/vnd.openxmlformats-officedocument.spreadsheetml.worksheet+xml"/><Override PartName="/xl/worksheets/sheet2.xml" ContentType="application/vnd.openxmlformats-officedocument.spreadsheetml.worksheet+xml"/></Types>
                    """);
                Text(zip,"_rels/.rels","""
                    <?xml version="1.0" encoding="utf-8"?><Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships"><Relationship Id="rId1" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument" Target="xl/workbook.xml"/></Relationships>
                    """);
                Text(zip,"xl/workbook.xml","""
                    <?xml version="1.0" encoding="utf-8"?><workbook xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main" xmlns:r="http://schemas.openxmlformats.org/officeDocument/2006/relationships"><sheets><sheet name="事项明细" sheetId="1" r:id="rId1"/><sheet name="每日汇总" sheetId="2" r:id="rId2"/></sheets></workbook>
                    """);
                Text(zip,"xl/_rels/workbook.xml.rels","""
                    <?xml version="1.0" encoding="utf-8"?><Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships"><Relationship Id="rId1" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/worksheet" Target="worksheets/sheet1.xml"/><Relationship Id="rId2" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/worksheet" Target="worksheets/sheet2.xml"/><Relationship Id="rId3" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/styles" Target="styles.xml"/></Relationships>
                    """);
                Text(zip,"xl/styles.xml","""
                    <?xml version="1.0" encoding="utf-8"?><styleSheet xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main"><numFmts count="3"><numFmt numFmtId="164" formatCode="yyyy-mm-dd"/><numFmt numFmtId="165" formatCode="yyyy-mm-dd hh:mm"/><numFmt numFmtId="166" formatCode="0.0%"/></numFmts><fonts count="2"><font><sz val="11"/><name val="Microsoft YaHei"/></font><font><b/><sz val="11"/><name val="Microsoft YaHei"/></font></fonts><fills count="3"><fill><patternFill patternType="none"/></fill><fill><patternFill patternType="gray125"/></fill><fill><patternFill patternType="solid"><fgColor rgb="FFE8F0E8"/><bgColor indexed="64"/></patternFill></fill></fills><borders count="1"><border><left/><right/><top/><bottom/><diagonal/></border></borders><cellStyleXfs count="1"><xf numFmtId="0" fontId="0" fillId="0" borderId="0"/></cellStyleXfs><cellXfs count="5"><xf numFmtId="0" fontId="0" fillId="0" borderId="0" xfId="0"><alignment vertical="top" wrapText="1"/></xf><xf numFmtId="0" fontId="1" fillId="2" borderId="0" xfId="0" applyFill="1"><alignment vertical="center" wrapText="1"/></xf><xf numFmtId="164" fontId="0" fillId="0" borderId="0" xfId="0" applyNumberFormat="1"/><xf numFmtId="165" fontId="0" fillId="0" borderId="0" xfId="0" applyNumberFormat="1"/><xf numFmtId="166" fontId="0" fillId="0" borderId="0" xfId="0" applyNumberFormat="1"/></cellXfs><cellStyles count="1"><cellStyle name="Normal" xfId="0" builtinId="0"/></cellStyles></styleSheet>
                    """);
                var detail=new List<IReadOnlyList<Cell>>{Header("归属日期","事项名称","状态","预计开始","预计结束","实际开始","实际完成","开始时区偏移","完成时区偏移","事项历时（分钟）","备注","事项ID")};
                foreach(var t in items)detail.Add([
                    new(t.Date.ToDateTime(TimeOnly.MinValue).ToOADate(),2),new(t.Title),new(t.StatusText),
                    Planned(t.Date,t.PlannedStart),Planned(t.Date,t.PlannedEnd),Actual(t.StartedAt),Actual(t.CompletedAt),
                    new(t.StartedAt?.ToString("zzz")),new(t.CompletedAt?.ToString("zzz")),new(t.ElapsedMinutes),new(t.Note),new(t.Id)]);
                if(items.Length==0)detail.Add([new("本月无事项")]);
                Sheet(zip,"xl/worksheets/sheet1.xml",detail,[13,40,12,22,22,22,22,16,16,20,55,36],true);
                var summary=new List<IReadOnlyList<Cell>>{Header("归属日期","事项总数","已完成","进行中","待开始","完成率")};
                foreach(var day in items.GroupBy(t=>t.Date))summary.Add([new(day.Key.ToDateTime(TimeOnly.MinValue).ToOADate(),2),new(day.Count()),new(day.Count(t=>t.Status==WorkStatus.Completed)),new(day.Count(t=>t.Status==WorkStatus.Running)),new(day.Count(t=>t.Status==WorkStatus.Pending)),new((double)day.Count(t=>t.Status==WorkStatus.Completed)/day.Count(),4)]);
                if(items.Length==0)summary.Add([new("本月无事项")]);
                summary.Add([]);summary.Add([new("导出时间"),new(exportedAt.ToString("yyyy-MM-dd HH:mm:ss zzz"))]);
                summary.Add([new("统计口径"),new("按原归属日期统计；含全部状态，不含已删除事项。事项历时包含中途间隔，不代表净工时。")]);
                Sheet(zip,"xl/worksheets/sheet2.xml",summary,[15,35,15,15,15,15],false);
            }
            File.Move(temporary,path,true);
        }
        finally{if(File.Exists(temporary))File.Delete(temporary);}
    }

    private static IReadOnlyList<Cell> Header(params string[] names)=>names.Select(n=>new Cell(n,1)).ToArray();
    private static Cell Planned(DateOnly d,TimeOnly? t)=>t is { } time?new(d.ToDateTime(time).ToOADate(),3):new(null);
    private static Cell Actual(DateTimeOffset? t)=>t is { } value?new(value.DateTime.ToOADate(),3):new(null);
    private static void Text(ZipArchive zip,string name,string value){using var writer=new StreamWriter(zip.CreateEntry(name).Open(),new UTF8Encoding(false));writer.Write(value.TrimStart());}
    private static void Sheet(ZipArchive zip,string name,IReadOnlyList<IReadOnlyList<Cell>> rows,double[] widths,bool filter)
    {
        using var writer=XmlWriter.Create(zip.CreateEntry(name).Open(),new XmlWriterSettings{Encoding=new UTF8Encoding(false),CloseOutput=true});
        writer.WriteStartDocument();writer.WriteStartElement("worksheet",Ns);
        writer.WriteStartElement("sheetViews");writer.WriteStartElement("sheetView");writer.WriteAttributeString("workbookViewId","0");writer.WriteStartElement("pane");writer.WriteAttributeString("ySplit","1");writer.WriteAttributeString("topLeftCell","A2");writer.WriteAttributeString("activePane","bottomLeft");writer.WriteAttributeString("state","frozen");writer.WriteEndElement();writer.WriteEndElement();writer.WriteEndElement();
        writer.WriteStartElement("cols");for(var i=0;i<widths.Length;i++){writer.WriteStartElement("col");writer.WriteAttributeString("min",(i+1).ToString(CultureInfo.InvariantCulture));writer.WriteAttributeString("max",(i+1).ToString(CultureInfo.InvariantCulture));writer.WriteAttributeString("width",widths[i].ToString(CultureInfo.InvariantCulture));writer.WriteAttributeString("customWidth","1");writer.WriteEndElement();}writer.WriteEndElement();
        writer.WriteStartElement("sheetData");
        for(var r=0;r<rows.Count;r++)
        {
            writer.WriteStartElement("row");writer.WriteAttributeString("r",(r+1).ToString(CultureInfo.InvariantCulture));
            if(r==0){writer.WriteAttributeString("ht","28");writer.WriteAttributeString("customHeight","1");}
            for(var c=0;c<rows[r].Count;c++)
            {
                var cell=rows[r][c];if(cell.Value is null)continue;
                writer.WriteStartElement("c");writer.WriteAttributeString("r",Column(c+1)+(r+1).ToString(CultureInfo.InvariantCulture));writer.WriteAttributeString("s",cell.Style.ToString(CultureInfo.InvariantCulture));
                if(cell.Value is string text){writer.WriteAttributeString("t","inlineStr");writer.WriteStartElement("is");writer.WriteStartElement("t");writer.WriteAttributeString("xml","space",null,"preserve");writer.WriteString(Clean(text));writer.WriteEndElement();writer.WriteEndElement();}
                else writer.WriteElementString("v",Convert.ToString(cell.Value,CultureInfo.InvariantCulture));
                writer.WriteEndElement();
            }
            writer.WriteEndElement();
        }
        writer.WriteEndElement();
        if(filter&&rows.Count>1){writer.WriteStartElement("autoFilter");writer.WriteAttributeString("ref",$"A1:{Column(widths.Length)}{rows.Count}");writer.WriteEndElement();}
        writer.WriteEndElement();writer.WriteEndDocument();
    }
    private static string Clean(string s)
    {
        var clean=new StringBuilder(s.Length);
        for(var i=0;i<s.Length;i++)
        {
            if(char.IsHighSurrogate(s[i])&&i+1<s.Length&&char.IsLowSurrogate(s[i+1])){clean.Append(s[i]);clean.Append(s[++i]);}
            else if(XmlConvert.IsXmlChar(s[i]))clean.Append(s[i]);
        }
        return clean.ToString();
    }
    private static string Column(int index){var s="";while(index>0){index--;s=(char)('A'+index%26)+s;index/=26;}return s;}
}
