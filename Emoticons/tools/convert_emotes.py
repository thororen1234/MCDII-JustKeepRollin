import math
import os

import numpy as np

HERE = os.path.dirname(os.path.abspath(__file__))
SOURCE = os.path.join(HERE, "source")
OUT = os.path.join(HERE, "..", "EmoteData.cs")

BONES = ["anchor", "body", "low_body", "head",
         "left_arm", "low_left_arm", "right_arm", "low_right_arm",
         "left_leg", "low_left_leg", "right_leg", "low_leg_right"]

LIMBS = [("left_arm", "low_left_arm"), ("right_arm", "low_right_arm"),
         ("left_leg", "low_left_leg"), ("right_leg", "low_leg_right")]

def ticks(n):
    return int(math.floor(n / 30.0 * 20.0))

EMOTES = [
    ("electro_shuffle", 169, True), ("gangnam_style", 18, True), ("hype", 68, True), ("squat_kick", 232, True),
    ("take_the_l", 16, True), ("boy", 29, False), ("calculated", 33, False), ("chicken", 19, True),
    ("clapping", 15, True), ("confused", 140, False), ("facepalm", 104, False), ("fist", 53, False),
    ("no", 30, False), ("pointing", 33, False), ("pure_salt", 104, False), ("salute", 50, False),
    ("shrug", 50, False), ("t_pose", 80, True), ("thinking", 100, True), ("wave", 40, False), ("yes", 23, False),
    ("bitchslap", ticks(100), False), ("bongo_cat", ticks(238), False), ("breathtaking", ticks(154), False),
    ("woah", ticks(66), False), ("stick_bug", ticks(25), True), ("slow_clap", ticks(200), False),
    ("hell_yeah", ticks(70), False), ("scared", ticks(50), True), ("smug_dance", ticks(29), True),
    ("nope", ticks(101), False), ("ragdoll_1", ticks(135), False), ("ragdoll_2", ticks(150), False),
    ("ragdoll_3", ticks(120), False),
]

TO_UE = np.array([[0, 0, 1], [-1, 0, 0], [0, 1, 0]], dtype=float)
STEPS = 4095
MAX_OFFSET = 4.0

class Keyframe:
    def __init__(self, parts):
        self.frame = float(parts[1])
        self.value = float(parts[2])
        mode = parts[3] if len(parts) > 3 else "LINEAR"
        self.mode = mode if mode in ("CONSTANT", "BEZIER") else "LINEAR"
        if len(parts) == 8:
            self.left_x, self.left_y, self.right_x, self.right_y = map(float, parts[4:8])
        else:
            self.left_x = self.left_y = self.right_x = self.right_y = 0.0

def load(path):
    bones, actions = {}, {}
    action = group = channel = None
    for line in open(path, encoding="utf-8"):
        p = line.split()
        if not p:
            continue
        if p[0] == "arm_bone":
            if len(p) == 22:
                name, parent, tail, rest = p[1], p[2], p[3:6], p[6:22]
            else:
                name, parent, tail, rest = p[1], "", p[2:5], p[5:21]
            bones[name] = (parent, np.array(list(map(float, rest)), dtype=float).reshape(4, 4),
                           np.array(list(map(float, tail)) + [1.0]))
        elif p[0] == "an":
            action = actions.setdefault(p[1], {})
        elif p[0] == "ao":
            group = action.setdefault(p[1], {})
        elif p[0] == "ag":
            channel = group.setdefault((p[1], int(p[2])), [])
        elif p[0] == "kf":
            channel.append(Keyframe(p))
    return bones, actions

def lerp(a, b, t):
    return a + (b - a) * t

def bezier(a, b, c, d, t):
    ab, bc, cd = lerp(a, b, t), lerp(b, c, t), lerp(c, d, t)
    return lerp(lerp(ab, bc, t), lerp(bc, cd, t), t)

def bezier_x(x1, x2, x, epsilon):
    t = x
    current = bezier(0.0, x1, x2, 1.0, t)
    step = math.copysign(0.1, x - current)
    for _ in range(10000):
        if abs(x - current) <= epsilon:
            break
        previous = step
        t += step
        current = bezier(0.0, x1, x2, 1.0, t)
        if math.copysign(step, x - current) != previous:
            step *= -0.25
    return t

