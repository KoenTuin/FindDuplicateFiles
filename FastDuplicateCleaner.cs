using System;
using System.IO;
using System.Linq;
using System.Collections.Concurrent;
using System.Threading.Tasks;

class FastDuplicateCleaner
{
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
                    string hash = DuplicateFinderCore.ComputeHash(file, bytesRead =>
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
            var original = DuplicateFinderCore.SelectOriginal(group.Value);
            var duplicates = group.Value.Where(f => f != original);

            foreach (var dup in duplicates)
            {
                DuplicateFinderCore.HandleDuplicate(dup, mode, moveFolder);
            }
        }

        Console.WriteLine("\nDone!");
    }
}

