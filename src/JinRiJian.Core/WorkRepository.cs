using System.Text.Json;
using Microsoft.Data.Sqlite;

namespace JinRiJian.Core;

public sealed class WorkRepository
{
    public const int ApplicationId = 0x4A524A31;
    public const int SchemaVersion = 1;
    private readonly object gate = new();
    public string DatabasePath { get; }
    private readonly string connectionString;

    public WorkRepository(string databasePath)
    {
        DatabasePath = Path.GetFullPath(databasePath);
        Directory.CreateDirectory(Path.GetDirectoryName(DatabasePath)!);
        connectionString = new SqliteConnectionStringBuilder { DataSource = DatabasePath, Pooling = false }.ToString();
        lock (gate)
        {
            using var db = Open();
            using var check = db.CreateCommand();
            check.CommandText = "PRAGMA user_version";
            var version = Convert.ToInt32(check.ExecuteScalar());
            if (version > SchemaVersion) throw new InvalidDataException("数据库来自较新版本的软件，请升级程序。");
            check.CommandText = "PRAGMA application_id";
            var appId = Convert.ToInt32(check.ExecuteScalar());
            if (appId != 0 && appId != ApplicationId) throw new InvalidDataException("这不是今日笺的数据文件。");
            if(appId==0){check.CommandText="SELECT count(*) FROM sqlite_master WHERE type='table' AND name NOT LIKE 'sqlite_%'";if(Convert.ToInt64(check.ExecuteScalar())>0)throw new InvalidDataException("已有文件不是今日笺数据库，未对其进行修改。");}
            using var tx = db.BeginTransaction();
            using var cmd = db.CreateCommand();
            cmd.Transaction = tx;
            cmd.CommandText = """
                CREATE TABLE IF NOT EXISTS tasks(
                    id TEXT PRIMARY KEY, task_date TEXT NOT NULL, title TEXT NOT NULL CHECK(length(trim(title)) BETWEEN 1 AND 200),
                    note TEXT NOT NULL DEFAULT '', planned_start TEXT NULL, planned_end TEXT NULL,
                    status INTEGER NOT NULL CHECK(status IN(0,1,2)), started_ms INTEGER NULL, started_offset INTEGER NULL,
                    completed_ms INTEGER NULL, completed_offset INTEGER NULL, start_zone TEXT NOT NULL, completion_zone TEXT NOT NULL,
                    sort_order INTEGER NOT NULL, created_ms INTEGER NOT NULL, updated_ms INTEGER NOT NULL,
                    deleted_ms INTEGER NULL, version INTEGER NOT NULL,
                    CHECK((status=0 AND started_ms IS NULL AND completed_ms IS NULL) OR
                          (status=1 AND started_ms IS NOT NULL AND completed_ms IS NULL) OR
                          (status=2 AND started_ms IS NOT NULL AND completed_ms IS NOT NULL AND completed_ms>=started_ms)));
                CREATE INDEX IF NOT EXISTS idx_tasks_date ON tasks(task_date,sort_order);
                CREATE INDEX IF NOT EXISTS idx_tasks_status ON tasks(status,task_date);
                CREATE TABLE IF NOT EXISTS task_events(id INTEGER PRIMARY KEY AUTOINCREMENT,task_id TEXT NOT NULL,
                    event_type TEXT NOT NULL,at_ms INTEGER NOT NULL,before_json TEXT NULL,after_json TEXT NOT NULL);
                CREATE TABLE IF NOT EXISTS settings(key TEXT PRIMARY KEY,value TEXT NOT NULL);
                PRAGMA user_version=1;
                """;
            cmd.ExecuteNonQuery();
            cmd.CommandText = $"PRAGMA application_id={ApplicationId};";
            cmd.ExecuteNonQuery();
            tx.Commit();
            using var wal = db.CreateCommand();
            wal.CommandText = "PRAGMA journal_mode=WAL; PRAGMA busy_timeout=5000;";
            wal.ExecuteNonQuery();
        }
    }

