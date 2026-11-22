"""Refresh the embedded icon masks. Requires Python, Pillow and PyMuPDF.

Run from the repository root. Downloads are cached under
artifacts/theme-icons-sources for offline regeneration.
"""

import concurrent.futures
import io
import pathlib
import re
import urllib.error
import urllib.request
import zipfile

import pymupdf
from PIL import Image


HERE = pathlib.Path(__file__).parent
CACHE = pathlib.Path("artifacts/theme-icons-sources")
LICENSES = pathlib.Path("Licenses")
RAW = "https://raw.githubusercontent.com"
LUCIDE = f"{RAW}/lucide-icons/lucide/c1a02a04d4f3f7049d44567d62b8b06cff0b9103"
LUCIDE_BOLD = f"{RAW}/lucide-icons/lucide/0.468.0/icons/bold.svg"
MATERIAL = f"{RAW}/google/material-design-icons/10ecc14790612ca8fd164e3f4ffe9c16fc50d5b7"
FLUENT = "https://cdn.jsdelivr.net/npm/@fluentui/svg-icons@1.1.347"
METRO = "https://learn.microsoft.com/en-us/windows/apps/design/iconography/segoe-ui-symbol-font"


def fetch(url, file):
    if not file.exists():
        file.parent.mkdir(parents=True, exist_ok=True)
        request = urllib.request.Request(url, headers={"User-Agent": "SnapFlow icon asset compiler"})
        for attempt in range(3):
            try:
                with urllib.request.urlopen(request, timeout=25) as response:
                    file.write_bytes(response.read())
                break
            except urllib.error.HTTPError as error:
                if error.code == 404 or attempt == 2:
                    raise
            except (OSError, urllib.error.URLError):
                if attempt == 2:
                    raise
    return file.read_bytes()


def png(svg):
    # Keep the upstream viewBox, line caps and winding rules. Tint at paint time.
    svg = svg.replace(b"currentColor", b"#000")
    with pymupdf.open(stream=svg, filetype="svg") as document:
        page = document[0]
        pixels = page.get_pixmap(matrix=pymupdf.Matrix(96 / page.rect.width, 96 / page.rect.height), alpha=True)
        alpha = Image.frombytes("RGBA", (pixels.width, pixels.height), pixels.samples).getchannel("A")
    if not alpha.getbbox():
        raise ValueError("Empty icon")
    image = Image.new("RGBA", alpha.size)
    image.putalpha(alpha)
    output = io.BytesIO()
    image.save(output, format="PNG", optimize=True)
    return output.getvalue()


def remote_icon(style, name):
    if style == "Claude" and name == "bold":
        return png(fetch(LUCIDE_BOLD, CACHE / style / "bold.svg"))
    if style == "WinUI":
        for size in (24, 20, 16, 28, 32, 48):
            filename = f"{name}_{size}_regular.svg"
            try:
                return png(fetch(f"{FLUENT}/icons/{filename}", CACHE / style / filename))
            except urllib.error.HTTPError as error:
                if error.code != 404:
                    raise
        raise ValueError("No regular-size variant in the Fluent package")
    url = (f"{LUCIDE}/icons/{name}.svg" if style == "Claude" else
           f"{MATERIAL}/symbols/web/{name}/materialsymbolsrounded/{name}_24px.svg")
    return png(fetch(url, CACHE / style / f"{name}.svg"))


def main():
    rows = [line.split("\t") for line in (HERE / "sources.tsv").read_text().splitlines() if line and not line.startswith("#")]
    icons = {}
    errors = []
    with concurrent.futures.ThreadPoolExecutor(max_workers=20) as pool:
        jobs = {pool.submit(remote_icon, style, name): (style, name)
                for style, column in [("Claude", 1), ("MaterialYou", 2), ("WinUI", 3)]
                for name in sorted({row[column] for row in rows})}
        metro = fetch(METRO, CACHE / "metro.html").decode("utf-8")
        symbols = dict((name, code) for code, name in re.findall(
            r'<td>([0-9A-F]{4})</td>\s*<td>(?:<span[^>]*>)?([\w]+)', metro, re.I))
        if len(symbols) < 100:
            raise ValueError("Cannot read Segoe MDL2 Assets catalogue")
        for job, key in jobs.items():
            try:
                icons[key] = job.result()
            except Exception as error:
                errors.append(f"{' '.join(key)}: {error}")
        licenses = [
            ("Lucide-license.txt", f"{LUCIDE}/LICENSE"),
            ("MaterialSymbols-license.txt", f"{MATERIAL}/LICENSE"),
            ("FluentSystemIcons-license.txt", f"{RAW}/microsoft/fluentui-system-icons/main/LICENSE"),
        ]
        for filename, url in licenses:
            (LICENSES / filename).write_bytes(fetch(url, CACHE / filename))

    metro_codes = {}
    for action, _, _, _, choices in rows:
        if choices == "-":
            continue
        code = next((symbols[name] + "\t" + (angle or "0") for name, _, angle in
                     (choice.partition(":") for choice in choices.split(",")) if name in symbols), None)
        if code is None:
            errors.append(f"MetroClassic {action}: {choices}")
        else:
            metro_codes[action] = code
    if errors:
        raise SystemExit("\n".join(errors))
    with zipfile.ZipFile(HERE / "ThemeIcons.zip", "w") as archive:
        def add(name, data):
            archive.writestr(zipfile.ZipInfo(name), data, compress_type=zipfile.ZIP_DEFLATED)

        for action, claude, material, winui, _ in rows:
            for style, symbol in [("Claude", claude), ("MaterialYou", material), ("WinUI", winui)]:
                add(f"{style}/{action}.png", icons[style, symbol])
        add("MetroClassic.tsv", "".join(f"{action}\t{code}\n" for action, code in metro_codes.items()))
    print(f"Generated {len(rows)} icons per theme.")


if __name__ == "__main__":
    main()
