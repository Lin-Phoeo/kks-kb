#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""kbpipe CLI：python -m pipeline.kbpipe <subcommand> [flags]。

子命令：scan chunk embed index vectors bundle verify publish rollback prune all
all = scan → chunk → embed → index → vectors → bundle（不含 publish）。
"""
import argparse
import json
import os
import sys
import time

from .config import Paths, load_config
from .scan import run_scan
from .chunk import run_chunk
from .embed import count_missing, make_http_embedder, run_embed
from .index import run_index
from .vectors import run_vectors
from .bundle import run_bundle
from .verify import VerifyError, run_verify
from .publish import prune, publish, resolve_current, rollback


def gen_release_id(paths):
    base = time.strftime("%Y%m%d-%H%M%S")
    rid, i = base, 1
    while paths.release_dir(rid).exists():
        i += 1
        rid = f"{base}-{i}"
    return rid


def _embedder_or_none(args, cfg):
    model = args.model or cfg["embed_model"]
    if args.no_embed:
        return None, model
    key = os.environ.get("GITEE_AI_KEY", "")
    if not key:
        raise RuntimeError("缺少环境变量 GITEE_AI_KEY")
    return make_http_embedder(cfg["embed_url"], model, key), model


def _cmd_all(args, cfg, paths):
    files = run_scan(paths, cfg["collections"])
    n_chunks = run_chunk(paths, files)
    model = args.model or cfg["embed_model"]
    if args.dry_run:
        missing = count_missing(paths, model)
        print(json.dumps({"scanned": len(files), "chunks": n_chunks,
                          "new_embeds": missing}, ensure_ascii=False))
        return 0
    batch = args.batch or cfg["batch"]
    if not args.no_embed:
        embedder, _ = _embedder_or_none(args, cfg)
        run_embed(paths, embedder, model, batch)
    run_index(paths, cfg["collections"])
    if not args.no_embed:
        run_vectors(paths, model)
    release = args.release or gen_release_id(paths)
    run_bundle(paths, cfg, release, no_embed=args.no_embed)
    print(release)
    return 0


def main(argv=None):
    common = argparse.ArgumentParser(add_help=False)
    common.add_argument("--root", default=os.getcwd(),
                        help="仓库根目录（含 col/、build/、releases/）")
    common.add_argument("--config", help="sources.json 路径（默认 <root>/pipeline/sources.json）")
    common.add_argument("--release", help="发布 id（bundle/verify/publish 用）")
    common.add_argument("--model", help="覆盖 sources.json 的 embed_model")
    common.add_argument("--batch", type=int, help="覆盖 sources.json 的 batch")
    common.add_argument("--dry-run", action="store_true",
                        help="仅 scan+chunk，报告还需嵌入多少 key")
    common.add_argument("--no-embed", action="store_true", help="出 FTS-only 包")

    ap = argparse.ArgumentParser(prog="kbpipe", parents=[common],
                                 description="可复现的知识库数据流水线")
    sub = ap.add_subparsers(dest="cmd", required=True)
    for name in ("scan", "chunk", "embed", "index", "vectors", "bundle",
                 "verify", "publish", "rollback", "prune", "all"):
        sub.add_parser(name, parents=[common])
    args = ap.parse_args(argv)

    config_path = args.config or os.path.join(args.root, "pipeline", "sources.json")
    cfg = load_config(config_path)
    paths = Paths(args.root)

    if args.dry_run and args.cmd != "all":
        ap.error("--dry-run 只能与 all 一起用")

    try:
        if args.cmd == "all":
            return _cmd_all(args, cfg, paths)
        if args.cmd == "scan":
            files = run_scan(paths, cfg["collections"])
            print(json.dumps({"scanned": len(files)}, ensure_ascii=False))
            return 0
        if args.cmd == "chunk":
            files = run_scan(paths, cfg["collections"])
            print(json.dumps({"chunks": run_chunk(paths, files)}, ensure_ascii=False))
            return 0
        if args.cmd == "embed":
            files = run_scan(paths, cfg["collections"])
            run_chunk(paths, files)
            embedder, model = _embedder_or_none(args, cfg)
            if embedder is None:
                print(json.dumps({"embedded": 0}, ensure_ascii=False))
                return 0
            res = run_embed(paths, embedder, model, args.batch or cfg["batch"])
            print(json.dumps(res, ensure_ascii=False))
            return 0
        if args.cmd == "index":
            print(run_index(paths, cfg["collections"]))
            return 0
        if args.cmd == "vectors":
            model = args.model or cfg["embed_model"]
            print(json.dumps(run_vectors(paths, model), ensure_ascii=False))
            return 0
        if args.cmd == "bundle":
            release = args.release or gen_release_id(paths)
            run_bundle(paths, cfg, release, no_embed=args.no_embed)
            print(release)
            return 0
        if args.cmd == "verify":
            release = args.release or gen_release_id(paths)
            run_verify(paths, release)
            print("ok")
            return 0
        if args.cmd == "publish":
            if not args.release:
                ap.error("publish 需要 --release")
            publish(paths, args.release)
            print(resolve_current(paths.root))
            return 0
        if args.cmd == "rollback":
            rollback(paths)
            print(resolve_current(paths.root))
            return 0
        if args.cmd == "prune":
            print(json.dumps({"removed": prune(paths)}, ensure_ascii=False))
            return 0
        ap.error(f"未知子命令：{args.cmd}")
    except (VerifyError, RuntimeError, FileNotFoundError, ValueError) as e:
        print(f"kbpipe: {e}", file=sys.stderr)
        return 2


if __name__ == "__main__":
    sys.exit(main())
