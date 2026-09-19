#!/usr/bin/env python3
"""Снимает тайминги старта MarkMello на macOS и Linux — аналог measure-startup.ps1.

Запускает приложение в smoke-режиме (--smoke-exit-after-open), где оно печатает
снимок IStartupMetrics и выходит само. Считает медиану, минимум и максимум по
стадиям плюс пиковую память процесса (max RSS из /usr/bin/time; на Windows
measure-startup.ps1 берёт Working Set, поэтому цифры между ОС не сравниваются).

С --folder приложение дополнительно открывает папку (--smoke-open-folder) и
раскрывает первый каталог в дереве.

Опорные значения и методика — docs/implementation-plan-folders-tabs.md, раздел
«Зафиксированные находки M0». Сравнение только парное, в одной сессии: соберите
AOT до и после изменения и прогоните в порядке «до · после · после · до».
Мерить на незанятой машине: параллельная сборка искажает результат на ~15 %.

Пример (приёмка по AOT-сборке):
    dotnet publish src/MarkMello.Desktop/MarkMello.Desktop.csproj -m:1 -c Release -r osx-arm64 \\
      --self-contained true -p:PublishAot=true -p:PublishSingleFile=false -o publish/head-osx-arm64
    tools/measure-startup.py --exe publish/head-osx-arm64/MarkMello --label "AOT head"
"""
import argparse
import os
import platform
import re
import statistics
import subprocess
import sys

STAGES = ["AppBootstrap", "FirstWindow", "DocumentModelReady", "ReadableDocument", "SecondaryFeatures"]
FOLDER_STAGES = ["OpenFolder", "ExpandNode"]


def parse_args():
    parser = argparse.ArgumentParser(description=__doc__.splitlines()[0])
    parser.add_argument("--exe", default="src/MarkMello.Desktop/bin/Release/net10.0/MarkMello")
    parser.add_argument("--document", default="sample.md")
    parser.add_argument("--folder", default="", help="путь папки: включает замер folder mode")
    parser.add_argument("--label", default="Release (JIT)")
    parser.add_argument("--warmups", type=int, default=2)
    parser.add_argument("--runs", type=int, default=10)
    return parser.parse_args()


def time_command():
    # macOS: -l печатает max RSS в байтах; GNU time: -v печатает его в килобайтах.
    if platform.system() == "Darwin":
        return ["/usr/bin/time", "-l"], r"(\d+)\s+maximum resident set size", 1
    return ["/usr/bin/time", "-v"], r"Maximum resident set size \(kbytes\):\s*(\d+)", 1024


def run_once(exe, document, folder):
    prefix, rss_pattern, rss_unit = time_command()
    args = [exe, "--smoke-exit-after-open"]
    if folder:
        args += ["--smoke-open-folder", folder]
    args.append(document)

    process = subprocess.run(prefix + args, capture_output=True, text=True, cwd=os.path.dirname(document))
    result = {"ExitCode": process.returncode}
    for line in process.stdout.splitlines():
        match = re.match(r"^\[(startup|workspace)\]\s+(\w+)\s+([\d.,]+)\s+ms", line)
        if match:
            result[match.group(2)] = float(match.group(3).replace(",", "."))

    match = re.search(rss_pattern, process.stderr)
    result["WorkingSetMb"] = round(int(match.group(1)) * rss_unit / 1024 / 1024, 1) if match else None
    return result


def stat(values):
    values = sorted(value for value in values if value is not None)
    if not values:
        return "n/a"
    return f"median {statistics.median(values):.1f} | min {values[0]:.1f} | max {values[-1]:.1f}"


def main():
    args = parse_args()
    exe = os.path.abspath(args.exe)
    document = os.path.abspath(args.document)
    folder = os.path.abspath(args.folder) if args.folder else ""
    for path in [exe, document] + ([folder] if folder else []):
        if not os.path.exists(path):
            sys.exit(f"не найден: {path}")

    print(f"=== {args.label} ===")
    print(f"exe: {exe}")
    print(f"doc: {document}")
    if folder:
        print(f"folder: {folder}")

    for _ in range(args.warmups):
        run_once(exe, document, folder)

    results = []
    for i in range(1, args.runs + 1):
        run = run_once(exe, document, folder)
        results.append(run)
        folder_part = f" OpenFolder={run.get('OpenFolder')} ExpandNode={run.get('ExpandNode')}" if folder else ""
        print(f"run {i:2}: exit={run['ExitCode']} FirstWindow={run.get('FirstWindow')} "
              f"DocModel={run.get('DocumentModelReady')} Readable={run.get('ReadableDocument')}"
              f"{folder_part} RSS={run['WorkingSetMb']} MB", flush=True)

    print()
    print(f"--- ИТОГ: {args.label}, {args.runs} прогонов (мс от старта процесса; folder-стадии — от начала операции) ---")
    for stage in STAGES + (FOLDER_STAGES if folder else []):
        print(f"{stage:<20} {stat(run.get(stage) for run in results)}")
    print(f"{'Max RSS, MB':<20} {stat(run['WorkingSetMb'] for run in results)}")
    print("exit codes: " + ",".join(str(run["ExitCode"]) for run in results))


if __name__ == "__main__":
    main()
