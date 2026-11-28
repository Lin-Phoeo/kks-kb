"""Pure Windows path checks for offline build output; no filesystem writes."""
from pathlib import PureWindowsPath


def output_path_error(output, repository, game):
    output, repository, game = (PureWindowsPath(str(p)) for p in (output, repository, game))
    if output == repository or repository in output.parents:
        return "Output must be outside the whole repository"
    if output == game:
        return "Output must not be the game root"
    if game in output.parents:
        first = output.relative_to(game).parts[0].casefold()
        if first in {"bepinex", "userdata", "mods", "abdata"} or first.endswith("_data"):
            return "Output must not be a game runtime or content directory"
    return None


def path_guard_checks():
    repository = PureWindowsPath("C:/Review/checkout")
    game = PureWindowsPath("D:/Games/KKS")
    cases = (
        ("rejectRepositorySiblingModule", "c:/review/CHECKOUT/docs/generated", False),
        ("rejectMixedCaseBepInEx", "d:/games/kks/bEpInEx/plugins/output", False),
        ("rejectAnyGameDataDirectory", "D:/Games/KKS/KoikatsuSunshine_dAtA/Managed/output", False),
        ("rejectGameRoot", "d:/GAMES/kks", False),
        ("allowExternalArtifacts", "E:/BuildArtifacts/aligned", True),
        ("allowGameScratch", "D:/Games/KKS/.scratch/aligned-review", True),
    )
    return {name: (output_path_error(path, repository, game) is None) == expected for name, path, expected in cases}


if __name__ == "__main__":
    import json
    checks = path_guard_checks()
    print(json.dumps({"checks": len(checks), "passed": all(checks.values()), "cases": checks}, indent=2))
    raise SystemExit(0 if all(checks.values()) else 1)
