#!/usr/bin/env python3
"""Устанавливает скилл один раз на машину и подключает его всем агентам.

Канон: ~/.agents/skills/google-play-publish. ~/.claude/skills и ~/.codex/skills получают ссылку на него
(symlink, на Windows junction). Прежние копии уходят в ~/.agents/skills/.backup/<время>/, приватные файлы
assets/.env* переносятся в новую копию. Обновляя скилл дальше, правь только канон: ссылки видят изменения сразу.

Архив: четыре соседние папки google-play-publish/, video-script/, remotion-motion-graphics/,
remotion-best-practices/, каждая с SKILL.md. Запусти google-play-publish/scripts/install_shared.py:
присутствующие соседние скиллы тоже получат канон и ссылки; проектные копии убираются только у издателя.

  python install_shared.py                              # канон из этой папки + ссылки агентам
  python install_shared.py --drop-project-copies DIR... # убрать устаревшие проектные копии (в backup)
"""
import argparse
import os
import shutil
import sys
import time
from pathlib import Path

NAME = "google-play-publish"
BUNDLED = ("video-script", "remotion-motion-graphics", "remotion-best-practices")
PRIVATE = (".env", ".env.superleo", ".env.sdk.local")  # в assets/, хранят ключи этой машины
AGENT_DIRS = (".claude", ".codex")  # ссылка создаётся, только если агент установлен
PROJECT_SKILL_DIRS = (".agents/skills", ".claude/skills", ".codex/skills")


def same(a, b):
    return os.path.normcase(os.path.realpath(a)) == os.path.normcase(os.path.realpath(b))


def is_link(p):
    return os.path.islink(p) or os.path.normcase(os.path.realpath(p)) != os.path.normcase(os.path.abspath(p))


def drop_link(p):
    try:
        os.rmdir(p)  # Windows junction
    except OSError:
        os.unlink(p)  # POSIX symlink


def make_link(target, path):
    if os.name == "nt":
        import _winapi
        _winapi.CreateJunction(str(target), str(path))
    else:
        os.symlink(target, path, target_is_directory=True)


def retire(path, dest):
    """Убрать копию с пути: ссылку удалить, каталог переместить в backup."""
    if is_link(path):
        drop_link(path)
        return "link removed"
    dest.parent.mkdir(parents=True, exist_ok=True)
    shutil.move(str(path), str(dest))
    return "moved to " + str(dest)


def install(source, home, stamp, name=NAME):
    skills = home / ".agents" / "skills"
    canonical = skills / name
    backup = skills / ".backup" / stamp
    if not same(source, canonical):
        new = skills / (name + ".new")
        if new.exists():
            shutil.rmtree(new)
        skills.mkdir(parents=True, exist_ok=True)
        shutil.copytree(source, new, ignore=shutil.ignore_patterns("__pycache__", "*.pyc"))
        if canonical.exists():
            for private in PRIVATE if name == NAME else ():
                old = canonical / "assets" / private
                if old.is_file() and not (new / "assets" / private).exists():
                    (new / "assets").mkdir(exist_ok=True)
                    shutil.copy2(old, new / "assets" / private)
            backup.mkdir(parents=True, exist_ok=True)
            shutil.move(str(canonical), str(backup / name))
        os.replace(new, canonical)
        print("canonical installed:", canonical)
    else:
        print("canonical already is the source:", canonical)
    for agent in AGENT_DIRS:
        if not (home / agent).is_dir():
            print("skip (agent not installed):", agent)
            continue
        folder = home / agent / "skills"
        folder.mkdir(exist_ok=True)
        link = folder / name
        if os.path.lexists(link):
            if same(link, canonical):
                print("already linked:", link)
                continue
            print("replaced", link, "-", retire(link, backup / agent.strip(".") / name))
        make_link(canonical, link)
        print("linked:", link, "->", canonical)
    return canonical


def drop_project_copies(projects, home, stamp):
    backup = home / ".agents" / "skills" / ".backup" / stamp / "projects"
    for project in projects:
        root = Path(project).resolve()
        for sub in PROJECT_SKILL_DIRS:
            path = root / sub / NAME
            if not os.path.lexists(path):
                continue
            private = [p.name for p in (path / "assets").glob(".env*") if p.is_file() and not p.name.endswith(".example")]
            print("project copy", path, "-", retire(path, backup / (root.name + "_" + sub.replace("/", "_")) / NAME))
            if private:
                print("  note: it had private files", private, "- they are in the backup, not in the shared copy")


def main(argv=None):
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument("--source", default=str(Path(__file__).resolve().parent.parent), help="skill folder to install")
    parser.add_argument("--drop-project-copies", nargs="+", default=[], metavar="DIR", help="project roots whose own copies are retired")
    args = parser.parse_args(argv)
    source = Path(args.source).resolve()
    if not (source / "SKILL.md").is_file():
        sys.exit("no SKILL.md in source: " + str(source))
    home = Path.home()
    stamp = time.strftime("%Y%m%d-%H%M%S")
    install(source, home, stamp)
    for name in BUNDLED:
        sibling = source.parent / name
        if (sibling / "SKILL.md").is_file():
            install(sibling, home, stamp, name)
    drop_project_copies(args.drop_project_copies, home, stamp)


if __name__ == "__main__":
    main()
