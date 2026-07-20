import argparse
from pathlib import Path

from PIL import Image, ImageOps


def main() -> None:
    parser = argparse.ArgumentParser(description="Normalize a generated wallpaper to an exact PNG size")
    parser.add_argument("--input", required=True)
    parser.add_argument("--output", required=True)
    parser.add_argument("--width", required=True, type=int)
    parser.add_argument("--height", required=True, type=int)
    parser.add_argument("--focus-x", default=0.5, type=float)
    parser.add_argument("--focus-y", default=0.5, type=float)
    args = parser.parse_args()

    source = Path(args.input)
    output = Path(args.output)
    with Image.open(source) as image:
        normalized = ImageOps.fit(
            image.convert("RGB"),
            (args.width, args.height),
            method=Image.Resampling.LANCZOS,
            centering=(args.focus_x, args.focus_y),
        )
        normalized.save(output, format="PNG", optimize=True)


if __name__ == "__main__":
    main()
