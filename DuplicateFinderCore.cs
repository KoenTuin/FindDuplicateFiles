using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;

/// <summary>
/// Contains the core duplicate-finding logic shared by
/// <see cref="FastDuplicateCleaner"/> and the test suite.
/// This is the single authoritative implementation of every algorithmic
/// decision (hashing, grouping, original-selection, duplicate-handling).
/// </summary>
public static class DuplicateFinderCore
{
    /// <summary>
    /// Computes the full SHA-256 hash of a file using a 1 MB read buffer.
    /// The optional <paramref name="bytesHashedCallback"/> is invoked after
    /// each buffer read with the number of bytes just processed — use this
    /// for progress reporting in the CLI.
    /// </summary>
    public static string ComputeHash(string path, Action<long>? bytesHashedCallback = null)
    {
        using (var sha = SHA256.Create())
        using (var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read))
        {
            byte[] buffer = new byte[1024 * 1024];
            int read;
            while ((read = fs.Read(buffer, 0, buffer.Length)) > 0)
            {
                sha.TransformBlock(buffer, 0, read, null, 0);
                bytesHashedCallback?.Invoke(read);
            }
            sha.TransformFinalBlock(Array.Empty<byte>(), 0, 0);
            return BitConverter.ToString(sha.Hash!).Replace("-", "").ToLower();
        }
    }

    /// <summary>
    /// Groups <paramref name="files"/> into sets of duplicates using a two-step
    /// algorithm:
    /// 1. Group by file size (only sizes with 2+ files are candidates).
    /// 2. Within each size-group, compute SHA-256; files that share the same
    ///    hash are duplicates.
    /// Returns only groups that contain 2 or more files.
    /// </summary>
    public static IReadOnlyList<IReadOnlyList<string>> FindDuplicateGroups(IEnumerable<string> files)
    {
        var sizeGroups = files
            .GroupBy(f => new FileInfo(f).Length)
            .Where(g => g.Count() > 1);

        var hashGroups = new Dictionary<string, List<string>>();
        foreach (var sizeGroup in sizeGroups)
        {
            foreach (var file in sizeGroup)
            {
                string hash = ComputeHash(file);
                if (!hashGroups.TryGetValue(hash, out var list))
                {
                    list = new List<string>();
                    hashGroups[hash] = list;
                }
                list.Add(file);
            }
        }

        return hashGroups
            .Values
            .Where(g => g.Count > 1)
            .Select(g => (IReadOnlyList<string>)g.AsReadOnly())
            .ToList();
    }

    /// <summary>
    /// Given a group of duplicate files, returns the file to keep as the
    /// "original": the entry with the <b>shortest filename</b> (not full path).
    /// Ties are broken by the stable ordering already present in the list.
    /// </summary>
    public static string SelectOriginal(IEnumerable<string> duplicateGroup)
    {
        return duplicateGroup.OrderBy(f => Path.GetFileName(f).Length).First();
    }

    /// <summary>
    /// Handles a duplicate file according to <paramref name="mode"/>:
    /// <list type="bullet">
    ///   <item><term>dry</term><description>logs the path but takes no action.</description></item>
    ///   <item><term>delete</term><description>permanently removes the file.</description></item>
    ///   <item><term>move</term><description>moves the file to <paramref name="moveFolder"/>,
    ///     appending a counter suffix when a name collision occurs.</description></item>
    /// </list>
    /// </summary>
    public static void HandleDuplicate(string filepath, string mode, string? moveFolder)
    {
        if (string.IsNullOrWhiteSpace(mode))
        {
            Console.WriteLine($"Error: Mode is not specified. Skipping {filepath}");
            return;
        }

        if (mode == "dry")
        {
            Console.WriteLine($"[DRY RUN] Duplicate: {filepath}");
        }
        else if (mode == "delete")
        {
            Console.WriteLine($"Deleting: {filepath}");
            try { File.Delete(filepath); }
            catch (Exception ex) { Console.WriteLine($"Failed to delete {filepath}: {ex.Message}"); }
        }
        else if (mode == "move")
        {
            if (string.IsNullOrWhiteSpace(moveFolder))
            {
                Console.WriteLine($"Error: Move folder is not specified. Cannot move {filepath}");
                return;
            }

            try
            {
                Directory.CreateDirectory(moveFolder);
                string dest = Path.Combine(moveFolder, Path.GetFileName(filepath));

                int counter = 1;
                string baseName = Path.Combine(moveFolder, Path.GetFileNameWithoutExtension(filepath));
                string ext = Path.GetExtension(filepath);

                while (File.Exists(dest))
                {
                    dest = $"{baseName} ({counter}){ext}";
                    counter++;
                }

                Console.WriteLine($"Moving: {filepath} → {dest}");
                File.Move(filepath, dest);
            }
            catch (Exception ex) { Console.WriteLine($"Failed to move {filepath}: {ex.Message}"); }
        }
        else
        {
            Console.WriteLine($"Unknown mode '{mode}'. Skipping {filepath}");
        }
    }
}
