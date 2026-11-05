using System.IO.Compression;

namespace AIStoryBuildersV2.Shared.Stories.Storage;

/// <summary>
/// Zip extraction entry by entry with a Zip Slip guard (D22). Never ZipFile.ExtractToDirectory, which aborts the
/// WASM runtime. Every entry path is validated before anything is written, so a bad entry rejects the whole archive.
/// </summary>
public static class SafeZip
{
    public sealed class UnsafeEntryException(string entry) : IOException($"The file contains an unsafe path: {entry}")
    {
        public string Entry { get; } = entry;
    }

    /// <summary>
    /// Normalizes an entry name to a relative path with '/' separators. Returns null for a rooted path, a drive letter,
    /// a '..' segment or an empty name.
    /// </summary>
    public static string? NormalizeEntryName(string entryName)
    {
        var name = entryName.Replace('\\', '/');
        if (name.Length == 0 || name.StartsWith('/') || name.Contains(':')) return null;
        var segments = name.Split('/');
        if (segments.Any(s => s == "..")) return null;
        var kept = segments.Where(s => s.Length > 0 && s != ".").ToArray();
        return kept.Length == 0 ? null : string.Join('/', kept);
    }

    /// <summary>The file entries of the archive with normalized names (directory entries are skipped).</summary>
    /// <exception cref="UnsafeEntryException">When any entry path is unsafe.</exception>
    public static List<(string Path, ZipArchiveEntry Entry)> ValidatedEntries(ZipArchive archive)
    {
        var result = new List<(string, ZipArchiveEntry)>();
        foreach (var entry in archive.Entries)
        {
            if (entry.FullName.EndsWith('/') || entry.FullName.EndsWith('\\'))
            {
                if (NormalizeEntryName(entry.FullName.TrimEnd('/', '\\')) is null && entry.FullName.Trim('/', '\\').Length > 0)
                    throw new UnsafeEntryException(entry.FullName);
                continue;
            }
            var path = NormalizeEntryName(entry.FullName) ?? throw new UnsafeEntryException(entry.FullName);
            result.Add((path, entry));
        }
        return result;
    }

    /// <summary>Extracts the validated entries under <paramref name="targetFolder"/>, skipping those <paramref name="skip"/> rejects.</summary>
    public static void ExtractTo(IEnumerable<(string Path, ZipArchiveEntry Entry)> entries, string targetFolder, Func<string, bool>? skip = null)
    {
        var root = Path.GetFullPath(targetFolder);
        foreach (var (path, entry) in entries)
        {
            if (skip?.Invoke(path) == true) continue;
            var full = Path.GetFullPath(Path.Combine(root, path.Replace('/', Path.DirectorySeparatorChar)));
            if (!full.StartsWith(root.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar, StringComparison.Ordinal))
                throw new UnsafeEntryException(entry.FullName);
            Directory.CreateDirectory(Path.GetDirectoryName(full)!);
            using var source = entry.Open();
            using var target = File.Create(full);
            source.CopyTo(target);
        }
    }

    /// <summary>Zips every file under <paramref name="folder"/> in memory, with '/' separators and no temp files.</summary>
    public static byte[] ZipFolder(string folder, Func<string, bool>? skip = null)
    {
        using var buffer = new MemoryStream();
        using (var archive = new ZipArchive(buffer, ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach (var file in Directory.GetFiles(folder, "*", SearchOption.AllDirectories).OrderBy(f => f, StringComparer.Ordinal))
            {
                var relative = Path.GetRelativePath(folder, file).Replace('\\', '/');
                if (skip?.Invoke(relative) == true) continue;
                var entry = archive.CreateEntry(relative, CompressionLevel.Optimal);
                using var target = entry.Open();
                using var source = File.OpenRead(file);
                source.CopyTo(target);
            }
        }
        return buffer.ToArray();
    }
}
