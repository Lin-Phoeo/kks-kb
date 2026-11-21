#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""publish / rollback / prune + resolve_current。

current/previous 是仓库根下的指针：
- Linux：符号链接（临时符号链接 + rename 原子替换）
- Windows：文本指针文件（内容为 release id，原子替换）
resolve_current 统一读取两种形态。
发布/回滚绝不删除旧发布目录；prune 才显式清理未引用的旧目录。
"""
import os
import shutil
from pathlib import Path

from .verify import run_verify


def _release_id_of(path):
    return None if path is None else Path(path).name


def resolve_current(root):
    return _resolve_pointer(Path(root) / "current", root)


def resolve_previous(root):
    return _resolve_pointer(Path(root) / "previous", root)


def _resolve_pointer(ptr, root):
    root = Path(root)
    if not os.path.lexists(str(ptr)):
        return None
    if os.path.islink(str(ptr)):
        target = os.readlink(str(ptr))
        t = Path(target)
        return t if t.is_absolute() else root / t
    text = Path(ptr).read_text(encoding="utf-8").strip()
    if not text:
        return None
    t = Path(text)
    # 文本指针存的是 release id（releases/ 下的目录名）
    return t if t.is_absolute() else root / "releases" / text


def _atomic_write_pointer(ptr, value):
    tmp = Path(str(ptr) + ".tmp")
    tmp.write_text(value, encoding="utf-8")
    os.replace(str(tmp), str(ptr))


def _atomic_symlink_pointer(ptr, target_rel):
    tmp = Path(str(ptr) + ".tmp")
    if os.path.lexists(str(tmp)):
        os.remove(str(tmp))
    os.symlink(target_rel, str(tmp))
    os.replace(str(tmp), str(ptr))


def _write_pointer(root, name, release_id):
    ptr = Path(root) / name
    if release_id is None:
        if os.path.lexists(str(ptr)):
            os.remove(str(ptr))
        return
    if os.name == "nt":
        _atomic_write_pointer(ptr, release_id)
    else:
        try:
            _atomic_symlink_pointer(ptr, os.path.join("releases", release_id))
        except OSError:
            _atomic_write_pointer(ptr, release_id)


def publish(paths, release_id):
    """先 verify，再原子切 current；旧 current 变 previous。"""
    rel = paths.release_dir(release_id)
    if not rel.is_dir():
        raise FileNotFoundError(f"发布目录不存在：{rel}")
    run_verify(paths, release_id)
    old_cur = resolve_current(paths.root)
    _write_pointer(paths.root, "previous", _release_id_of(old_cur))
    _write_pointer(paths.root, "current", release_id)
    return old_cur


def rollback(paths):
    """current/previous 互换。"""
    prev = resolve_previous(paths.root)
    curr = resolve_current(paths.root)
    if prev is None or not Path(prev).is_dir():
        raise RuntimeError(f"没有可回滚的 previous：{prev}")
    _write_pointer(paths.root, "current", _release_id_of(prev))
    _write_pointer(paths.root, "previous", _release_id_of(curr))


def prune(paths):
    """删除未被 current/previous 引用的旧发布目录。"""
    keep = {x for x in (_release_id_of(resolve_current(paths.root)),
                        _release_id_of(resolve_previous(paths.root))) if x}
    removed = []
    if paths.releases.is_dir():
        for child in sorted(paths.releases.iterdir()):
            if child.is_dir() and child.name not in keep:
                shutil.rmtree(child)
                removed.append(child.name)
    return removed
