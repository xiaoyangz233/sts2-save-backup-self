using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Windows.Forms;
using Microsoft.Win32;

[assembly: System.Reflection.AssemblyTitle("STS2 Save Backup")]
[assembly: System.Reflection.AssemblyDescription("Slay the Spire 2 旧版整包存档备份器")]
[assembly: System.Reflection.AssemblyProduct("STS2存档备份器")]
[assembly: System.Reflection.AssemblyVersion("1.0.0.0")]
[assembly: System.Reflection.AssemblyFileVersion("1.0.0.0")]

namespace Sts2SaveBackup
{
    internal static class TimeUtil
    {
        private static readonly DateTime Epoch = new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc);

        public static long ToUnixMs(DateTime time)
        {
            DateTime utc = time.Kind == DateTimeKind.Utc ? time : time.ToUniversalTime();
            return (long)(utc - Epoch).TotalMilliseconds;
        }

        public static DateTime FromUnixMs(long ms)
        {
            return Epoch.AddMilliseconds(ms).ToLocalTime();
        }
    }

    internal static class Program
    {
        [STAThread]
        private static int Main(string[] args)
        {
            if (args.Length > 0)
            {
                try { return Cli.Run(args); }
                catch (Exception ex)
                {
                    Console.Error.WriteLine(ex.ToString());
                    return 1;
                }
            }

            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.Run(new MainForm());
            return 0;
        }
    }

    internal static class GamePaths
    {
        public const string AppId = "2868840";
        public const string GameName = "Slay the Spire 2";
        public const string ProcessName = "SlayTheSpire2";

        public static string DetectRemote()
        {
            List<string> roots = new List<string>();

            try
            {
                object v = Registry.GetValue(@"HKEY_CURRENT_USER\Software\Valve\Steam", "SteamPath", null);
                string p = v as string;
                if (!string.IsNullOrEmpty(p)) roots.Add(p.Replace('/', '\\'));
            }
            catch { }

            try
            {
                foreach (DriveInfo d in DriveInfo.GetDrives())
                {
                    if (d.DriveType != DriveType.Fixed || !d.IsReady) continue;
                    string r = d.RootDirectory.FullName;
                    roots.Add(Path.Combine(r, "steam"));
                    roots.Add(Path.Combine(r, "Steam"));
                    roots.Add(Path.Combine(r, "SteamLibrary"));
                    roots.Add(Path.Combine(r, "Program Files (x86)", "Steam"));
                    roots.Add(Path.Combine(r, "Program Files", "Steam"));
                }
            }
            catch { }

            List<string> candidates = new List<string>();
            foreach (string root in roots)
            {
                try
                {
                    string ud = Path.Combine(root, "userdata");
                    if (!Directory.Exists(ud)) continue;
                    foreach (string account in Directory.GetDirectories(ud))
                    {
                        string remote = Path.Combine(account, AppId, "remote");
                        if (Directory.Exists(remote))
                        {
                            bool hasSave = File.Exists(Path.Combine(remote, "profile.save"));
                            bool hasProfile = Directory.GetDirectories(remote, "profile*").Length > 0;
                            if (hasSave || hasProfile) candidates.Add(remote);
                        }
                    }
                }
                catch { }
            }

            string best = "";
            DateTime bestTime = DateTime.MinValue;
            foreach (string c in candidates.Distinct(StringComparer.OrdinalIgnoreCase))
            {
                DateTime t = DateTime.MinValue;
                try
                {
                    string ps = Path.Combine(c, "profile.save");
                    if (File.Exists(ps)) t = File.GetLastWriteTime(ps);
                    else t = Directory.GetLastWriteTime(c);
                }
                catch { }
                if (t > bestTime) { bestTime = t; best = c; }
            }
            return best;
        }

        public static string DefaultOutput(string remote)
        {
            string account = GetAccount(remote);
            if (string.IsNullOrEmpty(account)) account = "default";
            string docs = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
            if (string.IsNullOrEmpty(docs)) docs = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            return Path.Combine(docs, "STS2存档备份", account, AppId);
        }

        public static string GetAccount(string remote)
        {
            try
            {
                DirectoryInfo di = new DirectoryInfo(remote.TrimEnd('\\'));
                if (di.Parent != null && di.Parent.Parent != null) return di.Parent.Parent.Name;
            }
            catch { }
            return "";
        }

        public static List<string> GetProfiles(string remote)
        {
            List<string> result = new List<string>();
            try
            {
                foreach (string dir in Directory.GetDirectories(remote, "profile*"))
                {
                    string name = Path.GetFileName(dir);
                    if (!Regex.IsMatch(name, @"^profile\d+$")) continue;
                    if (Directory.Exists(Path.Combine(dir, "saves"))) result.Add(name);
                }
            }
            catch { }
            result.Sort(delegate(string a, string b) { return ProfileNumber(a).CompareTo(ProfileNumber(b)); });
            return result;
        }

        public static string GetCurrentProfile(string remote)
        {
            try
            {
                string file = Path.Combine(remote, "profile.save");
                if (!File.Exists(file)) return "";
                string text = File.ReadAllText(file, Encoding.UTF8);
                Match m = Regex.Match(text, "\"last_profile_id\"\\s*:\\s*(\\d+)");
                if (m.Success) return "profile" + m.Groups[1].Value;
            }
            catch { }
            return "";
        }

        public static int ReadSchemaVersion(string remote)
        {
            try
            {
                string file = Path.Combine(remote, "profile.save");
                if (!File.Exists(file)) return 2;
                string text = File.ReadAllText(file, Encoding.UTF8);
                Match m = Regex.Match(text, "\"schema_version\"\\s*:\\s*(\\d+)");
                if (m.Success) return int.Parse(m.Groups[1].Value);
            }
            catch { }
            return 2;
        }

        public static bool SetCurrentProfile(string remote, string profile)
        {
            try
            {
                int n = ProfileNumber(profile);
                if (n <= 0) return false;
                string file = Path.Combine(remote, "profile.save");
                if (!File.Exists(file)) return false;
                string text = File.ReadAllText(file, Encoding.UTF8);
                string replaced = Regex.Replace(text, "(\"last_profile_id\"\\s*:\\s*)\\d+", "${1}" + n);
                File.WriteAllText(file, replaced, new UTF8Encoding(false));
                return true;
            }
            catch { return false; }
        }

        public static int ProfileNumber(string profile)
        {
            if (string.IsNullOrEmpty(profile)) return 0;
            Match m = Regex.Match(profile, @"(\d+)$");
            if (m.Success)
            {
                int n;
                if (int.TryParse(m.Groups[1].Value, out n)) return n;
            }
            return 0;
        }

        public static bool IsGameRunning()
        {
            try { return Process.GetProcessesByName(ProcessName).Length > 0; }
            catch { return false; }
        }
    }

    internal sealed class AppSettings
    {
        public string Remote = "";
        public string Output = "";
        public bool AllProfiles = false;
        public bool IncludeRoot = true;
        public bool SkipUnchanged = true;
        public int Keep = 30;
        public int AutoMinutes = 0;
        public bool AutoOnExit = true;

        private static string _dir;

        private static string Dir
        {
            get
            {
                if (!string.IsNullOrEmpty(_dir)) return _dir;

                string primary = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "STS2SaveBackup");
                if (CanWrite(primary)) { _dir = primary; return _dir; }

                // 便携模式：%APPDATA% 不可写时，放在 exe 旁边
                string portable = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "data");
                if (CanWrite(portable)) { _dir = portable; return _dir; }

                string temp = Path.Combine(Path.GetTempPath(), "STS2SaveBackup");
                if (CanWrite(temp)) { _dir = temp; return _dir; }

                _dir = AppDomain.CurrentDomain.BaseDirectory.TrimEnd('\\');
                return _dir;
            }
        }

        private static bool CanWrite(string dir)
        {
            try
            {
                Directory.CreateDirectory(dir);
                string test = Path.Combine(dir, ".write_test");
                File.WriteAllText(test, "ok");
                File.Delete(test);
                return true;
            }
            catch { return false; }
        }

        public static string SettingsFile { get { return Path.Combine(Dir, "settings.ini"); } }
        public static string LogFile { get { return Path.Combine(Dir, "backup.log"); } }

        public static AppSettings Load()
        {
            AppSettings s = new AppSettings();
            try
            {
                if (!File.Exists(SettingsFile)) return s;
                foreach (string line in File.ReadAllLines(SettingsFile, Encoding.UTF8))
                {
                    int idx = line.IndexOf('=');
                    if (idx <= 0) continue;
                    string k = line.Substring(0, idx).Trim();
                    string v = line.Substring(idx + 1).Trim();
                    if (k == "remote") s.Remote = v;
                    else if (k == "output") s.Output = v;
                    else if (k == "all_profiles") s.AllProfiles = ToBool(v);
                    else if (k == "include_root") s.IncludeRoot = ToBool(v);
                    else if (k == "skip_unchanged") s.SkipUnchanged = ToBool(v);
                    else if (k == "keep") { int n; if (int.TryParse(v, out n)) s.Keep = n; }
                    else if (k == "auto_minutes") { int n; if (int.TryParse(v, out n)) s.AutoMinutes = n; }
                    else if (k == "auto_on_exit") s.AutoOnExit = ToBool(v);
                }
            }
            catch { }
            return s;
        }

        public void Save()
        {
            try
            {
                string[] lines = new string[]
                {
                    "remote=" + Remote,
                    "output=" + Output,
                    "all_profiles=" + (AllProfiles ? "1" : "0"),
                    "include_root=" + (IncludeRoot ? "1" : "0"),
                    "skip_unchanged=" + (SkipUnchanged ? "1" : "0"),
                    "keep=" + Keep,
                    "auto_minutes=" + AutoMinutes,
                    "auto_on_exit=" + (AutoOnExit ? "1" : "0")
                };
                File.WriteAllLines(SettingsFile, lines, new UTF8Encoding(false));
            }
            catch { }
        }

        private static bool ToBool(string v)
        {
            return v == "1" || string.Equals(v, "true", StringComparison.OrdinalIgnoreCase)
                || string.Equals(v, "yes", StringComparison.OrdinalIgnoreCase);
        }
    }

    internal sealed class BackupInfo
    {
        public string ZipPath;
        public string Profile;
        public DateTime Time;
        public long ZipBytes;
        public long RawBytes;
        public int FileCount;
        public string Fingerprint = "";
        public bool HasMeta;

        public string FileName { get { return Path.GetFileName(ZipPath); } }
    }

    internal sealed class BackupResult
    {
        public int Created;
        public int Skipped;
        public long Bytes;
        public List<string> Zips = new List<string>();
        public List<string> Errors = new List<string>();
    }

    internal sealed class BackupManager
    {
        private readonly AppSettings _s;
        private readonly Action<string> _log;

        public BackupManager(AppSettings s, Action<string> log)
        {
            _s = s;
            _log = log ?? delegate { };
            Validate();
        }

        private void Validate()
        {
            if (string.IsNullOrEmpty(_s.Remote) || !Directory.Exists(_s.Remote))
                throw new Exception("找不到存档目录，请先选择 remote 文件夹。");
            if (string.IsNullOrEmpty(_s.Output))
                throw new Exception("请选择备份输出目录。");

            string r = Path.GetFullPath(_s.Remote).TrimEnd('\\');
            string o = Path.GetFullPath(_s.Output).TrimEnd('\\');
            if (o.Equals(r, StringComparison.OrdinalIgnoreCase) || o.StartsWith(r + "\\", StringComparison.OrdinalIgnoreCase))
                throw new Exception("备份目录不能放在存档目录里面，否则备份文件会被 Steam 云同步。");
        }

        public List<BackupInfo> ListBackups()
        {
            List<BackupInfo> list = new List<BackupInfo>();
            try
            {
                if (!Directory.Exists(_s.Output)) return list;
                foreach (string f in Directory.GetFiles(_s.Output, "profile*--*----.zip"))
                {
                    Match m = Regex.Match(Path.GetFileName(f), @"^(profile\d+)--(\d+)----\.zip$");
                    if (!m.Success) continue;
                    long ms;
                    if (!long.TryParse(m.Groups[2].Value, out ms)) continue;
                    BackupInfo info = new BackupInfo();
                    info.ZipPath = f;
                    info.Profile = m.Groups[1].Value;
                    try { info.Time = TimeUtil.FromUnixMs(ms); }
                    catch { info.Time = File.GetLastWriteTime(f); }
                    try { info.ZipBytes = new FileInfo(f).Length; } catch { info.ZipBytes = 0; }
                    info.FileCount = -1;
                    LoadMeta(info);
                    list.Add(info);
                }
            }
            catch { }
            list.Sort(delegate(BackupInfo a, BackupInfo b) { return b.Time.CompareTo(a.Time); });
            return list;
        }

        private static void LoadMeta(BackupInfo info)
        {
            string meta = info.ZipPath + ".meta.txt";
            if (!File.Exists(meta)) return;
            try
            {
                info.HasMeta = true;
                foreach (string line in File.ReadAllLines(meta, Encoding.UTF8))
                {
                    int idx = line.IndexOf('=');
                    if (idx <= 0) continue;
                    string k = line.Substring(0, idx).Trim();
                    string v = line.Substring(idx + 1).Trim();
                    if (k == "file_count") { int n; if (int.TryParse(v, out n)) info.FileCount = n; }
                    else if (k == "raw_bytes") { long n; if (long.TryParse(v, out n)) info.RawBytes = n; }
                    else if (k == "fingerprint") info.Fingerprint = v;
                }
            }
            catch { }
        }

        public BackupResult BackupAll(string trigger)
        {
            BackupResult result = new BackupResult();
            if (!Directory.Exists(_s.Output)) Directory.CreateDirectory(_s.Output);

            List<string> profiles;
            if (_s.AllProfiles) profiles = GamePaths.GetProfiles(_s.Remote);
            else
            {
                string cur = GamePaths.GetCurrentProfile(_s.Remote);
                profiles = new List<string>();
                if (!string.IsNullOrEmpty(cur)) profiles.Add(cur);
                else profiles = GamePaths.GetProfiles(_s.Remote).Take(1).ToList();
            }

            if (profiles.Count == 0) throw new Exception("没有找到 profile1/profile2/profile3 之类的档案目录。");

            _log("开始备份（" + trigger + "），档案：" + string.Join("、", profiles.ToArray()));
            long stamp = TimeUtil.ToUnixMs(DateTime.UtcNow);

            foreach (string profile in profiles)
            {
                try
                {
                    string src = Path.Combine(_s.Remote, profile);
                    if (!Directory.Exists(src))
                    {
                        _log("跳过 " + profile + "：目录不存在");
                        continue;
                    }

                    string fp = ComputeFingerprint(src, profile);
                    BackupInfo latest = null;
                    foreach (BackupInfo bi in ListBackups())
                    {
                        if (bi.Profile == profile) { latest = bi; break; }
                    }
                    if (_s.SkipUnchanged && latest != null && latest.HasMeta && latest.Fingerprint == fp)
                    {
                        _log("跳过 " + profile + "：内容和最近一份备份相同");
                        result.Skipped++;
                        continue;
                    }

                    while (File.Exists(Path.Combine(_s.Output, BuildName(profile, stamp)))) stamp++;
                    string zipName = BuildName(profile, stamp);
                    string zipPath = Path.Combine(_s.Output, zipName);
                    string tmp = zipPath + ".tmp";

                    int count = 0;
                    long raw = 0;
                    _log("正在打包 " + profile + " ...");
                    try
                    {
                        if (File.Exists(tmp)) File.Delete(tmp);
                        using (ZipArchive archive = ZipFile.Open(tmp, ZipArchiveMode.Create))
                        {
                            AddDirectory(archive, src, profile, ref count, ref raw);
                            if (_s.IncludeRoot) AddRootFiles(archive, profile, ref count, ref raw);
                        }
                        if (File.Exists(zipPath)) File.Delete(zipPath);
                        File.Move(tmp, zipPath);
                    }
                    catch
                    {
                        try { if (File.Exists(tmp)) File.Delete(tmp); } catch { }
                        throw;
                    }

                    long zipBytes = new FileInfo(zipPath).Length;
                    WriteMeta(zipPath, profile, fp, count, raw);
                    _log("完成 " + profile + "：" + count + " 个文件，" + HumanSize(raw) + " → " + HumanSize(zipBytes) + "（" + zipName + "）");

                    result.Created++;
                    result.Bytes += zipBytes;
                    result.Zips.Add(zipPath);
                }
                catch (Exception ex)
                {
                    result.Errors.Add(profile + "：" + ex.Message);
                    _log("失败 " + profile + "：" + ex.Message);
                }
            }

            if (result.Created > 0) ApplyRetention();
            _log("备份结束：新建 " + result.Created + " 份，跳过 " + result.Skipped + " 份" +
                 (result.Errors.Count > 0 ? "，失败 " + result.Errors.Count + " 份" : ""));
            return result;
        }

        private static string BuildName(string profile, long stamp)
        {
            return profile + "--" + stamp + "----.zip";
        }

        private void AddDirectory(ZipArchive archive, string root, string prefix, ref int count, ref long raw)
        {
            List<string> files = Directory.GetFiles(root, "*", SearchOption.AllDirectories).ToList();
            files.Sort(StringComparer.OrdinalIgnoreCase);
            string rootFull = Path.GetFullPath(root).TrimEnd('\\') + "\\";
            foreach (string file in files)
            {
                string full = Path.GetFullPath(file);
                string rel = full.Substring(rootFull.Length).Replace('\\', '/');
                AddFile(archive, full, prefix + "/" + rel, ref count, ref raw);
            }
        }

        private void AddRootFiles(ZipArchive archive, string profile, ref int count, ref long raw)
        {
            string settings = Path.Combine(_s.Remote, "settings.save.backup");
            if (File.Exists(settings))
                AddFile(archive, settings, "settings.save.backup", ref count, ref raw);

            int id = GamePaths.ProfileNumber(profile);
            if (id > 0)
            {
                int schema = GamePaths.ReadSchemaVersion(_s.Remote);
                string json = "{\r\n  \"last_profile_id\": " + id + ",\r\n  \"schema_version\": " + schema + "\r\n}";
                AddText(archive, "profile.save", json, ref count, ref raw);
            }
        }

        private static void AddFile(ZipArchive archive, string file, string entryName, ref int count, ref long raw)
        {
            DateTime dt = File.GetLastWriteTime(file);
            if (dt.Year < 1980) dt = new DateTime(1980, 1, 1);
            ZipArchiveEntry entry = archive.CreateEntry(entryName, CompressionLevel.Optimal);
            entry.LastWriteTime = new DateTimeOffset(dt);
            using (Stream es = entry.Open())
            using (FileStream fs = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
            {
                fs.CopyTo(es);
                raw += fs.Length;
            }
            count++;
        }

        private static void AddText(ZipArchive archive, string entryName, string text, ref int count, ref long raw)
        {
            byte[] bytes = new UTF8Encoding(false).GetBytes(text);
            ZipArchiveEntry entry = archive.CreateEntry(entryName, CompressionLevel.Optimal);
            entry.LastWriteTime = DateTimeOffset.Now;
            using (Stream es = entry.Open()) es.Write(bytes, 0, bytes.Length);
            raw += bytes.Length;
            count++;
        }

        private string ComputeFingerprint(string profileDir, string profile)
        {
            StringBuilder sb = new StringBuilder();
            List<string> files = Directory.GetFiles(profileDir, "*", SearchOption.AllDirectories).ToList();
            files.Sort(StringComparer.OrdinalIgnoreCase);
            string rootFull = Path.GetFullPath(profileDir).TrimEnd('\\') + "\\";
            foreach (string file in files)
            {
                FileInfo fi = new FileInfo(file);
                string rel = Path.GetFullPath(file).Substring(rootFull.Length).Replace('\\', '/');
                sb.Append(profile).Append('/').Append(rel.ToLowerInvariant())
                  .Append('|').Append(fi.Length)
                  .Append('|').Append(fi.LastWriteTimeUtc.Ticks)
                  .Append('\n');
            }
            if (_s.IncludeRoot)
            {
                sb.Append("schema|").Append(GamePaths.ReadSchemaVersion(_s.Remote)).Append('\n');
                string settings = Path.Combine(_s.Remote, "settings.save.backup");
                if (File.Exists(settings))
                {
                    FileInfo fi = new FileInfo(settings);
                    sb.Append("settings.save.backup|").Append(fi.Length).Append('|').Append(fi.LastWriteTimeUtc.Ticks).Append('\n');
                }
            }
            using (SHA256 sha = SHA256.Create())
            {
                byte[] hash = sha.ComputeHash(Encoding.UTF8.GetBytes(sb.ToString()));
                StringBuilder hex = new StringBuilder(hash.Length * 2);
                foreach (byte b in hash) hex.Append(b.ToString("x2"));
                return hex.ToString();
            }
        }

        private void WriteMeta(string zipPath, string profile, string fp, int count, long raw)
        {
            try
            {
                string[] lines = new string[]
                {
                    "version=1",
                    "profile=" + profile,
                    "created=" + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"),
                    "source_remote=" + _s.Remote,
                    "fingerprint=" + fp,
                    "file_count=" + count,
                    "raw_bytes=" + raw,
                    "include_root=" + (_s.IncludeRoot ? "1" : "0"),
                    "game=" + GamePaths.GameName + " (" + GamePaths.AppId + ")"
                };
                File.WriteAllLines(zipPath + ".meta.txt", lines, new UTF8Encoding(false));
            }
            catch { }
        }

        private void ApplyRetention()
        {
            if (_s.Keep <= 0) return;
            try
            {
                List<BackupInfo> all = ListBackups();
                Dictionary<string, List<BackupInfo>> groups = new Dictionary<string, List<BackupInfo>>();
                foreach (BackupInfo bi in all)
                {
                    // 只清理本工具生成的备份，绝不删除小黑盒旧版 zip
                    if (!bi.HasMeta) continue;
                    if (!groups.ContainsKey(bi.Profile)) groups[bi.Profile] = new List<BackupInfo>();
                    groups[bi.Profile].Add(bi);
                }
                foreach (KeyValuePair<string, List<BackupInfo>> kv in groups)
                {
                    List<BackupInfo> list = kv.Value;
                    list.Sort(delegate(BackupInfo a, BackupInfo b) { return b.Time.CompareTo(a.Time); });
                    for (int i = _s.Keep; i < list.Count; i++)
                    {
                        try
                        {
                            File.Delete(list[i].ZipPath);
                            string meta = list[i].ZipPath + ".meta.txt";
                            if (File.Exists(meta)) File.Delete(meta);
                            _log("按保留份数删除旧备份：" + Path.GetFileName(list[i].ZipPath));
                        }
                        catch (Exception ex) { _log("删除旧备份失败：" + ex.Message); }
                    }
                }
            }
            catch (Exception ex) { _log("清理旧备份失败：" + ex.Message); }
        }

        public int Restore(string zipPath, bool preBackup, bool switchProfile, out string preDir)
        {
            preDir = "";
            if (!File.Exists(zipPath)) throw new Exception("备份文件不存在：" + zipPath);

            string remoteFull = Path.GetFullPath(_s.Remote).TrimEnd('\\');
            string remotePrefix = remoteFull + "\\";
            if (preBackup)
            {
                preDir = Path.Combine(_s.Output, "_restore_backup_" + DateTime.Now.ToString("yyyyMMdd_HHmmss"));
            }

            int restored = 0;
            using (ZipArchive archive = ZipFile.OpenRead(zipPath))
            {
                foreach (ZipArchiveEntry e in archive.Entries)
                {
                    string name = e.FullName.Replace('\\', '/');
                    if (name.EndsWith("/")) continue;

                    string[] parts = name.Split('/');
                    bool bad = name.StartsWith("/") || name.Contains(":") || parts.Any(delegate(string p) { return p == ".."; });
                    if (bad) throw new Exception("压缩包内存在非法路径：" + name);

                    string target = Path.GetFullPath(Path.Combine(remoteFull, name.Replace('/', '\\')));
                    if (!target.StartsWith(remotePrefix, StringComparison.OrdinalIgnoreCase))
                        throw new Exception("压缩包内路径越界：" + name);

                    if (preBackup && File.Exists(target))
                    {
                        string bak = Path.Combine(preDir, name.Replace('/', '\\'));
                        string bakDir = Path.GetDirectoryName(bak);
                        if (!Directory.Exists(bakDir)) Directory.CreateDirectory(bakDir);
                        File.Copy(target, bak, true);
                    }

                    string dir = Path.GetDirectoryName(target);
                    if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);
                    e.ExtractToFile(target, true);
                    try { File.SetLastWriteTime(target, e.LastWriteTime.LocalDateTime); } catch { }
                    restored++;
                }
            }

            if (switchProfile)
            {
                string profile = DetectProfileFromZip(zipPath);
                if (!string.IsNullOrEmpty(profile)) GamePaths.SetCurrentProfile(_s.Remote, profile);
            }

            _log("还原完成：" + restored + " 个文件，来自 " + Path.GetFileName(zipPath));
            if (!string.IsNullOrEmpty(preDir)) _log("还原前的旧文件已保存到：" + preDir);
            return restored;
        }

        public static string DetectProfileFromZip(string zipPath)
        {
            try
            {
                using (ZipArchive archive = ZipFile.OpenRead(zipPath))
                {
                    foreach (ZipArchiveEntry e in archive.Entries)
                    {
                        Match m = Regex.Match(e.FullName.Replace('\\', '/'), @"^(profile\d+)/");
                        if (m.Success) return m.Groups[1].Value;
                    }
                }
            }
            catch { }
            return "";
        }

        public static bool ZipHasProfileSave(string zipPath)
        {
            try
            {
                using (ZipArchive archive = ZipFile.OpenRead(zipPath))
                {
                    foreach (ZipArchiveEntry e in archive.Entries)
                    {
                        if (string.Equals(e.FullName.Replace('\\', '/'), "profile.save", StringComparison.OrdinalIgnoreCase))
                            return true;
                    }
                }
            }
            catch { }
            return false;
        }

        public static string HumanSize(long bytes)
        {
            if (bytes < 0) return "—";
            if (bytes < 1024) return bytes + " B";
            if (bytes < 1024 * 1024) return (bytes / 1024.0).ToString("0.0") + " KB";
            if (bytes < 1024L * 1024 * 1024) return (bytes / 1024.0 / 1024.0).ToString("0.0") + " MB";
            return (bytes / 1024.0 / 1024.0 / 1024.0).ToString("0.00") + " GB";
        }
    }

    internal static class Cli
    {
        public static int Run(string[] args)
        {
            Dictionary<string, string> opts = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            HashSet<string> flags = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            for (int i = 0; i < args.Length; i++)
            {
                string a = args[i];
                if (!a.StartsWith("--")) continue;
                string key = a.Substring(2);
                string val = null;
                if (i + 1 < args.Length && !args[i + 1].StartsWith("--")) { val = args[i + 1]; i++; }
                if (val == null) flags.Add(key);
                else opts[key] = val;
            }

            if (flags.Contains("help") || flags.Contains("h"))
            {
                Console.WriteLine("STS2 存档备份器（命令行）");
                Console.WriteLine("  --source <remote目录>   默认自动检测");
                Console.WriteLine("  --output <备份目录>     默认 文档\\STS2存档备份\\<账号>\\2868840");
                Console.WriteLine("  --all                   备份全部档案位（默认只备份当前档案）");
                Console.WriteLine("  --no-root               不写入 profile.save / settings.save.backup");
                Console.WriteLine("  --no-skip               内容未变化也强制新建备份");
                Console.WriteLine("  --keep <N>              每个档案保留最近 N 份，0=不限");
                Console.WriteLine("  --list                  列出备份");
                Console.WriteLine("  --restore <zip>         还原指定备份到 --source");
                Console.WriteLine("  --switch                还原后切换当前档案（仅 --restore）");
                return 0;
            }

            AppSettings s = new AppSettings();
            s.Remote = Get(opts, "source");
            if (string.IsNullOrEmpty(s.Remote)) s.Remote = GamePaths.DetectRemote();
            s.Output = Get(opts, "output");
            if (string.IsNullOrEmpty(s.Output)) s.Output = GamePaths.DefaultOutput(s.Remote);
            s.AllProfiles = flags.Contains("all");
            s.IncludeRoot = !flags.Contains("no-root");
            s.SkipUnchanged = !flags.Contains("no-skip");
            s.Keep = 30;
            string keep = Get(opts, "keep");
            if (!string.IsNullOrEmpty(keep)) { int n; if (int.TryParse(keep, out n)) s.Keep = n; }
            s.AutoOnExit = false;

            Console.WriteLine("source=" + s.Remote);
            Console.WriteLine("output=" + s.Output);

            BackupManager mgr = new BackupManager(s, delegate(string msg) { Console.WriteLine(msg); });

            if (flags.Contains("list"))
            {
                List<BackupInfo> list = mgr.ListBackups();
                Console.WriteLine("count=" + list.Count);
                foreach (BackupInfo bi in list)
                {
                    Console.WriteLine(bi.Time.ToString("yyyy-MM-dd HH:mm:ss") + "  " + bi.Profile + "  " +
                        BackupManager.HumanSize(bi.ZipBytes) + "  " + (bi.FileCount >= 0 ? bi.FileCount.ToString() : "?") + "  " + bi.FileName);
                }
                return 0;
            }

            string restore = Get(opts, "restore");
            if (!string.IsNullOrEmpty(restore))
            {
                try
                {
                    string pre;
                    int n = mgr.Restore(restore, true, flags.Contains("switch"), out pre);
                    Console.WriteLine("restored=" + n);
                    if (!string.IsNullOrEmpty(pre)) Console.WriteLine("pre_backup=" + pre);
                    return 0;
                }
                catch (Exception ex)
                {
                    Console.Error.WriteLine("restore failed: " + ex.Message);
                    return 1;
                }
            }

            try
            {
                BackupResult r = mgr.BackupAll("命令行");
                Console.WriteLine("created=" + r.Created + " skipped=" + r.Skipped + " errors=" + r.Errors.Count);
                foreach (string z in r.Zips) Console.WriteLine("zip=" + z);
                return r.Errors.Count == 0 ? 0 : 1;
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine("backup failed: " + ex.Message);
                return 1;
            }
        }

        private static string Get(Dictionary<string, string> d, string key)
        {
            string v;
            return d.TryGetValue(key, out v) ? v : "";
        }
    }

    internal sealed class MainForm : Form
    {
        private readonly AppSettings _settings;
        private readonly Timer _timer;
        private bool _busy;
        private bool _gameWasRunning;
        private DateTime _nextAuto = DateTime.MaxValue;
        private readonly object _logLock = new object();

        private TextBox _txtRemote;
        private TextBox _txtOutput;
        private Label _lblProfiles;
        private CheckBox _chkAll;
        private CheckBox _chkRoot;
        private CheckBox _chkSkip;
        private NumericUpDown _numKeep;
        private CheckBox _chkTimer;
        private NumericUpDown _numInterval;
        private CheckBox _chkOnExit;
        private ListView _list;
        private TextBox _txtLog;
        private ToolStripStatusLabel _lblStatus;
        private ToolStripProgressBar _progress;
        private GroupBox _grpPath;
        private GroupBox _grpOptions;
        private GroupBox _grpAuto;

        public MainForm()
        {
            _settings = AppSettings.Load();
            BuildUi();
            LoadSettingsToUi();
            _gameWasRunning = GamePaths.IsGameRunning();

            _timer = new Timer();
            _timer.Interval = 10000;
            _timer.Tick += TimerTick;
            _timer.Start();
            ResetNextAuto();
        }

        private void BuildUi()
        {
            Text = "STS2 存档备份器 - 旧版整包备份";
            StartPosition = FormStartPosition.CenterScreen;
            ClientSize = new Size(940, 820);
            MinimumSize = new Size(880, 720);
            Font = new Font("Microsoft YaHei UI", 9F, FontStyle.Regular, GraphicsUnit.Point);
            BackColor = SystemColors.Control;

            _grpPath = new GroupBox();
            _grpPath.Text = "1. 存档位置";
            _grpPath.Left = 12; _grpPath.Top = 10; _grpPath.Width = 916; _grpPath.Height = 86;
            _grpPath.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
            Controls.Add(_grpPath);

            Label lblRemote = new Label();
            lblRemote.Text = "Steam 存档：";
            lblRemote.Left = 14; lblRemote.Top = 27; lblRemote.Width = 78;
            _grpPath.Controls.Add(lblRemote);

            _txtRemote = new TextBox();
            _txtRemote.Left = 96; _txtRemote.Top = 24; _txtRemote.Width = 646;
            _txtRemote.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
            _grpPath.Controls.Add(_txtRemote);

            Button btnDetect = new Button();
            btnDetect.Text = "自动检测";
            btnDetect.Left = 750; btnDetect.Top = 22; btnDetect.Width = 74; btnDetect.Height = 26;
            btnDetect.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            btnDetect.Click += delegate { DetectRemote(); };
            _grpPath.Controls.Add(btnDetect);

            Button btnBrowse = new Button();
            btnBrowse.Text = "浏览...";
            btnBrowse.Left = 830; btnBrowse.Top = 22; btnBrowse.Width = 72; btnBrowse.Height = 26;
            btnBrowse.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            btnBrowse.Click += delegate { BrowseRemote(); };
            _grpPath.Controls.Add(btnBrowse);

            _lblProfiles = new Label();
            _lblProfiles.Left = 96; _lblProfiles.Top = 56; _lblProfiles.Width = 800; _lblProfiles.Height = 20;
            _lblProfiles.ForeColor = Color.DimGray;
            _lblProfiles.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
            _grpPath.Controls.Add(_lblProfiles);

            _grpOptions = new GroupBox();
            _grpOptions.Text = "2. 备份选项";
            _grpOptions.Left = 12; _grpOptions.Top = 102; _grpOptions.Width = 916; _grpOptions.Height = 132;
            _grpOptions.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
            Controls.Add(_grpOptions);

            _chkAll = new CheckBox();
            _chkAll.Text = "备份全部档案位";
            _chkAll.Left = 14; _chkAll.Top = 22; _chkAll.Width = 160;
            _grpOptions.Controls.Add(_chkAll);

            _chkRoot = new CheckBox();
            _chkRoot.Text = "包含 profile.save（指向该档案）";
            _chkRoot.Left = 190; _chkRoot.Top = 22; _chkRoot.Width = 300;
            _grpOptions.Controls.Add(_chkRoot);

            _chkSkip = new CheckBox();
            _chkSkip.Text = "内容没变就跳过";
            _chkSkip.Left = 510; _chkSkip.Top = 22; _chkSkip.Width = 180;
            _grpOptions.Controls.Add(_chkSkip);

            Label lblKeep = new Label();
            lblKeep.Text = "每个档案保留";
            lblKeep.Left = 14; lblKeep.Top = 58; lblKeep.Width = 92;
            _grpOptions.Controls.Add(lblKeep);

            _numKeep = new NumericUpDown();
            _numKeep.Left = 108; _numKeep.Top = 54; _numKeep.Width = 62; _numKeep.Minimum = 0; _numKeep.Maximum = 9999;
            _grpOptions.Controls.Add(_numKeep);

            Label lblKeep2 = new Label();
            lblKeep2.Text = "份（0 = 不限）";
            lblKeep2.Left = 176; lblKeep2.Top = 58; lblKeep2.Width = 110;
            _grpOptions.Controls.Add(lblKeep2);

            Button btnBackup = new Button();
            btnBackup.Text = "立即备份";
            btnBackup.Left = 756; btnBackup.Top = 48; btnBackup.Width = 146; btnBackup.Height = 38;
            btnBackup.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            btnBackup.Font = new Font("Microsoft YaHei UI", 10F, FontStyle.Bold, GraphicsUnit.Point);
            btnBackup.Click += delegate { StartBackup("手动"); };
            _grpOptions.Controls.Add(btnBackup);

            Label lblOut = new Label();
            lblOut.Text = "输出目录：";
            lblOut.Left = 14; lblOut.Top = 100; lblOut.Width = 70;
            _grpOptions.Controls.Add(lblOut);

            _txtOutput = new TextBox();
            _txtOutput.Left = 88; _txtOutput.Top = 96; _txtOutput.Width = 556;
            _txtOutput.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
            _grpOptions.Controls.Add(_txtOutput);

            Button btnOut = new Button();
            btnOut.Text = "更改...";
            btnOut.Left = 654; btnOut.Top = 94; btnOut.Width = 86; btnOut.Height = 26;
            btnOut.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            btnOut.Click += delegate { BrowseOutput(); };
            _grpOptions.Controls.Add(btnOut);

            Button btnOpenOut = new Button();
            btnOpenOut.Text = "打开目录";
            btnOpenOut.Left = 746; btnOpenOut.Top = 94; btnOpenOut.Width = 86; btnOpenOut.Height = 26;
            btnOpenOut.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            btnOpenOut.Click += delegate { OpenFolder(GetOutput()); };
            _grpOptions.Controls.Add(btnOpenOut);

            Button btnRefresh = new Button();
            btnRefresh.Text = "刷新";
            btnRefresh.Left = 838; btnRefresh.Top = 94; btnRefresh.Width = 64; btnRefresh.Height = 26;
            btnRefresh.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            btnRefresh.Click += delegate { RefreshList(); };
            _grpOptions.Controls.Add(btnRefresh);

            _grpAuto = new GroupBox();
            _grpAuto.Text = "3. 自动备份";
            _grpAuto.Left = 12; _grpAuto.Top = 240; _grpAuto.Width = 916; _grpAuto.Height = 82;
            _grpAuto.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
            Controls.Add(_grpAuto);

            _chkTimer = new CheckBox();
            _chkTimer.Text = "每隔";
            _chkTimer.Left = 14; _chkTimer.Top = 24; _chkTimer.Width = 58;
            _chkTimer.CheckedChanged += delegate { _numInterval.Enabled = _chkTimer.Checked; ResetNextAuto(); };
            _grpAuto.Controls.Add(_chkTimer);

            _numInterval = new NumericUpDown();
            _numInterval.Left = 74; _numInterval.Top = 22; _numInterval.Width = 58; _numInterval.Minimum = 1; _numInterval.Maximum = 1440; _numInterval.Value = 30;
            _numInterval.ValueChanged += delegate { ResetNextAuto(); };
            _grpAuto.Controls.Add(_numInterval);

            Label lblMin = new Label();
            lblMin.Text = "分钟自动备份";
            lblMin.Left = 136; lblMin.Top = 26; lblMin.Width = 100;
            _grpAuto.Controls.Add(lblMin);

            _chkOnExit = new CheckBox();
            _chkOnExit.Text = "游戏退出后自动备份";
            _chkOnExit.Left = 260; _chkOnExit.Top = 24; _chkOnExit.Width = 190;
            _grpAuto.Controls.Add(_chkOnExit);

            Label lblAutoHint = new Label();
            lblAutoHint.Text = "自动备份只在游戏未运行时执行；检测到游戏退出会补一次。手动备份不受限制。";
            lblAutoHint.Left = 14; lblAutoHint.Top = 54; lblAutoHint.Width = 780;
            lblAutoHint.ForeColor = Color.DimGray;
            _grpAuto.Controls.Add(lblAutoHint);

            GroupBox grpList = new GroupBox();
            grpList.Text = "4. 备份列表（双击一行即可还原）";
            grpList.Left = 12; grpList.Top = 328; grpList.Width = 916; grpList.Height = 318;
            grpList.Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right;
            Controls.Add(grpList);

            _list = new ListView();
            _list.Dock = DockStyle.Fill;
            _list.View = View.Details;
            _list.FullRowSelect = true;
            _list.GridLines = true;
            _list.HideSelection = false;
            _list.MultiSelect = false;
            _list.Columns.Add("备份时间", 140);
            _list.Columns.Add("档案", 70);
            _list.Columns.Add("压缩大小", 86, HorizontalAlignment.Right);
            _list.Columns.Add("原始大小", 86, HorizontalAlignment.Right);
            _list.Columns.Add("文件数", 62, HorizontalAlignment.Right);
            _list.Columns.Add("文件名", 420);
            _list.DoubleClick += delegate { RestoreSelected(); };
            grpList.Controls.Add(_list);

            Panel listButtons = new Panel();
            listButtons.Dock = DockStyle.Bottom;
            listButtons.Height = 42;
            grpList.Controls.Add(listButtons);

            Label lblHint = new Label();
            lblHint.Text = "列表包含本工具备份和小黑盒旧版整包 zip；无 meta 的旧包不显示明细";
            lblHint.Left = 8; lblHint.Top = 13; lblHint.Width = 500; lblHint.ForeColor = Color.DimGray;
            listButtons.Controls.Add(lblHint);

            Button btnRestore = new Button();
            btnRestore.Text = "还原所选";
            btnRestore.Width = 100; btnRestore.Height = 28; btnRestore.Top = 7;
            btnRestore.Left = listButtons.Width - 340;
            btnRestore.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            btnRestore.Click += delegate { RestoreSelected(); };
            listButtons.Controls.Add(btnRestore);

            Button btnDelete = new Button();
            btnDelete.Text = "删除所选";
            btnDelete.Width = 100; btnDelete.Height = 28; btnDelete.Top = 7;
            btnDelete.Left = listButtons.Width - 236;
            btnDelete.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            btnDelete.Click += delegate { DeleteSelected(); };
            listButtons.Controls.Add(btnDelete);

            Button btnShow = new Button();
            btnShow.Text = "打开所在文件夹";
            btnShow.Width = 122; btnShow.Height = 28; btnShow.Top = 7;
            btnShow.Left = listButtons.Width - 130;
            btnShow.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            btnShow.Click += delegate { ShowSelectedInExplorer(); };
            listButtons.Controls.Add(btnShow);

            GroupBox grpLog = new GroupBox();
            grpLog.Text = "日志";
            grpLog.Left = 12; grpLog.Top = 652; grpLog.Width = 916; grpLog.Height = 136;
            grpLog.Anchor = AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right;
            Controls.Add(grpLog);

            _txtLog = new TextBox();
            _txtLog.Multiline = true;
            _txtLog.ReadOnly = true;
            _txtLog.ScrollBars = ScrollBars.Vertical;
            _txtLog.Dock = DockStyle.Fill;
            _txtLog.BackColor = Color.White;
            _txtLog.Font = new Font("Consolas", 8.5F, FontStyle.Regular, GraphicsUnit.Point);
            grpLog.Controls.Add(_txtLog);

            StatusStrip status = new StatusStrip();
            _lblStatus = new ToolStripStatusLabel();
            _lblStatus.Spring = true;
            _lblStatus.TextAlign = ContentAlignment.MiddleLeft;
            _lblStatus.Text = "就绪";
            status.Items.Add(_lblStatus);

            _progress = new ToolStripProgressBar();
            _progress.Width = 160;
            _progress.Style = ProgressBarStyle.Marquee;
            _progress.Visible = false;
            status.Items.Add(_progress);
            Controls.Add(status);

            FormClosing += delegate { try { CollectSettings().Save(); } catch { } _timer.Stop(); };
            Shown += delegate
            {
                UpdateProfilesLabel();
                RefreshList();
                AppendLog("程序已启动。存档目录：" + GetRemote());
            };
        }

        private void LoadSettingsToUi()
        {
            _txtRemote.Text = _settings.Remote;
            if (string.IsNullOrEmpty(_txtRemote.Text)) _txtRemote.Text = GamePaths.DetectRemote();
            _txtOutput.Text = _settings.Output;
            if (string.IsNullOrEmpty(_txtOutput.Text)) _txtOutput.Text = GamePaths.DefaultOutput(_txtRemote.Text);
            _chkAll.Checked = _settings.AllProfiles;
            _chkRoot.Checked = _settings.IncludeRoot;
            _chkSkip.Checked = _settings.SkipUnchanged;
            _numKeep.Value = Math.Max(0, Math.Min(9999, _settings.Keep));
            _chkOnExit.Checked = _settings.AutoOnExit;
            _chkTimer.Checked = _settings.AutoMinutes > 0;
            _numInterval.Value = Math.Max(1, Math.Min(1440, _settings.AutoMinutes > 0 ? _settings.AutoMinutes : 30));
            _numInterval.Enabled = _chkTimer.Checked;
        }

        private AppSettings CollectSettings()
        {
            AppSettings s = new AppSettings();
            s.Remote = _txtRemote.Text.Trim();
            s.Output = _txtOutput.Text.Trim();
            s.AllProfiles = _chkAll.Checked;
            s.IncludeRoot = _chkRoot.Checked;
            s.SkipUnchanged = _chkSkip.Checked;
            s.Keep = (int)_numKeep.Value;
            s.AutoOnExit = _chkOnExit.Checked;
            s.AutoMinutes = _chkTimer.Checked ? (int)_numInterval.Value : 0;
            if (string.IsNullOrEmpty(s.Output)) s.Output = GamePaths.DefaultOutput(s.Remote);
            return s;
        }

        private string GetRemote() { return _txtRemote.Text.Trim(); }
        private string GetOutput() { return _txtOutput.Text.Trim(); }

        private void DetectRemote()
        {
            string p = GamePaths.DetectRemote();
            if (string.IsNullOrEmpty(p))
            {
                MessageBox.Show(this, "没有自动找到 Steam 存档目录，请手动选择 remote 文件夹。\n\n" +
                    @"一般在 C:\steam\userdata\<账号>\2868840\remote", "未找到", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            _txtRemote.Text = p;
            if (string.IsNullOrEmpty(_txtOutput.Text)) _txtOutput.Text = GamePaths.DefaultOutput(p);
            UpdateProfilesLabel();
            AppendLog("检测到存档目录：" + p);
        }

        private void BrowseRemote()
        {
            FolderBrowserDialog dlg = new FolderBrowserDialog();
            dlg.Description = "选择 2868840\\remote 文件夹";
            if (Directory.Exists(GetRemote())) dlg.SelectedPath = GetRemote();
            if (dlg.ShowDialog(this) == DialogResult.OK)
            {
                _txtRemote.Text = dlg.SelectedPath;
                if (string.IsNullOrEmpty(_txtOutput.Text)) _txtOutput.Text = GamePaths.DefaultOutput(dlg.SelectedPath);
                UpdateProfilesLabel();
            }
        }

        private void BrowseOutput()
        {
            FolderBrowserDialog dlg = new FolderBrowserDialog();
            dlg.Description = "选择备份输出目录";
            if (Directory.Exists(GetOutput())) dlg.SelectedPath = GetOutput();
            if (dlg.ShowDialog(this) == DialogResult.OK)
            {
                _txtOutput.Text = dlg.SelectedPath;
                RefreshList();
            }
        }

        private void UpdateProfilesLabel()
        {
            string remote = GetRemote();
            if (string.IsNullOrEmpty(remote) || !Directory.Exists(remote))
            {
                _lblProfiles.Text = "存档目录不存在";
                _lblProfiles.ForeColor = Color.Firebrick;
                return;
            }
            List<string> profiles = GamePaths.GetProfiles(remote);
            string current = GamePaths.GetCurrentProfile(remote);
            string running = GamePaths.IsGameRunning() ? "运行中" : "未运行";
            _lblProfiles.Text = "当前档案：" + (string.IsNullOrEmpty(current) ? "?" : current) +
                "    档案位：" + (profiles.Count == 0 ? "未找到" : string.Join("、", profiles.ToArray())) +
                "    游戏：" + running;
            _lblProfiles.ForeColor = profiles.Count == 0 ? Color.Firebrick : Color.DimGray;
        }

        private void SetBusy(bool busy)
        {
            _busy = busy;
            _grpPath.Enabled = !busy;
            _grpOptions.Enabled = !busy;
            _grpAuto.Enabled = !busy;
            _progress.Visible = busy;
            _lblStatus.Text = busy ? "正在备份，请稍候..." : "就绪";
            Cursor = busy ? Cursors.WaitCursor : Cursors.Default;
        }

        private void StartBackup(string trigger)
        {
            if (_busy) return;
            AppSettings s = CollectSettings();
            if (string.IsNullOrEmpty(s.Remote) || !Directory.Exists(s.Remote))
            {
                MessageBox.Show(this, "存档目录不存在，请先选择正确的 remote 文件夹。", "提示", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            if (GamePaths.IsGameRunning())
            {
                DialogResult r = MessageBox.Show(this,
                    "游戏正在运行。运行中打包有可能拿到写了一半的存档。\n\n建议退出游戏再备份，仍要继续吗？",
                    "游戏正在运行", MessageBoxButtons.YesNo, MessageBoxIcon.Warning, MessageBoxDefaultButton.Button2);
                if (r != DialogResult.Yes) return;
            }

            s.Save();
            _settings.Remote = s.Remote;
            _settings.Output = s.Output;
            _settings.Keep = s.Keep;

            SetBusy(true);
            AppendLog("========== 开始备份（" + trigger + "）==========");
            BackupManager mgr;
            try { mgr = new BackupManager(s, AppendLog); }
            catch (Exception ex)
            {
                SetBusy(false);
                MessageBox.Show(this, ex.Message, "无法备份", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }

            Task.Factory.StartNew(delegate
            {
                BackupResult result = null;
                Exception error = null;
                try { result = mgr.BackupAll(trigger); }
                catch (Exception ex) { error = ex; }

                BeginInvoke(new Action(delegate
                {
                    SetBusy(false);
                    UpdateProfilesLabel();
                    RefreshList();
                    if (error != null)
                    {
                        AppendLog("备份失败：" + error.Message);
                        MessageBox.Show(this, error.Message, "备份失败", MessageBoxButtons.OK, MessageBoxIcon.Error);
                    }
                    else if (result != null)
                    {
                        if (result.Created > 0)
                            _lblStatus.Text = "已新建 " + result.Created + " 份备份";
                        else
                            _lblStatus.Text = "没有新备份（内容未变化）";
                    }
                }));
            });
        }

        private void RefreshList()
        {
            if (_list == null) return;
            _list.BeginUpdate();
            try
            {
                _list.Items.Clear();
                AppSettings s = CollectSettings();
                if (string.IsNullOrEmpty(s.Output) || !Directory.Exists(s.Output)) return;
                BackupManager mgr = new BackupManager(s, delegate { });
                foreach (BackupInfo bi in mgr.ListBackups())
                {
                    ListViewItem item = new ListViewItem(bi.Time.ToString("yyyy-MM-dd HH:mm:ss"));
                    item.SubItems.Add(bi.Profile);
                    item.SubItems.Add(BackupManager.HumanSize(bi.ZipBytes));
                    item.SubItems.Add(bi.HasMeta ? BackupManager.HumanSize(bi.RawBytes) : "—");
                    item.SubItems.Add(bi.FileCount >= 0 ? bi.FileCount.ToString() : "—");
                    item.SubItems.Add(bi.FileName);
                    item.Tag = bi;
                    _list.Items.Add(item);
                }
            }
            catch (Exception ex) { AppendLog("刷新列表失败：" + ex.Message); }
            finally { _list.EndUpdate(); }
        }

        private BackupInfo GetSelected()
        {
            if (_list.SelectedItems.Count == 0) return null;
            return _list.SelectedItems[0].Tag as BackupInfo;
        }

        private void RestoreSelected()
        {
            if (_busy) return;
            BackupInfo bi = GetSelected();
            if (bi == null)
            {
                MessageBox.Show(this, "请先在列表里选择一份备份。", "提示", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            bool running = GamePaths.IsGameRunning();
            string msg = "将要把备份还原到：\n" + GetRemote() + "\n\n" +
                "备份：" + bi.FileName + "\n档案：" + bi.Profile + "\n时间：" + bi.Time.ToString("yyyy-MM-dd HH:mm:ss");
            if (running) msg += "\n\n警告：游戏正在运行，建议先退出游戏！";
            msg += "\n\n还原前会把将被覆盖的现有文件另存一份。继续吗？";
            if (MessageBox.Show(this, msg, "确认还原", MessageBoxButtons.YesNo, MessageBoxIcon.Warning,
                MessageBoxDefaultButton.Button2) != DialogResult.Yes) return;

            bool switchProfile = false;
            if (!BackupManager.ZipHasProfileSave(bi.ZipPath))
            {
                DialogResult r = MessageBox.Show(this,
                    "这份备份里没有 profile.save，还原后游戏可能还停在原来的档案位。\n\n是否顺便把当前档案切换到 " + bi.Profile + "？",
                    "切换档案", MessageBoxButtons.YesNo, MessageBoxIcon.Question, MessageBoxDefaultButton.Button1);
                switchProfile = (r == DialogResult.Yes);
            }

            AppSettings s = CollectSettings();
            s.Save();
            SetBusy(true);
            AppendLog("========== 开始还原 " + bi.FileName + " ==========");

            Task.Factory.StartNew(delegate
            {
                string pre = "";
                int count = 0;
                Exception error = null;
                try
                {
                    BackupManager mgr = new BackupManager(s, AppendLog);
                    count = mgr.Restore(bi.ZipPath, true, switchProfile, out pre);
                }
                catch (Exception ex) { error = ex; }

                BeginInvoke(new Action(delegate
                {
                    SetBusy(false);
                    UpdateProfilesLabel();
                    if (error != null)
                    {
                        AppendLog("还原失败：" + error.Message);
                        MessageBox.Show(this, error.Message, "还原失败", MessageBoxButtons.OK, MessageBoxIcon.Error);
                    }
                    else
                    {
                        _lblStatus.Text = "已还原 " + count + " 个文件";
                        MessageBox.Show(this, "还原完成，共 " + count + " 个文件。\n\n" +
                            (string.IsNullOrEmpty(pre) ? "" : "还原前的旧文件保存在：\n" + pre + "\n\n") +
                            "如果开着 Steam 云，启动游戏前请确认云端冲突选择的是本地文件。",
                            "完成", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    }
                }));
            });
        }

        private void DeleteSelected()
        {
            if (_busy) return;
            BackupInfo bi = GetSelected();
            if (bi == null) return;
            if (MessageBox.Show(this, "确定删除这份备份吗？\n\n" + bi.FileName, "删除备份",
                MessageBoxButtons.YesNo, MessageBoxIcon.Warning, MessageBoxDefaultButton.Button2) != DialogResult.Yes) return;
            try
            {
                File.Delete(bi.ZipPath);
                string meta = bi.ZipPath + ".meta.txt";
                if (File.Exists(meta)) File.Delete(meta);
                AppendLog("已删除：" + bi.FileName);
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, "删除失败：" + ex.Message, "错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            RefreshList();
        }

        private void ShowSelectedInExplorer()
        {
            BackupInfo bi = GetSelected();
            if (bi == null)
            {
                OpenFolder(GetOutput());
                return;
            }
            try { Process.Start("explorer.exe", "/select,\"" + bi.ZipPath + "\""); }
            catch { OpenFolder(GetOutput()); }
        }

        private void OpenFolder(string path)
        {
            try
            {
                if (string.IsNullOrEmpty(path)) return;
                if (!Directory.Exists(path)) Directory.CreateDirectory(path);
                Process.Start("explorer.exe", "\"" + path + "\"");
            }
            catch (Exception ex) { AppendLog("打开目录失败：" + ex.Message); }
        }

        private void ResetNextAuto()
        {
            if (_chkTimer != null && _chkTimer.Checked)
                _nextAuto = DateTime.Now.AddMinutes((double)_numInterval.Value);
            else
                _nextAuto = DateTime.MaxValue;
        }

        private void TimerTick(object sender, EventArgs e)
        {
            bool running = GamePaths.IsGameRunning();
            if (running != _gameWasRunning)
            {
                _gameWasRunning = running;
                UpdateProfilesLabel();
                if (!running && _chkOnExit.Checked && !_busy)
                {
                    AppendLog("检测到游戏已退出，自动备份。");
                    StartBackup("游戏退出");
                }
            }

            if (_chkTimer.Checked && !_busy && DateTime.Now >= _nextAuto)
            {
                _nextAuto = DateTime.Now.AddMinutes((double)_numInterval.Value);
                if (running)
                {
                    AppendLog("定时备份时间到，但游戏正在运行，跳过。");
                }
                else
                {
                    AppendLog("定时备份时间到。");
                    StartBackup("定时");
                }
            }
        }

        private void AppendLog(string message)
        {
            if (InvokeRequired)
            {
                BeginInvoke(new Action<string>(AppendLog), message);
                return;
            }
            string line = "[" + DateTime.Now.ToString("HH:mm:ss") + "] " + message;
            _txtLog.AppendText(line + Environment.NewLine);
            try
            {
                lock (_logLock)
                {
                    File.AppendAllText(AppSettings.LogFile,
                        "[" + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + "] " + message + Environment.NewLine,
                        new UTF8Encoding(false));
                }
            }
            catch { }
        }
    }
}
