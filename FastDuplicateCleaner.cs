using System;
using System.IO;
using System.Linq;
using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Threading.Tasks;

class FastDuplicateCleaner
{
    static readonly object logLock = new object();
    static string logFilePath;

    static void LogAction(string action, string original, string destination)
    {
        if (string.IsNullOrWhiteSpace(logFilePath)) return;

        string line = string.Join(",",
            DateTime.Now.ToString("s"),
            action,
            CsvEscape(original),
            CsvEscape(destination ?? string.Empty));

        lock (logLock)
        {
            File.AppendAllText(logFilePath, line + Environment.NewLine);
        }
    }

    static string CsvEscape(string value)
    {
        if (value.Contains(',') || value.Contains('"') || value.Contains('\n'))
        {
            return "\"" + value.Replace("\"", "\"\"") + "\"";
        }
        return value;
    }

    // Full SHA256 content hash with progress reporting
    static string FullHash(string path, Action<long> bytesHashedCallback = null)
    {
        using (var sha = SHA256.Create())
        using (var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read))
        {
            byte[] buffer = new byte[1024 * 1024]; // 1 MB buffer
            int read;
            while ((read = fs.Read(buffer, 0, buffer.Length)) > 0)
            {
                sha.TransformBlock(buffer, 0, read, null, 0);
                bytesHashedCallback?.Invoke(read);
            }
            sha.TransformFinalBlock(Array.Empty<byte>(), 0, 0);
            return BitConverter.ToString(sha.Hash).Replace("-", "").ToLower();
        }
    }

    static void HandleDuplicate(string filepath, string mode, string moveFolder, string targetFolder)
    {
        if (string.IsNullOrWhiteSpace(mode))
        {
            Console.WriteLine($"Error: Mode is not specified. Skipping {filepath}");
            return;
        }

        if (mode == "dry")
        {
            Console.WriteLine($"[DRY RUN] Duplicate: {filepath}");
            LogAction("dry-run", filepath, null);
        }
        else if (mode == "delete")
        {
            Console.WriteLine($"Deleting: {filepath}");
            try
            {
                File.Delete(filepath);
                LogAction("delete", filepath, null);
            }
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
                // Preserve the file's original subfolder structure (relative to the
                // scanned target folder) inside the duplicates folder, so the origin
                // location is never lost just because the file was moved.
                string relativeDir = Path.GetDirectoryName(Path.GetRelativePath(targetFolder, filepath)) ?? string.Empty;
                string destDir = Path.Combine(moveFolder, relativeDir);
                Directory.CreateDirectory(destDir);

                string dest = Path.Combine(destDir, Path.GetFileName(filepath));

                int counter = 1;
                string baseName = Path.Combine(destDir, Path.GetFileNameWithoutExtension(filepath));
                string ext = Path.GetExtension(filepath);

                while (File.Exists(dest))
                {
                    dest = $"{baseName} ({counter}){ext}";
                    counter++;
                }

                Console.WriteLine($"Moving: {filepath} → {dest}");
                File.Move(filepath, dest);
                LogAction("move", filepath, dest);
            }
            catch (Exception ex) { Console.WriteLine($"Failed to move {filepath}: {ex.Message}"); }
        }
        else
        {
            Console.WriteLine($"Unknown mode '{mode}'. Skipping {filepath}");
        }
    }

    static void Main(string[] args)
    {
        string targetFolder = args.Length > 1 ? args[1] : null;
        string mode = args.Length > 0 ? args[0]?.ToLower() : null;
        string moveFolder = args.Length > 2 ? args[2] : null;

        if (string.IsNullOrWhiteSpace(targetFolder))
        {
            Console.Write("Enter folder to scan: ");
            targetFolder = Console.ReadLine() ?? string.Empty;
        }

        if (string.IsNullOrWhiteSpace(mode))
        {
            Console.WriteLine("\nSelect mode: dry / delete / move");
            mode = (Console.ReadLine() ?? string.Empty).ToLower();
        }

        if (mode == "move" && string.IsNullOrWhiteSpace(moveFolder))
        {
            Console.Write("Enter folder where duplicates should be moved: ");
            moveFolder = Console.ReadLine() ?? string.Empty;
        }

        if (string.IsNullOrWhiteSpace(targetFolder) || !Directory.Exists(targetFolder))
        {
            Console.WriteLine("Error: Target folder path is invalid.");
            return;
        }

        // Persist an action log to disk so the origin of every moved/deleted file is
        // recoverable even after the terminal window is closed.
        logFilePath = Path.Combine(targetFolder, $"FastDuplicateCleaner_log_{DateTime.Now:yyyyMMdd_HHmmss}.csv");
        File.WriteAllText(logFilePath, "Timestamp,Action,OriginalPath,DestinationPath" + Environment.NewLine);
        Console.WriteLine($"Logging actions to: {logFilePath}");

        Console.WriteLine("\nScanning files...");

        var files = Directory.EnumerateFiles(targetFolder, "*", SearchOption.AllDirectories).ToList();

        // STEP 1 — group by file size
        var groupsBySize = files
            .GroupBy(f => new FileInfo(f).Length)
            .Where(g => g.Count() > 1)
            .ToList();

        Console.WriteLine($"Potential duplicate size groups: {groupsBySize.Count}");

        // STEP 2 — full SHA256 hash only for files in size groups with 2+ files
        var finalHashGroups = new ConcurrentDictionary<string, ConcurrentBag<string>>();

        long totalBytes = groupsBySize.Sum(g => g.Sum(f => new FileInfo(f).Length));
        long processedBytes = 0;

        Parallel.ForEach(groupsBySize, group =>
        {
            foreach (string file in group)
            {
                try
                {
                    string hash = FullHash(file, bytesRead =>
                    {
                        System.Threading.Interlocked.Add(ref processedBytes, bytesRead);

                        double percent = (double)processedBytes / totalBytes * 100;
                        Console.Write($"\rHashing progress: {percent:F2}%   ");
                    });

                    finalHashGroups.AddOrUpdate(
                        hash,
                        _ => new ConcurrentBag<string> { file },
                        (_, bag) => { bag.Add(file); return bag; }
                    );
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"\nSkipping file for hashing {file}: {ex.Message}");
                }
            }
        });

        Console.WriteLine("\n\nProcessing duplicates...\n");

        foreach (var group in finalHashGroups.Where(g => g.Value.Count > 1))
        {
            // Keep the file with the shortest filename as the "original"
            var sorted = group.Value.OrderBy(f => Path.GetFileName(f).Length).ToList();

            var original = sorted.First();       // keep this
            var duplicates = sorted.Skip(1);     // everything else is duplicate

            foreach (var dup in duplicates)
            {
                HandleDuplicate(dup, mode, moveFolder, targetFolder);
            }
        }

        Console.WriteLine("\nDone!");
    }
}