    private SqliteConnection Open()
    {
        var db = new SqliteConnection(connectionString);
        db.Open();
        using var cmd = db.CreateCommand();
        cmd.CommandText = "PRAGMA busy_timeout=5000; PRAGMA foreign_keys=ON;";
        cmd.ExecuteNonQuery();
        return db;
    }

    public IReadOnlyList<WorkItem> All(bool includeDeleted = false)
    {
        lock (gate)
        {
            using var db = Open();
            using var cmd = db.CreateCommand();
            cmd.CommandText = "SELECT * FROM tasks " + (includeDeleted ? "" : "WHERE deleted_ms IS NULL ") + "ORDER BY task_date,sort_order,created_ms,id";
            using var r = cmd.ExecuteReader();
            var result = new List<WorkItem>();
            while (r.Read()) result.Add(Read(r));
            return result;
        }
    }

    public WorkItem Get(string id, bool includeDeleted = false)
    {
        lock (gate)
        {
            using var db = Open();
            return Get(db, null, id, includeDeleted);
        }
    }

    private static WorkItem Get(SqliteConnection db, SqliteTransaction? tx, string id, bool includeDeleted = false)
    {
        using var cmd = db.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = "SELECT * FROM tasks WHERE id=$id" + (includeDeleted ? "" : " AND deleted_ms IS NULL");
        cmd.Parameters.AddWithValue("$id", id);
        using var r = cmd.ExecuteReader();
        if (!r.Read()) throw new KeyNotFoundException("事项不存在，或已被删除。");
        return Read(r);
    }

    public WorkItem Insert(WorkItem item)
    {
        lock (gate)
        {
            using var db = Open();
            using var tx = db.BeginTransaction();
            using var cmd = db.CreateCommand();
            cmd.Transaction = tx;
            cmd.CommandText = """
                INSERT INTO tasks VALUES($id,$date,$title,$note,$ps,$pe,$status,$start,$so,$end,$eo,$sz,$ez,$sort,$created,$updated,$deleted,$version)
                """;
            Bind(cmd, item);
            cmd.ExecuteNonQuery();
            Event(db, tx, "created", null, item);
            tx.Commit();
            return item;
        }
    }

    public WorkItem Mutate(string id, string eventType, Func<WorkItem, WorkItem> transform, long? expectedVersion = null, bool includeDeleted = false)
    {
        lock (gate)
        {
            using var db = Open();
            using var tx = db.BeginTransaction();
            var before = Get(db, tx, id, includeDeleted);
            if (expectedVersion is { } expected && before.Version != expected) throw new ConcurrentEditException();
            var proposed = transform(before);
            if (proposed == before) return before;
            var after = proposed with { Version = before.Version + 1 };
            using var cmd = db.CreateCommand();
            cmd.Transaction = tx;
            cmd.CommandText = """
                UPDATE tasks SET title=$title,note=$note,planned_start=$ps,planned_end=$pe,status=$status,
                    started_ms=$start,started_offset=$so,completed_ms=$end,completed_offset=$eo,start_zone=$sz,completion_zone=$ez,
                    sort_order=$sort,updated_ms=$updated,deleted_ms=$deleted,version=$version WHERE id=$id AND version=$oldversion
                """;
            Bind(cmd, after);
            cmd.Parameters.AddWithValue("$oldversion", before.Version);
            if (cmd.ExecuteNonQuery() != 1) throw new ConcurrentEditException();
            Event(db, tx, eventType, before, after);
            tx.Commit();
            return after;
        }
    }

    public IReadOnlyList<WorkEvent> Events(string id)
    {
        lock (gate)
        {
            using var db = Open(); using var cmd = db.CreateCommand();
            cmd.CommandText = "SELECT id,task_id,event_type,at_ms,before_json,after_json FROM task_events WHERE task_id=$id ORDER BY id";
            cmd.Parameters.AddWithValue("$id", id);
            using var r = cmd.ExecuteReader(); var rows = new List<WorkEvent>();
            while (r.Read()) rows.Add(new(r.GetInt64(0),r.GetString(1),r.GetString(2),DateTimeOffset.FromUnixTimeMilliseconds(r.GetInt64(3)),r.IsDBNull(4)?null:r.GetString(4),r.GetString(5)));
            return rows;
        }
    }

