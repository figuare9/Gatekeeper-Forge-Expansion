"""Read-only source/asset inventory checks; does not build, install or launch."""
from pathlib import Path, PurePosixPath
import hashlib
import json

ROOT = Path(__file__).resolve().parent


def read_json(path):
    return json.loads(path.read_text(encoding="utf-8-sig"))


def verify(rows, base):
    seen = set()
    for row in rows:
        name = row["path"]
        relative = PurePosixPath(name)
        if relative.is_absolute() or ".." in relative.parts or "\\" in name or ":" in name or name in seen:
            raise ValueError("Invalid/duplicate inventory path: " + name)
        seen.add(name)
        path = (base / name).resolve()
        if not path.is_relative_to(base.resolve()):
            raise ValueError("Inventory path leaves its source root: " + name)
        data = path.read_bytes()
        if "bytes" in row and len(data) != row["bytes"]:
            raise ValueError("Size mismatch: " + name)
        if hashlib.sha256(data).hexdigest() != row["sha256"].lower():
            raise ValueError("SHA-256 mismatch: " + name)
    return seen


def main():
    manifest = read_json(ROOT / "source-manifest.json")
    if manifest["format"] != 1:
        raise ValueError("Unsupported source inventory format")
    current = verify(manifest["files"], ROOT)
    actual = {
        p.relative_to(ROOT).as_posix()
        for p in (ROOT / "src").rglob("*")
        if p.is_file() and not {"bin", "obj", "__pycache__"}.intersection(p.relative_to(ROOT).parts)
    }
    if current != actual:
        raise ValueError("Source inventory coverage differs: " + repr(sorted(current ^ actual)))
    history = ROOT / "historical/installer-0.15.4"
    original = verify(read_json(history / "code-source-hashes.json"), history)
    assets = verify(read_json(history / "shared-assets.json"), ROOT)
    locks = sorted((ROOT / "vendor/BepInEx").rglob("packages.lock.json"))
    if not locks or any(not read_json(path).get("dependencies") for path in locks):
        raise ValueError("Missing/invalid vendored NuGet dependency locks")
    unity = read_json(ROOT / "dependencies.lock.json")["files"]["unity"]
    if hashlib.sha256((ROOT / "vendor/UnitySupport/2022.3.62.zip").read_bytes()).hexdigest() != unity["hash"]:
        raise ValueError("Bundled Unity support archive differs from its pinned digest")
    print(f"Verified {len(current)} current source/project/asset files, {len(original)} original source files, "
          f"{len(assets)} shared asset inputs, {len(locks)} dependency lock documents and pinned Unity support ZIP.")
    print(f"Current installer {manifest['installer']}; gameplay {manifest['gameplay']}; protocol {manifest['protocol']}.")


if __name__ == "__main__":
    main()
