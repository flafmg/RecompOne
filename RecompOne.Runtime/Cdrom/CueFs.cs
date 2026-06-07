namespace RecompOne.Runtime.Cdrom;

public sealed class CueFs : IDisposable
{
    private record Entry(int Lba, uint Size, bool IsDir, string Name);

    private readonly CueBin _bin;

    private CueFs(CueBin bin) => _bin = bin;

    public static CueFs Open(string cuePath) => new(CueBin.Open(cuePath));

    public byte[] ReadFile(string path)
    {
        path = path.TrimStart('/', '\\');
        var parts = path.Split('/', '\\');
        var dir = Root();
        for (int i = 0; i < parts.Length - 1; i++)
            dir = Find(dir, StripVersion(parts[i]), true);
        var file = Find(dir, StripVersion(parts[^1]), false);
        return ReadExtent(file.Lba, (int)file.Size);
    }

    private static string StripVersion(string name)
    {
        int semi = name.IndexOf(';');
        return semi >= 0 ? name[..semi] : name;
    }

    public bool Exists(string path)
    {
        try { ReadFile(path); return true; }
        catch { return false; }
    }

    public string? FindFile(string name) => Search(Root(), "", name.ToUpperInvariant());

    public bool Locate(string name, out int lba, out uint size)
    {
        lba = 0;
        size = 0;
        var entry = LocateEntry(name);
        if (entry == null) return false;
        lba = entry.Lba;
        size = entry.Size;
        return true;
    }

    private Entry? LocateEntry(string name)
    {
        name = name.TrimStart('/', '\\');
        try
        {
            var parts = name.Split('/', '\\');
            var dir = Root();
            for (int i = 0; i < parts.Length - 1; i++)
                dir = Find(dir, StripVersion(parts[i]), true);
            return Find(dir, StripVersion(parts[^1]), false);
        }
        catch (FileNotFoundException) { }

        int slash = name.LastIndexOfAny(['/', '\\']);
        var basename = slash >= 0 ? name[(slash + 1)..] : name;
        return SearchEntry(Root(), StripVersion(basename).ToUpperInvariant());
    }

    private Entry? SearchEntry(Entry dir, string name)
    {
        foreach (var e in Entries(dir))
        {
            if (e.IsDir)
            {
                var found = SearchEntry(e, name);
                if (found != null) return found;
            }
            else if (e.Name.Equals(name, StringComparison.OrdinalIgnoreCase))
                return e;
        }
        return null;
    }

    public byte[] ReadSector(int lba) => _bin.ReadSector(lba);

    public byte[] ReadSectorData(int lba, int size) => _bin.ReadSectorData(lba, size);

    public byte[] ReadSectors(int lba, int size) => ReadExtent(lba, size);

    private string? Search(Entry dir, string basePath, string name)
    {
        foreach (var e in Entries(dir))
        {
            if (e.IsDir)
            {
                var p = basePath.Length > 0 ? basePath + "/" + e.Name : e.Name;
                var found = Search(e, p, name);
                if (found != null) return found;
            }
            else if (e.Name.Equals(name, StringComparison.OrdinalIgnoreCase))
                return basePath.Length > 0 ? basePath + "/" + e.Name : e.Name;
        }
        return null;
    }

    private Entry Root()
    {
        var pvd = _bin.ReadSector(16);
        return ParseEntry(pvd, 156);
    }

    private Entry Find(Entry dir, string name, bool wantDir)
    {
        string upper = name.ToUpperInvariant();
        foreach (var e in Entries(dir))
            if (e.IsDir == wantDir && e.Name.Equals(upper, StringComparison.OrdinalIgnoreCase))
                return e;
        throw new FileNotFoundException($"{(wantDir ? "directory" : "File")} not found: {name}");
    }

    private IEnumerable<Entry> Entries(Entry dir)
    {
        var data = ReadExtent(dir.Lba, (int)dir.Size);
        int i = 0;
        while (i < data.Length)
        {
            byte len = data[i];
            if (len == 0) { i = (i / 2048 + 1) * 2048; continue; }
            var e = ParseEntry(data, i);
            if (e.Name is not ("\x00" or "\x01"))
                yield return e;
            i += len;
        }
    }

    private byte[] ReadExtent(int lba, int size)
    {
        var result = new byte[size];
        int done = 0;
        int cur = lba;
        while (done < size)
        {
            var sector = _bin.ReadSector(cur++);
            int n = Math.Min(2048, size - done);
            sector.AsSpan(0, n).CopyTo(result.AsSpan(done));
            done += n;
        }
        return result;
    }

    private static Entry ParseEntry(byte[] data, int off)
    {
        int lba = BitConverter.ToInt32(data, off + 2);
        uint size = BitConverter.ToUInt32(data, off + 10);
        bool isDir = (data[off + 25] & 0x02) != 0;
        int nameLen = data[off + 32];
        string raw = System.Text.Encoding.ASCII.GetString(data, off + 33, nameLen);
        int semi = raw.IndexOf(';');
        return new Entry(lba, size, isDir, semi >= 0 ? raw[..semi] : raw);
    }

    public void Dispose() => _bin.Dispose();
}
