"""Run every test module in a fresh process and fail on unexpected skips.

Fresh processes keep legacy app singletons from leaking between fixture modules.
The browser test runs separately with --browser and a fresh Chromium instance.
"""
import argparse
from datetime import datetime, timezone
import importlib.util
import json
import os
from pathlib import Path
import subprocess
import sys
import tempfile
import unittest

ROOT = Path(__file__).resolve().parents[1]
BROWSER_TESTS = {
    'test_home_discovery.HomeDiscoveryTests.test_browser_home_discovery_search_assistant_resume_and_layout',
    'test_catalog_browser.CatalogBrowserTests.test_browser_catalog_filter_clear_count_and_empty_state',
    'test_catalog_browser.CatalogBrowserTests.test_browser_search_submit_readable_results_and_safe_highlights',
    'test_onboarding.OnboardingTests.test_browser_help_highlights_visible_page_controls',
    'test_reader_browser.ReaderBrowserTests.test_browser_reader_header_keeps_evidence_without_hiding_the_first_paragraph',
    'test_reader_browser.ReaderBrowserTests.test_browser_all_anchor_entry_points_clear_both_sticky_bars',
    'test_reader_browser.ReaderBrowserTests.test_browser_mobile_outline_collapses_after_selection_and_respects_large_text',
    'test_reader_browser.ReaderBrowserTests.test_browser_folded_anchor_opens_only_ancestors_and_survives_history',
}
BROWSER_ONLY_MODULES = {'test_reader_browser', 'test_catalog_browser'}


def leaves(suite):
    for test in suite:
        if isinstance(test, unittest.TestSuite):
            yield from leaves(test)
        else:
            yield test


def worker(module, output, browser):
    sys.path[:0] = [str(ROOT / 'tests'), str(ROOT)]
    with tempfile.TemporaryDirectory(prefix='kks-tests-') as directory:
        os.environ['KB_GAME_ROOT'] = str(Path(directory) / 'game')
        import jieba
        jieba.dt.tmp_dir = directory
        suite = unittest.defaultTestLoader.loadTestsFromName(module)
        selected = [test for test in leaves(suite) if (test.id() in BROWSER_TESTS) == browser]
        result = unittest.TextTestRunner(verbosity=2).run(unittest.TestSuite(selected))
        report = {'module': module, 'tests_run': result.testsRun,
                  'failures': len(result.failures), 'errors': len(result.errors),
                  'skipped': [{'test': test.id(), 'reason': why} for test, why in result.skipped]}
        output.write_text(json.dumps(report, indent=2) + '\n', encoding='utf-8')
        return 0 if result.wasSuccessful() and not result.skipped and result.testsRun else 1


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--output', type=Path, default=Path('test-results'))
    parser.add_argument('--browser', action='store_true')
    parser.add_argument('--module', help='Internal child-process module selection')
    args = parser.parse_args()
    if args.module:
        return worker(args.module, args.output, args.browser)
    args.output.mkdir(parents=True, exist_ok=True)
    modules = sorted({name.split('.')[0] for name in BROWSER_TESTS}) if args.browser else [p.stem for p in sorted((ROOT / 'tests').glob('test_*.py')) if p.stem not in BROWSER_ONLY_MODULES]
    env = dict(os.environ, PYTHONDONTWRITEBYTECODE='1', PYTHONUTF8='1')
    if args.browser:
        env['KKS_TEST_BROWSER_CHANNEL'] = 'chromium'
    else:
        env.pop('KKS_TEST_BROWSER_CHANNEL', None)
    reports = []
    for module in modules:
        # Chromium puts its Unix-domain socket under TMPDIR. Deep checkout paths
        # exceed sockaddr_un limits even when ordinary files fit there.
        runtime_parent = '/tmp' if os.name == 'posix' else args.output.resolve()
        runtime = tempfile.TemporaryDirectory(prefix='kks-', dir=runtime_parent)
        env.update(TMP=runtime.name, TEMP=runtime.name, TMPDIR=runtime.name)
        result_path = args.output.resolve() / (module + '.json')
        cmd = [sys.executable, '-X', 'utf8', '-B', str(Path(__file__).resolve()),
               '--module', module, '--output', str(result_path)]
        if args.browser:
            cmd.append('--browser')
        try:
            run = subprocess.run(cmd, cwd=ROOT, env=env, text=True, capture_output=True,
                                 encoding='utf-8', errors='replace', timeout=300)
            log = run.stdout + run.stderr
            report = json.loads(result_path.read_text(encoding='utf-8')) if result_path.exists() else {'module': module, 'tests_run': 0, 'failures': 0, 'errors': 1, 'skipped': []}
            report.update(exit_code=run.returncode, command=cmd)
        except subprocess.TimeoutExpired as error:
            log = 'Test module exceeded 300 seconds: ' + str(error)
            report = {'module': module, 'tests_run': 0, 'failures': 0, 'errors': 1, 'skipped': [], 'exit_code': 124, 'command': cmd}
        finally:
            # Child termination releases any legacy fixture's unclosed SQLite handle.
            runtime.cleanup()
        (args.output / (module + '.log')).write_text(log, encoding='utf-8')
        reports.append(report)
        print(f"{module}: exit={report['exit_code']} tests={report['tests_run']} failures={report['failures']} errors={report['errors']} skipped={len(report['skipped'])}", flush=True)
    summary = {'timestamp_utc': datetime.now(timezone.utc).isoformat(), 'python': sys.version,
               'browser': args.browser, 'tests_run': sum(r['tests_run'] for r in reports),
               'failures': sum(r['failures'] for r in reports), 'errors': sum(r['errors'] for r in reports),
               'skipped': [s for r in reports for s in r['skipped']], 'modules': reports}
    summary['passed'] = all(r['exit_code'] == 0 for r in reports)
    (args.output / 'summary.json').write_text(json.dumps(summary, indent=2) + '\n', encoding='utf-8')
    return 0 if summary['passed'] else 1


if __name__ == '__main__':
    raise SystemExit(main())