    public UserSettings LoadSettings()
    {
        lock (gate)
        {
            using var db = Open();using var cmd = db.CreateCommand();cmd.CommandText = "SELECT value FROM settings WHERE key='user'";
            if (cmd.ExecuteScalar() is not string value) return new();
            try
            {
                var result=JsonSerializer.Deserialize<UserSettings>(value)??new();
                static WindowBounds? Valid(WindowBounds? b)=>b is not null&&double.IsFinite(b.Left)&&double.IsFinite(b.Top)&&double.IsFinite(b.Width)&&double.IsFinite(b.Height)&&b.Width>=260&&b.Height>=220&&b.Dpi>0&&b.Dpi<=10?b:null;
                return result with {Mode=Enum.IsDefined(result.Mode)?result.Mode:DisplayMode.Standard,StandardBounds=Valid(result.StandardBounds),CompactBounds=Valid(result.CompactBounds),Theme=result.Theme is "system" or "light" or "dark"?result.Theme:"system"};
            }
            catch (JsonException) { return new(); }
        }
    }

    public void SaveSettings(UserSettings value)
    {
        lock (gate)
        {
            using var db = Open();using var cmd = db.CreateCommand();
            cmd.CommandText = "INSERT INTO settings(key,value) VALUES('user',$value) ON CONFLICT(key) DO UPDATE SET value=excluded.value";
            cmd.Parameters.AddWithValue("$value",JsonSerializer.Serialize(value));cmd.ExecuteNonQuery();
        }
    }

