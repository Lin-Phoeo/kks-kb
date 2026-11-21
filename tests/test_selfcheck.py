#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""ops/deploy/selfcheck.py 的判定逻辑（纯函数部分）。"""
import importlib.util
import os
import unittest

_p = os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", "ops", "deploy", "selfcheck.py")
_spec = importlib.util.spec_from_file_location("selfcheck", _p)
sc = importlib.util.module_from_spec(_spec)
_spec.loader.exec_module(sc)

HEALTHY = {"service_active": True, "healthz": {"ok": True, "chunks": 10, "vec": [10, 1024]},
           "disk_free_pct": 60, "mem_avail_mb": 1500, "backup_age_h": 5, "cert_days": {}}


def m(**kw):
    d = dict(HEALTHY)
    d.update(kw)
    return d


class EvaluateTests(unittest.TestCase):
    def test_healthy(self):
        self.assertEqual(sc.evaluate(HEALTHY), [])

    def test_service_down(self):
        self.assertIn("kb 服务未运行", sc.evaluate(m(service_active=False)))

    def test_healthz_unreachable(self):
        self.assertIn("healthz 无响应", sc.evaluate(m(healthz=None)))

    def test_vec_chunk_mismatch_is_caught(self):
        p = sc.evaluate(m(healthz={"ok": True, "chunks": 10, "vec": [9, 1024]}))
        self.assertTrue(any("向量行数" in x for x in p))

    def test_proxied_healthz_without_sizes_is_fine(self):
        self.assertEqual(sc.evaluate(m(healthz={"ok": True})), [])

    def test_disk_mem_backup_cert(self):
        p = sc.evaluate(m(disk_free_pct=5, mem_avail_mb=100, backup_age_h=70,
                          cert_days={"1.2.3.4": 1.5}))
        self.assertEqual(len(p), 4)

    def test_never_backed_up(self):
        self.assertIn("从未成功备份", sc.evaluate(m(backup_age_h=None)))

    def test_boundaries(self):
        self.assertEqual(sc.evaluate(m(backup_age_h=sc.BACKUP_MAX_AGE_H)), [])
        self.assertNotEqual(sc.evaluate(m(backup_age_h=sc.BACKUP_MAX_AGE_H + 0.1)), [])

    def test_output_is_sorted_for_stable_comparison(self):
        p = sc.evaluate(m(service_active=False, healthz=None, backup_age_h=None))
        self.assertEqual(p, sorted(p))


class DecideTests(unittest.TestCase):
    def test_healthy_and_quiet(self):
        act, st = sc.decide([], {}, 1000)
        self.assertIsNone(act)
        self.assertEqual(st["problems"], [])

    def test_new_problem_mails_once_then_stays_quiet(self):
        act, st = sc.decide(["a"], {"problems": [], "last_mail": 0}, 1000)
        self.assertEqual(act, "new")
        act, st = sc.decide(["a"], st, 1000 + 60)
        self.assertIsNone(act)

    def test_reminder_after_24h(self):
        _, st = sc.decide(["a"], {}, 1000)
        act, _ = sc.decide(["a"], st, 1000 + sc.RENOTIFY_S)
        self.assertEqual(act, "reminder")

    def test_changed_problem_set_mails_immediately(self):
        _, st = sc.decide(["a"], {}, 1000)
        act, _ = sc.decide(["a", "b"], st, 1001)
        self.assertEqual(act, "new")

    def test_recovery_mails_once(self):
        _, st = sc.decide(["a"], {}, 1000)
        act, st = sc.decide([], st, 2000)
        self.assertEqual(act, "recovered")
        act, _ = sc.decide([], st, 3000)
        self.assertIsNone(act)


if __name__ == "__main__":
    unittest.main()
