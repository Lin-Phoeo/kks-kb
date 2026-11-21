#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""服务器自检 + 邮件告警（只用标准库，由 kb-selfcheck.timer 每 5 分钟跑一次）。

检查：服务存活 / healthz（向量行数 == chunk 数）/ 磁盘 / 内存 / 备份新鲜度 / 证书剩余天数。
告警策略：问题集合变化（新增或恢复）时发信；持续异常每 24 小时再提醒一次，不刷屏。
邮件走 config.json 里配置的 SMTP，收件人 = alert_email 或 auth.admin_email。

用法:
  selfcheck.py                 正常运行（发信）
  selfcheck.py --print         只打印结果，不发信、不写状态
  退出码: 0 健康, 1 有问题
"""
import glob
import json
import os
import shutil
import smtplib
import ssl
import subprocess
import sys
import time
import urllib.request
from email.header import Header
from email.mime.text import MIMEText

_ROOT = os.path.abspath(os.path.join(os.path.dirname(__file__), "..", ".."))
CONFIG = os.environ.get("KB_CONFIG", os.path.join(_ROOT, "config.json"))
STATE = os.environ.get("KB_SELFCHECK_STATE", "/var/lib/kb-selfcheck/state.json")
BACKUP_DIR = os.environ.get("KB_BACKUP_DIR", "/var/backups/kb")
DATA_DIR = os.environ.get("KB_DATA_DIR", os.path.join(_ROOT, "data"))
HEALTHZ = os.environ.get("KB_HEALTHZ", "http://127.0.0.1:8000/healthz")

DISK_MIN_FREE_PCT = 15
MEM_MIN_AVAIL_MB = 300
BACKUP_MAX_AGE_H = 36
CERT_MIN_DAYS = 3
RENOTIFY_S = 24 * 3600


def evaluate(m):
    """纯函数：指标 -> 问题列表（稳定排序，便于比较变化）。"""
    p = []
    if not m.get("service_active"):
        p.append("kb 服务未运行")
    hz = m.get("healthz")
    if hz is None:
        p.append("healthz 无响应")
    elif not hz.get("ok"):
        p.append("healthz 报告异常（向量矩阵与 chunk 数不一致？）")
    elif hz.get("vec") is not None and hz.get("chunks") is not None and hz["vec"][0] != hz["chunks"]:
        p.append(f"向量行数 {hz['vec'][0]} != chunk 数 {hz['chunks']}")
    free = m.get("disk_free_pct")
    if free is not None and free < DISK_MIN_FREE_PCT:
        p.append(f"磁盘剩余 {free:.0f}% < {DISK_MIN_FREE_PCT}%")
    mem = m.get("mem_avail_mb")
    if mem is not None and mem < MEM_MIN_AVAIL_MB:
        p.append(f"可用内存 {mem:.0f}MB < {MEM_MIN_AVAIL_MB}MB")
    age = m.get("backup_age_h")
    if age is None:
        p.append("从未成功备份")
    elif age > BACKUP_MAX_AGE_H:
        p.append(f"最近一次成功备份在 {age:.0f} 小时前")
    for name, days in sorted((m.get("cert_days") or {}).items()):
        if days < CERT_MIN_DAYS:
            p.append(f"证书 {name} 剩余 {days:.1f} 天")
    return sorted(p)


def decide(problems, state, now):
    """纯函数：是否发信。返回 (动作, 新状态)。动作: None / 'new' / 'recovered' / 'reminder'"""
    last = state.get("problems", [])
    last_mail = state.get("last_mail", 0)
    new_state = {"problems": problems, "last_mail": last_mail}
    if problems == last:
        if problems and now - last_mail >= RENOTIFY_S:
            new_state["last_mail"] = now
            return "reminder", new_state
        return None, new_state
    new_state["last_mail"] = now
    return ("recovered" if not problems else "new"), new_state


# ---------------- 采集（有副作用，不在单测范围） ----------------
def _service_active():
    try:
        r = subprocess.run(["systemctl", "is-active", "kb"], capture_output=True, text=True, timeout=10)
        return r.stdout.strip() == "active"
    except Exception:
        return False


def _healthz():
    try:
        with urllib.request.urlopen(HEALTHZ, timeout=8) as r:
            return json.load(r)
    except Exception:
        return None


def _mem_avail_mb():
    try:
        for line in open("/proc/meminfo"):
            if line.startswith("MemAvailable:"):
                return int(line.split()[1]) / 1024
    except OSError:
        pass
    return None


def _backup_age_h():
    p = os.path.join(BACKUP_DIR, "last_ok")
    try:
        return (time.time() - os.path.getmtime(p)) / 3600
    except OSError:
        return None


def _cert_days():
    out = {}
    for pem in glob.glob("/etc/letsencrypt/live/*/fullchain.pem"):
        try:
            r = subprocess.run(["openssl", "x509", "-noout", "-enddate", "-in", pem],
                               capture_output=True, text=True, timeout=10)
            end = r.stdout.strip().split("=", 1)[1]
            ts = time.mktime(time.strptime(end, "%b %d %H:%M:%S %Y %Z"))
            out[os.path.basename(os.path.dirname(pem))] = (ts - time.time()) / 86400
        except Exception:
            out[os.path.basename(os.path.dirname(pem))] = -1
    return out


def collect():
    du = shutil.disk_usage(DATA_DIR if os.path.isdir(DATA_DIR) else "/")
    return {
        "service_active": _service_active(),
        "healthz": _healthz(),
        "disk_free_pct": du.free / du.total * 100,
        "mem_avail_mb": _mem_avail_mb(),
        "backup_age_h": _backup_age_h(),
        "cert_days": _cert_days(),
    }


def _send(subject, body):
    cfg = json.load(open(CONFIG, encoding="utf-8"))
    smtp = cfg.get("smtp", {})
    to = cfg.get("alert_email") or cfg.get("auth", {}).get("admin_email")
    if not (smtp.get("user") and to):
        print("[selfcheck] 未配置 SMTP 或收件人，无法发信", file=sys.stderr)
        return False
    msg = MIMEText(body, "plain", "utf-8")
    msg["Subject"] = Header(subject, "utf-8")
    msg["From"] = smtp.get("from") or smtp["user"]
    msg["To"] = to
    ctx = ssl.create_default_context()
    with smtplib.SMTP_SSL(smtp["host"], int(smtp.get("port", 465)), context=ctx, timeout=20) as s:
        s.login(smtp["user"], smtp["pass"])
        s.sendmail(msg["From"], [to], msg.as_string())
    return True


def main(argv):
    dry = "--print" in argv
    problems = evaluate(collect())
    if problems:
        print("问题:\n  - " + "\n  - ".join(problems))
    else:
        print("健康")
    if dry:
        return 1 if problems else 0

    try:
        state = json.load(open(STATE, encoding="utf-8"))
    except (OSError, ValueError):
        state = {}
    action, new_state = decide(problems, state, time.time())
    if action:
        host = os.uname().nodename if hasattr(os, "uname") else "kb"
        if action == "recovered":
            subject, body = f"[KKS-KB] 已恢复 ({host})", "之前的问题已全部恢复。"
        else:
            tag = "提醒" if action == "reminder" else "告警"
            subject = f"[KKS-KB] {tag}: {len(problems)} 个问题 ({host})"
            body = "\n".join(f"- {x}" for x in problems)
        try:
            if not _send(subject, body):
                new_state["last_mail"] = state.get("last_mail", 0)   # 没发出去，下次还要试
        except Exception as e:
            print(f"[selfcheck] 发信失败: {type(e).__name__}: {e}", file=sys.stderr)
            new_state["last_mail"] = state.get("last_mail", 0)
            new_state["problems"] = state.get("problems", [])        # 保持"未通知"状态
    os.makedirs(os.path.dirname(STATE), exist_ok=True)
    tmp = STATE + ".tmp"
    json.dump(new_state, open(tmp, "w", encoding="utf-8"))
    os.replace(tmp, STATE)
    return 1 if problems else 0


if __name__ == "__main__":
    sys.exit(main(sys.argv[1:]))