def interpolate(a, progress, b):
    if a.mode == "CONSTANT":
        return a.value
    if a.mode == "LINEAR":
        return lerp(a.value, b.value, progress)
    if progress <= 0:
        return a.value
    if progress >= 1:
        return b.value
    frames = b.frame - a.frame
    values = b.value - a.value
    if values == 0:
        values = 1e-5
    right_x = min(max((a.right_x - a.frame) / frames, 0.0), 1.0)
    right_y = (a.right_y - a.value) / values
    left_x = min(max((b.left_x - a.frame) / frames, 0.0), 1.0)
    left_y = (b.left_y - a.value) / values
    epsilon = max(min(5e-4, 1.0 / values * 5e-4), 1e-5)
    return bezier(0.0, right_y, left_y, 1.0, bezier_x(right_x, left_x, progress, epsilon)) * values + a.value

def evaluate(keys, frame):
    if not keys:
        return 0.0
    if len(keys) == 1 or keys[0].frame > frame:
        return keys[0].value
    for i in range(1, len(keys)):
        if keys[i].frame > frame:
            a = keys[i - 1]
            return interpolate(a, (frame - a.frame) / (keys[i].frame - a.frame), keys[i])
    return keys[-1].value

def duration(action):
    return max((int(math.ceil(keys[-1].frame)) for group in action.values() for keys in group.values() if keys),
               default=0)

def rot(axis, angle):
    c, s = math.cos(angle), math.sin(angle)
    m = np.identity(4)
    i, j = {"x": (1, 2), "y": (2, 0), "z": (0, 1)}[axis]
    m[i, i], m[i, j], m[j, i], m[j, j] = c, -s, s, c
    return m

def pose(bones, action, frame):
    world = {}

    def compute(name):
        if name in world:
            return world[name]
        parent, rest, _ = bones[name]
        rel = np.linalg.inv(bones[parent][1]) @ rest if parent else rest
        values = {key: evaluate(keys, frame) for key, keys in action.get(name, {}).items()}
        t = np.identity(4)
        t[0:3, 3] = [values.get(("location", i), 0.0) for i in range(3)]
        s = np.diag([values.get(("scale", i), 1.0) for i in range(3)] + [1.0])
        m = rel @ t @ s
        m = m @ rot("z", values.get(("rotation", 2), 0.0))
        m = m @ rot("y", values.get(("rotation", 1), 0.0))
        m = m @ rot("x", values.get(("rotation", 0), 0.0))
        if parent:
            m = compute(parent) @ m
        world[name] = m
        return m

    for name in bones:
        compute(name)
    return world

def rotation_of(m):
    r = m[0:3, 0:3].copy()
    for i in range(3):
        r[:, i] /= np.linalg.norm(r[:, i])
    u, _, vt = np.linalg.svd(r)
    return u @ vt

def quaternion(r):
    trace = r[0, 0] + r[1, 1] + r[2, 2]
    if trace > 0:
        s = math.sqrt(trace + 1.0) * 2
        q = [(r[2, 1] - r[1, 2]) / s, (r[0, 2] - r[2, 0]) / s, (r[1, 0] - r[0, 1]) / s, 0.25 * s]
    elif r[0, 0] > r[1, 1] and r[0, 0] > r[2, 2]:
        s = math.sqrt(1.0 + r[0, 0] - r[1, 1] - r[2, 2]) * 2
        q = [0.25 * s, (r[0, 1] + r[1, 0]) / s, (r[0, 2] + r[2, 0]) / s, (r[2, 1] - r[1, 2]) / s]
    elif r[1, 1] > r[2, 2]:
        s = math.sqrt(1.0 + r[1, 1] - r[0, 0] - r[2, 2]) * 2
        q = [(r[0, 1] + r[1, 0]) / s, 0.25 * s, (r[1, 2] + r[2, 1]) / s, (r[0, 2] - r[2, 0]) / s]
    else:
        s = math.sqrt(1.0 + r[2, 2] - r[0, 0] - r[1, 1]) * 2
        q = [(r[0, 2] + r[2, 0]) / s, (r[1, 2] + r[2, 1]) / s, 0.25 * s, (r[1, 0] - r[0, 1]) / s]
    q = np.array(q)
    q /= np.linalg.norm(q)
    return -q if q[3] < 0 else q

def between(a, b):
    a, b = a / np.linalg.norm(a), b / np.linalg.norm(b)
    axis, c = np.cross(a, b), float(np.dot(a, b))
    if np.linalg.norm(axis) < 1e-9:
        if c > 0:
            return np.identity(3)
        other = np.array([1.0, 0, 0]) if abs(a[0]) < 0.9 else np.array([0, 1.0, 0])
        axis = np.cross(a, other)
        axis /= np.linalg.norm(axis)
        return 2 * np.outer(axis, axis) - np.identity(3)
    k = np.array([[0, -axis[2], axis[1]], [axis[2], 0, -axis[0]], [-axis[1], axis[0], 0]])
    return np.identity(3) + k + k @ k * (1 / (1 + c))

