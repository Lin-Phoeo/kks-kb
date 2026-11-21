"""Reject private/runtime artifacts before publishing the source repository."""
from pathlib import Path
import re
import subprocess
import sys

ROOT = Path(__file__).resolve().parents[1]
ALLOWED_DIRS = {'.github', 'app', 'docs', 'examples', 'eval', 'ops', 'pipeline', 'scripts', 'tests'}
ALLOWED_ROOT = {'.gitignore', '.gitattributes', '.gitleaks.toml', 'README.md', 'AGENTS.md',
                'CONTRIBUTING.md', 'SECURITY.md', 'THIRD_PARTY_NOTICES.md', 'LICENSE',
                'pyproject.toml', 'config.example.json'}
FORBIDDEN_PARTS = {'.git', 'data', 'game', 'backups', 'research', 'handoff', '__pycache__',
                   'node_modules', 'UserData', 'abdata', 'mods', 'BepInEx'}
FORBIDDEN_EXTENSIONS = {'.db', '.sqlite', '.sqlite3', '.npy', '.docx', '.pdf', '.zip', '.zstd',
                        '.dmp', '.log', '.exe', '.dll', '.pem', '.key', '.p12'}


def check():
    result = subprocess.run(['git', 'ls-files', '-z', '--cached', '--others', '--exclude-standard'],
                            cwd=ROOT, capture_output=True, check=True)
    names = sorted(set(n.decode('utf-8') for n in result.stdout.split(b'\0') if n))
    problems = []
    for name in names:
        path = Path(name)
        if (path.parts[0] not in ALLOWED_DIRS and name not in ALLOWED_ROOT
                and not re.fullmatch(r'requirements(?:[.-][a-zA-Z0-9_-]+)*\.(?:lock|in|txt)', name)):
            problems.append((name, 'not in public source allowlist'))
        if any(p in FORBIDDEN_PARTS or p.startswith('.venv') for p in path.parts):
            problems.append((name, 'private or generated directory'))
        if path.suffix.lower() in FORBIDDEN_EXTENSIONS or path.name in {'config.json', '.env'}:
            problems.append((name, 'private or runtime artifact'))
        if path.name.startswith('.env.') and path.name != '.env.example':
            problems.append((name, 'environment configuration'))
        if 'credential' in path.name.lower() or path.name.lower().startswith('cookies'):
            problems.append((name, 'credential/session file name'))
        file = ROOT / path
        if file.is_symlink():
            problems.append((name, 'symlinks need explicit publication review'))
        if file.is_file() and file.stat().st_size > 3 * 1024 * 1024:
            problems.append((name, 'file exceeds reviewed source size limit'))
    for name, reason in problems:
        print(f'{name}: {reason}')
    print(f'Public source scope: {len(names)} files, {len(problems)} violations')
    return 1 if problems else 0


if __name__ == '__main__':
    sys.exit(check())
