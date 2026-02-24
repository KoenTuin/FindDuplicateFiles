using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Xunit;

/// <summary>
/// Tests for <see cref="DuplicateFinderCore"/>.
/// Each test corresponds directly to a decision encoded in <see cref="DuplicateFinderCore"/>.
/// </summary>
public class DuplicateFinderCoreTests : IDisposable
{
    // Temporary directory created fresh for every test.
    private readonly string _tempDir;

    public DuplicateFinderCoreTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, recursive: true);
    }

    // ---------------------------------------------------------------
    // Helpers
    // ---------------------------------------------------------------

    private string CreateFile(string name, string content)
    {
        string path = Path.Combine(_tempDir, name);
        File.WriteAllText(path, content);
        return path;
    }

    private string CreateFile(string subDir, string name, string content)
    {
        string dir = Path.Combine(_tempDir, subDir);
        Directory.CreateDirectory(dir);
        string path = Path.Combine(dir, name);
        File.WriteAllText(path, content);
        return path;
    }

    // ---------------------------------------------------------------
    // ComputeHash
    // ---------------------------------------------------------------

    [Fact]
    public void ComputeHash_SameContent_ReturnsSameHash()
    {
        string a = CreateFile("a.txt", "hello");
        string b = CreateFile("b.txt", "hello");

        Assert.Equal(DuplicateFinderCore.ComputeHash(a), DuplicateFinderCore.ComputeHash(b));
    }

    [Fact]
    public void ComputeHash_DifferentContent_ReturnsDifferentHash()
    {
        string a = CreateFile("a.txt", "hello");
        string b = CreateFile("b.txt", "world");

        Assert.NotEqual(DuplicateFinderCore.ComputeHash(a), DuplicateFinderCore.ComputeHash(b));
    }

    [Fact]
    public void ComputeHash_EmptyFile_ReturnsConsistentHash()
    {
        string a = CreateFile("empty1.txt", "");
        string b = CreateFile("empty2.txt", "");

        string hashA = DuplicateFinderCore.ComputeHash(a);
        string hashB = DuplicateFinderCore.ComputeHash(b);

        Assert.NotEmpty(hashA);
        Assert.Equal(hashA, hashB);
    }

    [Fact]
    public void ComputeHash_ReturnsLowercaseHexString()
    {
        string a = CreateFile("hex.txt", "test");
        string hash = DuplicateFinderCore.ComputeHash(a);

        Assert.Matches("^[0-9a-f]+$", hash);
    }

    // ---------------------------------------------------------------
    // FindDuplicateGroups
    // ---------------------------------------------------------------

    [Fact]
    public void FindDuplicateGroups_NoDuplicates_ReturnsEmpty()
    {
        string a = CreateFile("a.txt", "aaa");
        string b = CreateFile("b.txt", "bbb");

        var groups = DuplicateFinderCore.FindDuplicateGroups(new[] { a, b });

        Assert.Empty(groups);
    }

    [Fact]
    public void FindDuplicateGroups_SingleFile_ReturnsEmpty()
    {
        string a = CreateFile("a.txt", "only");

        var groups = DuplicateFinderCore.FindDuplicateGroups(new[] { a });

        Assert.Empty(groups);
    }

    [Fact]
    public void FindDuplicateGroups_EmptyList_ReturnsEmpty()
    {
        var groups = DuplicateFinderCore.FindDuplicateGroups(Array.Empty<string>());

        Assert.Empty(groups);
    }

    [Fact]
    public void FindDuplicateGroups_TwoIdenticalFiles_ReturnsOneGroup()
    {
        string a = CreateFile("a.txt", "same content");
        string b = CreateFile("b.txt", "same content");

        var groups = DuplicateFinderCore.FindDuplicateGroups(new[] { a, b });

        Assert.Single(groups);
        Assert.Contains(a, groups[0]);
        Assert.Contains(b, groups[0]);
    }

    [Fact]
    public void FindDuplicateGroups_ThreeIdenticalFiles_ReturnsSingleGroupWithAllThree()
    {
        string a = CreateFile("a.txt", "triplicate");
        string b = CreateFile("b.txt", "triplicate");
        string c = CreateFile("c.txt", "triplicate");

        var groups = DuplicateFinderCore.FindDuplicateGroups(new[] { a, b, c });

        Assert.Single(groups);
        Assert.Equal(3, groups[0].Count);
    }

    [Fact]
    public void FindDuplicateGroups_TwoPairsOfDuplicates_ReturnsTwoGroups()
    {
        string a1 = CreateFile("a1.txt", "group-a");
        string a2 = CreateFile("a2.txt", "group-a");
        string b1 = CreateFile("b1.txt", "group-b");
        string b2 = CreateFile("b2.txt", "group-b");

        var groups = DuplicateFinderCore.FindDuplicateGroups(new[] { a1, a2, b1, b2 });

        Assert.Equal(2, groups.Count);
    }

    /// <summary>
    /// Step 1 of the algorithm: files are first grouped by SIZE.
    /// Two files with the same byte count but different content must NOT be
    /// reported as duplicates — they differ in hash (Step 2).
    /// </summary>
    [Fact]
    public void FindDuplicateGroups_SameSizeDifferentContent_NotDuplicate()
    {
        // Ensure identical length but different bytes.
        string a = CreateFile("a.txt", "AAAA");
        string b = CreateFile("b.txt", "BBBB");

        Assert.Equal(new FileInfo(a).Length, new FileInfo(b).Length);

        var groups = DuplicateFinderCore.FindDuplicateGroups(new[] { a, b });

        Assert.Empty(groups);
    }

    [Fact]
    public void FindDuplicateGroups_DifferentSizes_NotDuplicate()
    {
        string a = CreateFile("small.txt", "hi");
        string b = CreateFile("large.txt", "hello world");

        var groups = DuplicateFinderCore.FindDuplicateGroups(new[] { a, b });

        Assert.Empty(groups);
    }

    [Fact]
    public void FindDuplicateGroups_DuplicatesInSubdirectories_Detected()
    {
        string a = CreateFile("sub1", "file.txt", "data");
        string b = CreateFile("sub2", "file.txt", "data");

        var groups = DuplicateFinderCore.FindDuplicateGroups(new[] { a, b });

        Assert.Single(groups);
    }

    // ---------------------------------------------------------------
    // SelectOriginal  (shortest filename rule in DuplicateFinderCore.SelectOriginal)
    // ---------------------------------------------------------------

    [Fact]
    public void SelectOriginal_ShortestFilenameIsKept()
    {
        // "a.txt" (5 chars) is shorter than "ab.txt" (6 chars)
        string a = CreateFile("a.txt", "content");
        string b = CreateFile("ab.txt", "content");

        string original = DuplicateFinderCore.SelectOriginal(new[] { a, b });

        Assert.Equal(a, original);
    }

    [Fact]
    public void SelectOriginal_LongerNameIsNotKept()
    {
        string a = CreateFile("short.txt", "x");
        string b = CreateFile("much_longer_name.txt", "x");

        string original = DuplicateFinderCore.SelectOriginal(new[] { a, b });

        Assert.NotEqual(b, original);
    }

    [Fact]
    public void SelectOriginal_AmongThreeFiles_ShortestIsKept()
    {
        string a = CreateFile("z.txt", "data");
        string b = CreateFile("zz.txt", "data");
        string c = CreateFile("zzz.txt", "data");

        string original = DuplicateFinderCore.SelectOriginal(new[] { b, c, a });

        Assert.Equal(a, original);
    }

    [Fact]
    public void SelectOriginal_SingleFile_ReturnsThatFile()
    {
        string a = CreateFile("only.txt", "data");

        Assert.Equal(a, DuplicateFinderCore.SelectOriginal(new[] { a }));
    }

    // ---------------------------------------------------------------
    // HandleDuplicate — dry mode
    // ---------------------------------------------------------------

    [Fact]
    public void HandleDuplicate_DryMode_DoesNotDeleteFile()
    {
        string file = CreateFile("dry.txt", "keep me");

        DuplicateFinderCore.HandleDuplicate(file, "dry", null);

        Assert.True(File.Exists(file));
    }

    // ---------------------------------------------------------------
    // HandleDuplicate — delete mode
    // ---------------------------------------------------------------

    [Fact]
    public void HandleDuplicate_DeleteMode_DeletesFile()
    {
        string file = CreateFile("delete_me.txt", "bye");

        DuplicateFinderCore.HandleDuplicate(file, "delete", null);

        Assert.False(File.Exists(file));
    }

    // ---------------------------------------------------------------
    // HandleDuplicate — move mode
    // ---------------------------------------------------------------

    [Fact]
    public void HandleDuplicate_MoveMode_MovesFileToDestination()
    {
        string file = CreateFile("move_me.txt", "data");
        string dest = Path.Combine(_tempDir, "moved");

        DuplicateFinderCore.HandleDuplicate(file, "move", dest);

        Assert.False(File.Exists(file));
        Assert.True(File.Exists(Path.Combine(dest, "move_me.txt")));
    }

    [Fact]
    public void HandleDuplicate_MoveMode_CreatesDestinationFolderIfMissing()
    {
        string file = CreateFile("newdir_file.txt", "data");
        string dest = Path.Combine(_tempDir, "brand_new_folder");

        Assert.False(Directory.Exists(dest));

        DuplicateFinderCore.HandleDuplicate(file, "move", dest);

        Assert.True(Directory.Exists(dest));
    }

    /// <summary>
    /// When a file with the same name already exists in the destination
    /// folder, the duplicate is renamed with a counter: "(1)", "(2)", …
    /// This matches the counter loop in <see cref="DuplicateFinderCore.HandleDuplicate"/>.
    /// </summary>
    [Fact]
    public void HandleDuplicate_MoveMode_RenamesWithCounterOnCollision()
    {
        string dest = Path.Combine(_tempDir, "dest");
        Directory.CreateDirectory(dest);

        // Pre-create a file at the destination so there is a name collision.
        File.WriteAllText(Path.Combine(dest, "dup.txt"), "existing");

        string file = CreateFile("dup.txt", "new data");

        DuplicateFinderCore.HandleDuplicate(file, "move", dest);

        Assert.True(File.Exists(Path.Combine(dest, "dup (1).txt")));
    }

    [Fact]
    public void HandleDuplicate_MoveMode_RenamesWithIncrementingCounterOnMultipleCollisions()
    {
        string dest = Path.Combine(_tempDir, "dest2");
        Directory.CreateDirectory(dest);

        // Pre-create both "dup.txt" and "dup (1).txt" so counter must reach 2.
        File.WriteAllText(Path.Combine(dest, "dup.txt"), "first");
        File.WriteAllText(Path.Combine(dest, "dup (1).txt"), "second");

        string file = CreateFile("dup.txt", "third");

        DuplicateFinderCore.HandleDuplicate(file, "move", dest);

        Assert.True(File.Exists(Path.Combine(dest, "dup (2).txt")));
    }

    [Fact]
    public void HandleDuplicate_MoveMode_NullMoveFolder_DoesNotMoveFile()
    {
        string file = CreateFile("nomove.txt", "data");

        DuplicateFinderCore.HandleDuplicate(file, "move", null);

        Assert.True(File.Exists(file));
    }

    [Fact]
    public void HandleDuplicate_MoveMode_EmptyMoveFolder_DoesNotMoveFile()
    {
        string file = CreateFile("nomove2.txt", "data");

        DuplicateFinderCore.HandleDuplicate(file, "move", "   ");

        Assert.True(File.Exists(file));
    }

    // ---------------------------------------------------------------
    // HandleDuplicate — edge cases
    // ---------------------------------------------------------------

    [Fact]
    public void HandleDuplicate_NullMode_DoesNotDeleteFile()
    {
        string file = CreateFile("nullmode.txt", "safe");

        DuplicateFinderCore.HandleDuplicate(file, null!, null);

        Assert.True(File.Exists(file));
    }

    [Fact]
    public void HandleDuplicate_EmptyMode_DoesNotDeleteFile()
    {
        string file = CreateFile("emptymode.txt", "safe");

        DuplicateFinderCore.HandleDuplicate(file, "  ", null);

        Assert.True(File.Exists(file));
    }

    [Fact]
    public void HandleDuplicate_UnknownMode_DoesNotDeleteFile()
    {
        string file = CreateFile("unknownmode.txt", "safe");

        DuplicateFinderCore.HandleDuplicate(file, "wipe", null);

        Assert.True(File.Exists(file));
    }
}