def tail(bones, world, name):
    return (world[name] @ np.linalg.inv(bones[name][1]) @ bones[name][2])[0:3]

def bake(bones, action, frames):
    rest_rot = {name: rotation_of(bones[name][1]) for name in BONES}
    rest_pelvis = bones["body"][1][0:3, 3]
    rest_world = {name: bones[name][1] for name in bones}
    rotations, offsets = [], []
    for frame in range(frames):
        world = pose(bones, action, float(frame))
        deltas = [rotation_of(world[n]) @ rest_rot[n].T for n in BONES]
        for upper, lower in LIMBS:
            i = BONES.index(upper)
            rest_dir = tail(bones, rest_world, lower) - rest_world[upper][0:3, 3]
            now_dir = tail(bones, world, lower) - world[upper][0:3, 3]
            deltas[i] = between(deltas[i] @ rest_dir, now_dir) @ deltas[i]
        rotations.append([TO_UE @ d @ TO_UE.T for d in deltas])
        offsets.append(TO_UE @ (world["body"][0:3, 3] - rest_pelvis))
    return rotations, offsets

def encode(value, limit):
    v = int(round((min(max(value / limit, -1.0), 1.0) + 1.0) / 2.0 * STEPS))
    return chr(48 + (v >> 6)) + chr(48 + (v & 63))

def angle(r):
    return math.degrees(math.acos(min(1.0, max(-1.0, (np.trace(r) - 1) / 2))))

def title(key):
    return " ".join(w.capitalize() for w in key.split("_")).replace("Take The L", "Take the L")

def literal(value):
    if isinstance(value, bool):
        return "true" if value else "false"
    if isinstance(value, str):
        return f'@"{value}"'
    return str(value)

def switch(name, param, values, default):
    kind = {bool: "bool", str: "string", int: "int"}[type(values[0])]
    lines = [f"    public static {kind} {name}(int {param})", "    {", f"        switch ({param})", "        {"]
    lines += [f"            case {i}: return {literal(v)};" for i, v in enumerate(values)]
    return lines + ["        }", f"        return {default};", "    }", ""]

def main():
    bones, actions = load(os.path.join(SOURCE, "armature.bobj"))
    actions.update(load(os.path.join(SOURCE, "actions.bobj"))[1])
    ragdoll_bones, ragdoll_actions = load(os.path.join(SOURCE, "ragdoll.bobj"))

    entries, total = [], 0
    for key, ticks_, looping in EMOTES:
        rig, rig_actions = (ragdoll_bones, ragdoll_actions) if key.startswith("ragdoll") else (bones, actions)
        action = rig_actions["emote_" + key]
        length = duration(action)
        frames = (length if looping else max(length, ticks_)) + 1
        rotations, offsets = bake(rig, action, frames)
        mask = [i for i in range(len(BONES)) if max(angle(f[i]) for f in rotations) > 0.25]
        moves = bool(max(np.abs(o).max() for o in offsets) > 0.005)
        data = []
        for f in range(frames):
            for i in mask:
                q = quaternion(rotations[f][i])
                data.append(encode(q[0], 1) + encode(q[1], 1) + encode(q[2], 1))
            if moves:
                data.append("".join(encode(v, MAX_OFFSET) for v in offsets[f]))
        text = "".join(data)
        total += len(text)
        entries.append((key, length, ticks_, looping, frames, sum(1 << i for i in mask), moves, text))

    lines = [
        "// Generated by tools/convert_emotes.py: don't edit by hand.",
        "namespace Emoticons;",
        "",
        "public static class EmoteData",
        "{",
        f"    public const int Count = {len(entries)};",
        f"    public const float MaxOffset = {MAX_OFFSET}f;",
        "",
    ]
    lines += switch("Key", "index", [e[0] for e in entries], '""')
    lines += switch("SlotName", "slot", BONES, '""')
    lines += switch("Title", "index", [title(e[0]) for e in entries], '""')
    lines += switch("Length", "index", [e[1] for e in entries], "0")
    lines += switch("Duration", "index", [e[2] for e in entries], "0")
    lines += switch("Looping", "index", [e[3] for e in entries], "false")
    lines += switch("Frames", "index", [e[4] for e in entries], "0")
    lines += switch("Bones", "index", [e[5] for e in entries], "0")
    lines += switch("Moves", "index", [e[6] for e in entries], "false")
    lines += switch("Animation", "index", [e[7] for e in entries], '""')
    lines[-1:] = ["}", ""]
    with open(OUT, "w", encoding="utf-8", newline="\n") as f:
        f.write("\n".join(lines))
    print(f"{len(entries)} emotes, {total} characters of animation -> {os.path.normpath(OUT)}")

if __name__ == "__main__":
    main()