    public void BackupTo(string destination)
    {
        destination = Path.GetFullPath(destination);
        if (SamePath(destination, DatabasePath)) throw new ArgumentException("备份位置不能与正在使用的数据文件相同。");
        lock (gate)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            var temp = destination + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                using (var source = Open())
                using (var target = new SqliteConnection(new SqliteConnectionStringBuilder {DataSource=temp,Pooling=false}.ToString()))
                { target.Open(); source.BackupDatabase(target); }
                ValidateBackup(temp);
                File.Move(temp,destination,true);
            }
            finally { if (File.Exists(temp)) File.Delete(temp); }
        }
    }

    public string RestoreFrom(string backupPath, string safetyDirectory)
    {
        backupPath = Path.GetFullPath(backupPath);
        if (SamePath(backupPath,DatabasePath)) throw new ArgumentException("请选择独立的备份文件。");
        ValidateBackup(backupPath);
        lock (gate)
        {
            Directory.CreateDirectory(safetyDirectory);
            var safety = Path.Combine(safetyDirectory,$"before-restore-{DateTime.UtcNow:yyyyMMdd-HHmmss}-{Guid.NewGuid():N}.db");
            BackupTo(safety);
            using var source = new SqliteConnection(new SqliteConnectionStringBuilder {DataSource=backupPath,Mode=SqliteOpenMode.ReadOnly,Pooling=false}.ToString());
            source.Open();using var target = Open();source.BackupDatabase(target);
            return safety;
        }
    }

    public static void ValidateBackup(string path)
    {
        using var db = new SqliteConnection(new SqliteConnectionStringBuilder {DataSource=path,Mode=SqliteOpenMode.ReadOnly,Pooling=false}.ToString());
        db.Open();using var cmd=db.CreateCommand();cmd.CommandText="PRAGMA quick_check";
        if (!string.Equals(cmd.ExecuteScalar()?.ToString(),"ok",StringComparison.Ordinal)) throw new InvalidDataException("备份文件校验失败。");
        cmd.CommandText="PRAGMA application_id";if(Convert.ToInt32(cmd.ExecuteScalar())!=ApplicationId)throw new InvalidDataException("这不是今日笺的备份文件。");
        cmd.CommandText="PRAGMA user_version";if(Convert.ToInt32(cmd.ExecuteScalar())!=SchemaVersion)throw new InvalidDataException("备份版本不兼容。");
        cmd.CommandText="SELECT * FROM tasks";
        using(var reader=cmd.ExecuteReader())while(reader.Read())
        {
            var item=Read(reader);
            if(!Enum.IsDefined(item.Status)||item.Title.Trim().Length is 0 or >200)throw new InvalidDataException("备份中的事项内容不完整。");
        }
        cmd.CommandText="SELECT task_id,event_type,at_ms,before_json,after_json FROM task_events LIMIT 0";using(var reader=cmd.ExecuteReader())reader.Read();
        cmd.CommandText="SELECT key,value FROM settings LIMIT 0";using(var reader=cmd.ExecuteReader())reader.Read();
    }

    private static bool SamePath(string a,string b) => string.Equals(a,b,OperatingSystem.IsWindows()?StringComparison.OrdinalIgnoreCase:StringComparison.Ordinal);
    private static void Event(SqliteConnection db,SqliteTransaction tx,string type,WorkItem? before,WorkItem after)
    {
        using var cmd=db.CreateCommand();cmd.Transaction=tx;
        cmd.CommandText="INSERT INTO task_events(task_id,event_type,at_ms,before_json,after_json) VALUES($id,$type,$at,$before,$after)";
        cmd.Parameters.AddWithValue("$id",after.Id);cmd.Parameters.AddWithValue("$type",type);cmd.Parameters.AddWithValue("$at",after.UpdatedAt.ToUnixTimeMilliseconds());
        cmd.Parameters.AddWithValue("$before",before is null?DBNull.Value:JsonSerializer.Serialize(before));cmd.Parameters.AddWithValue("$after",JsonSerializer.Serialize(after));cmd.ExecuteNonQuery();
    }

    private static void Bind(SqliteCommand c,WorkItem x)
    {
        void P(string n,object? v)=>c.Parameters.AddWithValue(n,v??DBNull.Value);
        P("$id",x.Id);P("$date",x.Date.ToString("yyyy-MM-dd"));P("$title",x.Title);P("$note",x.Note);P("$ps",x.PlannedStart?.ToString("HH:mm"));P("$pe",x.PlannedEnd?.ToString("HH:mm"));
        P("$status",(int)x.Status);P("$start",x.StartedAt?.ToUnixTimeMilliseconds());P("$so",x.StartedAt?.Offset.TotalMinutes);P("$end",x.CompletedAt?.ToUnixTimeMilliseconds());P("$eo",x.CompletedAt?.Offset.TotalMinutes);
        P("$sz",x.StartTimeZone);P("$ez",x.CompletionTimeZone);P("$sort",x.SortOrder);P("$created",x.CreatedAt.ToUnixTimeMilliseconds());P("$updated",x.UpdatedAt.ToUnixTimeMilliseconds());P("$deleted",x.DeletedAt?.ToUnixTimeMilliseconds());P("$version",x.Version);
    }
    private static WorkItem Read(SqliteDataReader r)
    {
        string S(string n)=>r.GetString(r.GetOrdinal(n));
        long L(string n)=>r.GetInt64(r.GetOrdinal(n));
        DateTimeOffset? T(string n,string? offset=null){var i=r.GetOrdinal(n);if(r.IsDBNull(i))return null;var t=DateTimeOffset.FromUnixTimeMilliseconds(r.GetInt64(i));return offset is not null?t.ToOffset(TimeSpan.FromMinutes(L(offset))):t;}
        TimeOnly? P(string n)=>r.IsDBNull(r.GetOrdinal(n))?null:TimeOnly.ParseExact(S(n),"HH:mm");
        return new WorkItem {Id=S("id"),Date=DateOnly.ParseExact(S("task_date"),"yyyy-MM-dd"),Title=S("title"),Note=S("note"),PlannedStart=P("planned_start"),PlannedEnd=P("planned_end"),
            Status=(WorkStatus)L("status"),StartedAt=T("started_ms","started_offset"),CompletedAt=T("completed_ms","completed_offset"),StartTimeZone=S("start_zone"),CompletionTimeZone=S("completion_zone"),
            SortOrder=(int)L("sort_order"),CreatedAt=T("created_ms")!.Value,UpdatedAt=T("updated_ms")!.Value,DeletedAt=T("deleted_ms"),Version=L("version")};
    }
}
