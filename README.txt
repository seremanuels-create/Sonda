SONDA - disk space analyser
===========================

What it does
------------
Scans a whole drive (or a single folder) and tells you:
  - the MAIN CAUSE of the space usage, up front, with what it is and how to free it;
  - every OTHER CAUSE, heaviest first;
  - the biggest files, with path, type ("what it is"), category and safety level
    (safe to delete / your call / leave alone);
  - a map of the folders (each rectangle proportional to the space used);
  - the BALANCE: space used according to Windows against space found in files, with
    an explanation of the difference (restore points, unreadable folders, MFT...).

Sizes are "on disk": the space actually taken, rounded up to the cluster; NTFS-compressed
files and OneDrive "online-only" files count for what they really use.

How to use it
-------------
1. Pick a drive (or "Folder..." for a single folder) and press Analyse.
   A drive with a million files takes 10-20 seconds on an SSD.
2. Left column: the main cause and the others. Click a cause for the detail.
3. Folders tab: double-click to enter, right-click to open in File Explorer,
   copy the path or send to the Recycle Bin.
4. Biggest files tab: filter by text or category; "Delete selected" sends items to the
   Recycle Bin (recoverable).
5. Restart as administrator to read System Volume Information (restore points),
   WindowsApps and other users' profiles.

Language
--------
English and Italian. The gear button (top right) opens Settings > Language; "Automatic"
follows Windows. The choice is saved in %APPDATA%\Sonda\impostazioni.json.

Command line
------------
  Sonda.exe C:\                        open and analyse C: straight away
  Sonda.exe --report C:\ --out r.txt   write the full report to a text file
  Sonda.exe --report C:\ --csv dir     as above, plus three CSV files
  Sonda.exe --report C:\ --lang en     force the language for this run

Portable
--------
Sonda.exe is a single file: copy it anywhere, it writes nothing to the registry.
No .NET installation needed. On first run it extracts its native libraries into
%TEMP%\.net\Sonda\ (that is how single-file .NET programs work).

Good to know
------------
- WinSxS: the size shown is gross (hard links shared with System32); Sonda says so in the
  category description. The real figure comes from
  "Dism /Online /Cleanup-Image /AnalyzeComponentStore".
- The "safe to delete / your call / leave alone" labels are per-category heuristics:
  always look at the path before deleting. Everything goes to the Recycle Bin.

StarVerb Audio - 2026 - MIT licence
