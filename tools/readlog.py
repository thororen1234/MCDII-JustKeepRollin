"""Prints a mod's log file (Shared/FileLog.cs) as text.

Usage: python tools/readlog.py <Mod> [--previous]
       python tools/readlog.py <path to a .log file>

The file is the engine's Interchange node container: each line is an attribute name, "000123|text", stored as an
Unreal string (ANSI, or UTF-16 when it has other characters). The lines are found by that pattern and sorted.
"""
import os
import re
import sys

def lines(data):
    found = {}
    # ANSI strings: the text, then a NUL.
    for match in re.finditer(rb"(\d{6})\|([^\x00]*)\x00", data):
        found[int(match.group(1))] = match.group(2).decode("latin-1")
    # UTF-16 strings, at either byte alignment.
    for start in (0, 1):
        text = data[start:].decode("utf-16-le", errors="replace")
        for match in re.finditer(r"(\d{6})\|([^\x00]*)\x00", text):
            found.setdefault(int(match.group(1)), match.group(2))
    return [found[number] for number in sorted(found)]

def main():
    if len(sys.argv) < 2:
        print(__doc__)
        return 1
    target = sys.argv[1]
    if not target.lower().endswith(".log"):
        suffix = ".previous" if "--previous" in sys.argv else ""
        target = os.path.join(os.environ["LOCALAPPDATA"], "Dungeons2", "Saved", "Mods", target + suffix + ".log")
    if not os.path.exists(target):
        print(f"No log at {target}")
        return 1
    with open(target, "rb") as file:
        data = file.read()
    sys.stdout.reconfigure(encoding="utf-8")
    for line in lines(data):
        print(line)
    return 0

if __name__ == "__main__":
    sys.exit(main())
