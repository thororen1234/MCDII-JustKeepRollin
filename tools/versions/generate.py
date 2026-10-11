"""Writes versions.png and versions.txt: the latest version of every Minecraft Dungeons II mod on Nexus Mods, for
BetterBlueprintLoader's update check (BetterBlueprintLoader/ModUpdates.cs).

Usage: python tools/versions/generate.py [<output folder>]

A game mod can't read web pages, but it can download an image, so the text goes into a PNG. versions.txt has one line per
mod: "modId;mainVersion;fileName=version;...", all lower case. In versions.png each character is a 2x2 pixel cell whose
red, green and blue are each one of 4 levels (0, 85, 170, 255): 6 bits, the character's place in ALPHABET. The image
starts with 4 grey cells of those levels, for the reader to tell them apart by, and the text ends with the value 63.
Cells go left to right, then down. Characters outside the alphabet become "_".

Nexus Mods' GraphQL API is public: no key is needed. Run every hour by .github/workflows/versions.yml.
"""
import json
import os
import struct
import sys
import time
import urllib.request
import zlib

API = "https://api.nexusmods.com/v2/graphql"
GAME_DOMAIN = "minecraftdungeons2"
ALPHABET = "0123456789abcdefghijklmnopqrstuvwxyz .-_;=\n'()+!,&[]:/#@~*%^$"
END = 63
SIZE = 256
CELL = 2
CELLS = SIZE // CELL
LEVELS = (0, 85, 170, 255)
# Files that aren't the mod's current ones.
OLD = {"ARCHIVED", "OLD_VERSION", "DELETED", "REMOVED"}
PAGE = 50
BATCH = 25


def query(text, variables=None):
    body = json.dumps({"query": text, "variables": variables or {}}).encode()
    request = urllib.request.Request(API, body, {"Content-Type": "application/json", "User-Agent": "MCDII-Mods versions"})
    for attempt in range(4):
        try:
            with urllib.request.urlopen(request, timeout=60) as response:
                answer = json.load(response)
            break
        except OSError:
            if attempt == 3:
                raise
            time.sleep(5 * (attempt + 1))
    if answer.get("errors"):
        raise RuntimeError(json.dumps(answer["errors"]))
    return answer["data"]


def game_id():
    data = query("query($domain: String!) { game(domainName: $domain) { id } }", {"domain": GAME_DOMAIN})
    return data["game"]["id"]


def mods(game):
    """Every mod of the game: its id and the version its page shows."""
    found = {}
    offset = 0
    while True:
        data = query(
            "query($game: String!, $count: Int!, $offset: Int!) { mods(filter: { gameId: [{ value: $game, op: EQUALS }] },"
            " count: $count, offset: $offset) { totalCount nodes { modId version } } }",
            {"game": str(game), "count": PAGE, "offset": offset})
        nodes = data["mods"]["nodes"]
        for node in nodes:
            found[int(node["modId"])] = node.get("version") or ""
        offset += len(nodes)
        if not nodes or offset >= data["mods"]["totalCount"]:
            return found


def files(game, ids):
    """Every mod's files: name, version, category, date."""
    found = {}
    ids = sorted(ids)
    for start in range(0, len(ids), BATCH):
        batch = ids[start:start + BATCH]
        fields = " ".join(f"m{i}: modFiles(modId: {i}, gameId: {game}) {{ name version category date }}" for i in batch)
        data = query("{ " + fields + " }")
        for i in batch:
            found[i] = data.get(f"m{i}") or []
    return found


def clean(value):
    text = str(value or "").lower()
    for mark in ";=\n":
        text = text.replace(mark, " ")
    return "".join(char if char in ALPHABET else "_" for char in text.strip())


def line(mod_id, page_version, mod_files):
    current = sorted((f for f in mod_files if f.get("category") not in OLD), key=lambda f: f.get("date") or 0)
    main = [f for f in current if f.get("category") == "MAIN"]
    version = main[-1]["version"] if main else page_version
    parts = [str(mod_id), clean(version)]
    named = {}
    for f in current:
        named[clean(f.get("name"))] = clean(f.get("version"))
    parts += [f"{name}={v}" for name, v in named.items() if name]
    return ";".join(parts)


def chunk(kind, data):
    return struct.pack(">I", len(data)) + kind + data + struct.pack(">I", zlib.crc32(kind + data) & 0xFFFFFFFF)


def encode(text):
    values = [ALPHABET.index(char) for char in text] + [END]
    cells = [(level,) * 3 for level in LEVELS]
    cells += [(LEVELS[v >> 4], LEVELS[(v >> 2) & 3], LEVELS[v & 3]) for v in values]
    if len(cells) > CELLS * CELLS:
        raise RuntimeError(f"{len(text)} characters don't fit in the image")
    cells += [(0, 0, 0)] * (CELLS * CELLS - len(cells))
    rows = bytearray()
    for y in range(SIZE):
        rows.append(0)
        for x in range(SIZE):
            rows.extend(cells[(y // CELL) * CELLS + x // CELL])
    header = struct.pack(">IIBBBBB", SIZE, SIZE, 8, 2, 0, 0, 0)
    return b"\x89PNG\r\n\x1a\n" + chunk(b"IHDR", header) + chunk(b"IDAT", zlib.compress(bytes(rows), 9)) + chunk(b"IEND", b"")


def main():
    out = sys.argv[1] if len(sys.argv) > 1 else "."
    os.makedirs(out, exist_ok=True)
    game = game_id()
    pages = mods(game)
    all_files = files(game, pages)
    text = "\n".join(line(i, pages[i], all_files[i]) for i in sorted(pages))
    with open(os.path.join(out, "versions.png"), "wb") as image:
        image.write(encode(text))
    with open(os.path.join(out, "versions.txt"), "w", encoding="utf-8", newline="\n") as plain:
        plain.write(text)
    print(f"{len(pages)} mods, {len(text)} characters")


if __name__ == "__main__":
    main()
