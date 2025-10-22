using System.Diagnostics;
using ContentEditor;

namespace ContentPatcher.FileFormats;

public sealed class IniFile : IDisposable
{
    private StreamReader? reader;

    public IniFile() { }
    public IniFile(string filepath) => Open(filepath);

    public void Open(string filepath) => Open(File.OpenRead(filepath));
    public void Open(FileStream stream)
    {
        Close();
        reader = new StreamReader(stream);
    }
    public void Close()
    {
        reader?.Dispose();
    }

    public IEnumerable<(string key, string value, string? group)> ReadEntries()
    {
        Debug.Assert(reader != null);
        int lineCount = -1;
        string? group = null;
        while (!reader.EndOfStream) {
            lineCount++;
            var line = reader.ReadLine().AsSpan().TrimStart();

            if (line.IsEmpty || line[0] == '#' || line[0] == ';') continue;
            if (line[0] == '[') {
                var end = line.IndexOf(']');
                if (end == -1) Logger.Error($"Invalid ini group at line {lineCount}: {line}");
                group = line[1..end].ToString();
                continue;
            }

            var eq = line.IndexOf('=');
            if (eq == -1) {
                Logger.Error($"Invalid ini entry at line {lineCount}: {line}");
                continue;
            }

            var key = line[..eq].TrimEnd();
            var value = line[(eq+1)..].TrimStart();
            yield return (key.ToString(), value.ToString(), group);
        }
    }

    public static IEnumerable<(string key, string value, string? group)> ReadFile(string filepath)
    {
        if (!File.Exists(filepath)) {
            return Enumerable.Empty<(string key, string value, string? group)>();
        }

        using var ini = new IniFile(filepath);
        return ini.ReadEntries().ToList();
    }

    public static IEnumerable<KeyValuePair<string, string>> ReadFileIgnoreKeyCasing(string filepath)
    {
        if (!File.Exists(filepath)) {
            yield break;
        }

        using var ini = new IniFile(filepath);
        foreach (var (key, value, _) in ini.ReadEntries()) {
            yield return new KeyValuePair<string, string>(key.ToLowerInvariant().Replace(" ", "").Replace("_", ""), value);
        }
    }

    public void Dispose() => Close();

    public static void WriteToFile(string iniFilepath, params KeyValuePair<string, string>[] values)
    {
        using var fs = new StreamWriter(File.Create(iniFilepath));
        foreach (var pair in values) {
            fs.WriteLine(pair.Key + " = " + pair.Value);
        }
    }

    public static void WriteToFile(string iniFilepath, IEnumerable<(string key, string value, string? group)> values)
    {
        var tempFilepath = iniFilepath + ".tmp";
        using var fs = new StreamWriter(File.Create(tempFilepath));

        string? lastGroup = null;
        foreach (var (key, value, group) in values.OrderBy(g => g.group)) {
            if (lastGroup != group) {
                fs.WriteLine();
                fs.WriteLine($"[{group}]");
                lastGroup = group;
            }

            fs.WriteLine(key + " = " + value);
        }
        fs.Dispose();
        File.Replace(tempFilepath, iniFilepath, iniFilepath + ".bak");
        File.Delete(iniFilepath + ".bak");
    }
}