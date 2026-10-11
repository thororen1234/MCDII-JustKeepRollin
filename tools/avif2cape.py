"""Turns an animated cape (AVIF, GIF, WebP or APNG) into a PNG of its frames stacked top to bottom, for CustomCapes.

Usage: python tools/avif2cape.py <animated image> [<output .png>]

The game can't read AVIF (Unreal imports PNG, not AVIF), so animated AVIF capes are converted first. Each frame keeps
its size (64x32, 22x17 or an HD size of either, or 32x16). The output defaults to the input's name with .png. Prints the
frames per second the image plays at, for CustomCapes' Frames Per Second setting. Needs Pillow 11.3 or newer (pip
install pillow).
"""
import os
import sys

from PIL import Image, ImageSequence

def main():
    if len(sys.argv) < 2:
        print(__doc__)
        return 1
    source = sys.argv[1]
    target = sys.argv[2] if len(sys.argv) > 2 else os.path.splitext(source)[0] + ".png"
    with Image.open(source) as image:
        frames = []
        durations = []
        for frame in ImageSequence.Iterator(image):
            frames.append(frame.convert("RGBA"))
            durations.append(frame.info.get("duration") or 0)
    width, height = frames[0].size
    if any(frame.size != (width, height) for frame in frames):
        print("The frames aren't all the same size")
        return 1
    strip = Image.new("RGBA", (width, height * len(frames)))
    for i, frame in enumerate(frames):
        strip.paste(frame, (0, height * i))
    strip.save(target)
    print(f"Saved {target}: {len(frames)} frames of {width}x{height}")
    timed = [duration for duration in durations if duration > 0]
    if timed:
        print(f"Plays at about {1000 * len(timed) / sum(timed):.0f} frames per second")
    return 0

if __name__ == "__main__":
    sys.exit(main())
