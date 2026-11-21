#!/usr/bin/env python3
"""Create synthetic local demo data without credentials or external services."""

import argparse
from contextlib import closing
import importlib.util
import json
import os
from pathlib import Path
import secrets
import shutil
import sqlite3
import tempfile


REPO_ROOT = Path(__file__).resolve().parents[1]
EXAMPLE_DOCS = REPO_ROOT / "examples" / "demo" / "docs"


def _write_json(path, value):
    path.write_text(json.dumps(value, ensure_ascii=False, indent=2) + "\n", encoding="utf-8")


def create_demo(root):
    """Create data/ and config.json; refuse any existing destination, even empty."""
    root = Path(root).resolve()
    data_path = root / "data"
    config_path = root / "config.json"
    for path in (data_path, config_path):
        if os.path.lexists(path):
            raise FileExistsError(f"Refusing to overwrite existing path: {path}")

    # Load the standalone indexer, not app/__init__.py (which expects a database).
    spec = importlib.util.spec_from_file_location("demo_indexer", REPO_ROOT / "app" / "indexer.py")
    indexer = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(indexer)

    config = {
        "_comment": "Synthetic local demo only. Bind to 127.0.0.1. AI Q&A is not configured.",
        "site_url": "http://127.0.0.1:8000",
        "llm_url": "",
        "llm_model": "",
        "llm_key": "",
        "llm_fallbacks": [],
        "gitee_embed_url": "",
        "embed_model": "",
        "gitee_key": "",
        "auth": {"required": False, "secret": secrets.token_hex(32), "open_registration": False},
        "security": {"allowed_origins": []},
        "smtp": {"enabled": False},
        "cloudflare": {"turnstile_sitekey": "", "turnstile_secret": "", "trusted_proxies": []},
        "oauth": {"linuxdo": {"client_id": "", "client_secret": ""}},
    }
    root.mkdir(parents=True, exist_ok=True)
    with tempfile.TemporaryDirectory(prefix=".demo-build-", dir=root) as stage_name:
        staged_data = Path(stage_name) / "data"
        shutil.copytree(EXAMPLE_DOCS, staged_data / "docs")
        (staged_data / "source-files").mkdir()
        indexer.build(str(staged_data / "kb.db"), str(staged_data / "docs"),
                      do_embed=False, collection="kks")
        with closing(sqlite3.connect(staged_data / "kb.db")) as connection:
            docs = [{"path": path, "title": title, "chars": chars, "col": "kks", "loose": False}
                    for path, title, chars in connection.execute(
                        "SELECT path, title, chars FROM doc ORDER BY path")]
        _write_json(staged_data / "manual.json", {
            "built": "local demo", "total": len(docs), "manual_total": len(docs),
            "ref_total": 0, "dropped": 0, "reference": [],
            "chapters": [{"id": "demo", "title": "本地演示", "intro": "纯自造示例：阅读、搜索与目录导航。",
                          "count": len(docs), "chars": sum(doc["chars"] for doc in docs), "docs": docs}],
        })
        _write_json(staged_data / "doc_metadata.json", {
            "documents": {"kks:" + doc["path"]: {
                "title": doc["title"], "content_type": "explanation", "source_type": "site_curation",
                "curation_level": "curated", "verification_status": "unverified", "image_status": "text_only",
                "scope_note": "仓库自造演示文本；不是游戏实测、原始教材或推荐参数。",
            } for doc in docs},
        })

        # Exclusive creation protects existing files, including a second concurrent run.
        config_created = data_created = False
        try:
            with config_path.open("x", encoding="utf-8") as config_file:
                config_created = True
                if os.name != "nt":
                    os.chmod(config_path, 0o600)
                json.dump(config, config_file, ensure_ascii=False, indent=2)
                config_file.write("\n")
            data_path.mkdir()
            data_created = True
            shutil.copytree(staged_data, data_path, dirs_exist_ok=True)
        except BaseException:
            # Only remove destinations this invocation created; never existing data.
            if data_created:
                shutil.rmtree(data_path)
            if config_created:
                config_path.unlink()
            raise
    return len(docs)


def main(argv=None):
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--root", type=Path, default=REPO_ROOT,
                        help="Destination containing data/ and config.json (default: repository root).")
    args = parser.parse_args(argv)
    try:
        count = create_demo(args.root)
    except (FileExistsError, OSError, ImportError) as error:
        parser.exit(1, f"Demo setup failed: {error}\n")
    print(f"Created {count} synthetic demo documents in {args.root.resolve()}.")
    print("Local reading and keyword search are ready. AI Q&A is not configured.")
    print("From the repository root, run:")
    print("  python -m uvicorn app:app --app-dir app --host 127.0.0.1 --port 8000")
    if args.root.resolve() != REPO_ROOT:
        print("--root only relocates data/config; to serve it, that root also needs a copy of app/.")
    print("Open http://127.0.0.1:8000/manual or http://127.0.0.1:8000/search?q=RimLight")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
