# FastDuplicateCleaner

FastDuplicateCleaner is a **fast, safe, and free duplicate file cleaner** written in C#.
It finds *true* duplicate files using cryptographic hashing and helps you reclaim disk space **without paying for expensive commercial software**.

Many duplicate file cleaners lock essential features behind paywalls.
FastDuplicateCleaner provides those same core features — transparently and safely — as a **free, open alternative**.

The project was created using a **vibecoding / promptcoding** workflow: requirements were expressed in natural language, refined iteratively, and translated into working code through AI-assisted development.

---

## Why FastDuplicateCleaner?

Most duplicate file tools:

* Require a paid license to delete or move duplicates
* Are opaque about how duplicates are detected
* Either sacrifice speed or correctness

FastDuplicateCleaner is designed to be:

* ✅ **Correct** — no false positives
* ⚡ **Efficient** — smart filtering before hashing
* 🧠 **Transparent** — visible progress reporting
* 🛠️ **Practical** — dry run, move, or delete modes
* 💸 **Free** — no subscriptions, no feature locks

---

## Vibecoding / Promptcoding

This project was built through **iterative, prompt-driven development**:

* Initial goal: find duplicate files
* Performance optimizations added gradually
* False positives eliminated
* Progress reporting introduced
* UX refined through real-world usage

This approach is often called **vibecoding** or **promptcoding**:

> Express intent → observe behavior → refine → repeat

Rather than rigid upfront design, the tool evolved organically based on feedback and experimentation.

---

## How FastDuplicateCleaner Works

FastDuplicateCleaner uses a **multi-step pipeline** to safely detect duplicates.

1. **File Scan**
   All files in the selected folder (including subfolders) are enumerated.

2. **Group by File Size (Fast Filter)**
   Files are grouped by size.

   * Files with different sizes can never be duplicates
   * This instantly eliminates most files

3. **Full SHA256 Hash (Guaranteed Correctness)**
   For each size group with more than one file:

   * A **full SHA256 hash** is computed
   * Only files with the same size **and** same hash are considered duplicates
     This ensures **no false positives**.

4. **Original File Selection**
   Among duplicates:

   * The file with the **shortest filename** is kept
   * Files like `file (1).jpg` or `file - Copy.jpg` are treated as duplicates

5. **Duplicate Handling**
   Duplicates can be:

   * **Dry-run** → show what would happen
   * **Moved** → relocated to a separate folder
   * **Deleted** → permanently removed

---

## Building the Executable

### Prerequisites

* Windows
* .NET SDK installed (`dotnet --version` should work)

### Build a Single-File EXE

From the project folder:

```powershell
dotnet publish FastDuplicateCleaner.cs `
  -c Release `
  -r win-x64 `
  /p:SelfContained=true `
  /p:PublishSingleFile=true `
  /p:PublishAot=false `
  -o .\output
```

The executable will be created at:

```
output/FastDuplicateCleaner.exe
```

---

## Usage

### Interactive Mode

Run the executable without arguments:

```powershell
FastDuplicateCleaner.exe
```

You will be prompted for:

1. Folder to scan
2. Mode (`dry`, `move`, or `delete`)
3. Destination folder (if using `move`)

---

### Command-Line Mode

```powershell
FastDuplicateCleaner.exe dry "C:\MyFolder"
FastDuplicateCleaner.exe delete "C:\MyFolder"
FastDuplicateCleaner.exe move "C:\MyFolder" "C:\Duplicates"
```

---

### Modes Explained

#### Dry Run (`dry`)

* No files are modified
* Shows which files would be treated as duplicates
* Recommended for first-time use

#### Move (`move`)

* Keeps one original file
* Moves all duplicates to a separate folder
* Safest cleanup method

#### Delete (`delete`)

* Permanently deletes duplicate files
* No recycle bin
* **Use only after verifying with dry-run or move mode**

---

### How Originals Are Chosen

* The file with the **shortest filename** is preserved
* Example:

```
photo.jpg        ← kept
photo (1).jpg    ← duplicate
photo - Copy.jpg ← duplicate
```

---

## Progress Reporting

* Tracks progress based on **total bytes hashed**
* Smooth percentage updates
* Large files no longer freeze the display

Example:

```
Hashing progress: 73.42%
```

---

## Performance Considerations

* Very large files (GB+) can take time to hash
* Performance depends on:

  * SSD vs HDD
  * External or network drives
  * Antivirus software scanning files

---

## Safety Guidelines

* **Always start with dry mode**
* Keep backups of important data
* Avoid system folders:

  * `C:\Windows`
  * `C:\Program Files`
  * Application data directories
* Do not interrupt the program during hashing

---

## Known Limitations

* Symbolic links are treated as files
* Hard links may appear as duplicates
* Locked or inaccessible files are skipped

---

## Why SHA256?

* Cryptographically strong
* Extremely low collision probability
* Industry-standard hashing algorithm

Ensures **only truly identical files** are detected.

---

## License

This project is provided as-is.
Use at your own risk and always keep backups when deleting files.

---

## Final Thoughts

FastDuplicateCleaner proves that **powerful, reliable tools don’t need to be paid, bloated, or opaque**.
Built through vibecoding, refined through iteration, and designed for safety — it’s a practical, transparent alternative to commercial duplicate file cleaners.

Happy cleaning 🧹
