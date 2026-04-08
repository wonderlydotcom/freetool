#!/usr/bin/env python3
from __future__ import annotations

import argparse
import re
import subprocess
import sys
import xml.etree.ElementTree as ET
from pathlib import Path, PurePosixPath
from typing import Iterable

BACKEND_SOURCE_EXTENSIONS = {".cs", ".fs"}
FRONTEND_SOURCE_EXTENSIONS = {".js", ".jsx", ".ts", ".tsx"}
FRONTEND_IGNORED_FILE_NAMES = {"schema.d.ts", "setupTests.ts", "vite-env.d.ts"}
FRONTEND_IMPORT_LIST_ITEM = re.compile(r"^(type\s+)?[A-Za-z_$][\w$]*,$")
HUNK_HEADER = re.compile(r"^@@ -\d+(?:,\d+)? \+(?P<new_start>\d+)(?:,\d+)? @@")


def repository_root() -> Path:
    return Path(__file__).resolve().parent.parent


def to_posix_path(path: str | Path) -> str:
    return PurePosixPath(str(path)).as_posix()


def normalize_changed_path(path: str) -> str:
    return to_posix_path(path).lstrip("./")


def is_backend_source_file(relative_path: str) -> bool:
    path = PurePosixPath(relative_path)
    return (
        relative_path.startswith("src/")
        and path.suffix.lower() in BACKEND_SOURCE_EXTENSIONS
        and "test" not in {part.lower() for part in path.parts}
    )


def is_frontend_source_file(relative_path: str) -> bool:
    path = PurePosixPath(relative_path)

    if not relative_path.startswith("www/src/"):
        return False

    if path.suffix.lower() not in FRONTEND_SOURCE_EXTENSIONS:
        return False

    if path.name in FRONTEND_IGNORED_FILE_NAMES:
        return False

    return ".test." not in path.name and ".spec." not in path.name


def resolve_repo_relative_path(candidate: str, repo_root: Path) -> str:
    normalized = normalize_changed_path(candidate)

    if normalized.startswith("www/src/"):
        return normalized

    absolute_candidate = Path(candidate)
    if absolute_candidate.is_absolute():
        try:
            return to_posix_path(absolute_candidate.resolve().relative_to(repo_root))
        except ValueError:
            return to_posix_path(absolute_candidate.resolve())

    if normalized.startswith("src/"):
        return f"www/{normalized}"

    return normalized


def selected_changed_files(changed_files: Iterable[str], predicate) -> list[str]:
    return sorted(
        {
            normalized
            for normalized in (normalize_changed_path(path) for path in changed_files)
            if predicate(normalized)
        }
    )


def read_diff(relative_path: str, diff_base: str, repo_root: Path) -> list[str]:
    completed = subprocess.run(
        [
            "git",
            "diff",
            "--unified=3",
            "--no-color",
            f"{diff_base}...HEAD",
            "--",
            relative_path,
        ],
        capture_output=True,
        check=False,
        cwd=repo_root,
        text=True,
    )

    if completed.returncode != 0:
        raise RuntimeError(completed.stderr.strip() or f"git diff failed for {relative_path}")

    return completed.stdout.splitlines()


def is_blank_or_comment(stripped_line: str) -> bool:
    return (
        stripped_line == ""
        or stripped_line.startswith("//")
        or stripped_line.startswith("/*")
        or stripped_line.startswith("*")
        or stripped_line.startswith("*/")
    )


def iter_diff_hunks(diff_lines: list[str]) -> Iterable[tuple[str, list[str]]]:
    current_header: str | None = None
    current_body: list[str] = []

    for diff_line in diff_lines:
        if diff_line.startswith("@@ "):
            if current_header is not None:
                yield current_header, current_body
            current_header = diff_line
            current_body = []
            continue

        if current_header is not None:
            current_body.append(diff_line)

    if current_header is not None:
        yield current_header, current_body


def frontend_import_hunk_line(stripped_line: str) -> bool:
    return (
        stripped_line.startswith("import ")
        or stripped_line.startswith("from ")
        or stripped_line.startswith("} from ")
        or stripped_line in {"{", "}"}
        or FRONTEND_IMPORT_LIST_ITEM.match(stripped_line) is not None
    )


def frontend_hunk_is_import_like(hunk_lines: list[str]) -> bool:
    changed_lines = [
        diff_line[1:].strip()
        for diff_line in hunk_lines
        if diff_line and diff_line[0] in {"+", "-"}
    ]

    if any(
        diff_line
        and diff_line[0] in {" ", "+", "-"}
        and (
            diff_line[1:].strip().startswith("import ")
            or " from " in diff_line[1:]
        )
        for diff_line in hunk_lines
    ):
        return True

    return bool(changed_lines) and all(
        frontend_import_hunk_line(stripped)
        or stripped.startswith(("type ", "interface ", "export type "))
        for stripped in changed_lines
    )


def frontend_line_is_meaningful(stripped_line: str, import_like_hunk: bool) -> bool:
    if is_blank_or_comment(stripped_line):
        return False

    if stripped_line.startswith(("type ", "interface ", "export type ")):
        return False

    if import_like_hunk and frontend_import_hunk_line(stripped_line):
        return False

    return True


def backend_line_is_meaningful(line: str) -> bool:
    stripped = line.strip()

    if is_blank_or_comment(stripped):
        return False

    return not (
        stripped.startswith("open ")
        or stripped.startswith("using ")
        or stripped.startswith("namespace ")
    )


def collect_changed_line_numbers(
    target_files: list[str],
    diff_base: str,
    repo_root: Path,
    label: str,
) -> dict[str, set[int]]:
    files_requiring_coverage: dict[str, set[int]] = {}
    ignored_files: list[str] = []

    for relative_path in target_files:
        diff_lines = read_diff(relative_path, diff_base, repo_root)
        changed_lines: set[int] = set()

        for header, hunk_lines in iter_diff_hunks(diff_lines):
            match = HUNK_HEADER.match(header)
            if match is None:
                continue

            new_line_number = int(match.group("new_start"))
            import_like_hunk = label == "frontend" and frontend_hunk_is_import_like(hunk_lines)

            for diff_line in hunk_lines:
                if not diff_line or diff_line.startswith(("diff --git", "index ", "--- ", "+++ ")):
                    continue

                if diff_line[0] == " ":
                    new_line_number += 1
                    continue

                if diff_line[0] == "-":
                    continue

                if diff_line[0] != "+":
                    continue

                stripped = diff_line[1:].strip()
                is_meaningful = (
                    frontend_line_is_meaningful(stripped, import_like_hunk)
                    if label == "frontend"
                    else backend_line_is_meaningful(diff_line[1:])
                )

                if is_meaningful:
                    changed_lines.add(new_line_number)

                new_line_number += 1

        if changed_lines:
            files_requiring_coverage[relative_path] = changed_lines
        else:
            ignored_files.append(relative_path)

    if ignored_files:
        print(f"Skipping {label} coverage enforcement for non-runtime-only diffs:")
        for relative_path in ignored_files:
            print(f"  - {relative_path}")

    return files_requiring_coverage


def parse_backend_reports(report_paths: list[str], repo_root: Path) -> dict[str, dict[int, bool]]:
    file_lines: dict[str, dict[int, bool]] = {}

    for report_path in report_paths:
        root = ET.parse(report_path).getroot()
        source_roots = [
            Path(source.text).resolve()
            for source in root.findall("./sources/source")
            if source.text and source.text.strip()
        ]

        for class_node in root.findall(".//class"):
            filename = class_node.attrib.get("filename")
            if not filename:
                continue

            relative_path = None
            filename_path = Path(filename)

            if filename_path.is_absolute():
                try:
                    relative_path = to_posix_path(filename_path.resolve().relative_to(repo_root))
                except ValueError:
                    continue
            else:
                for source_root in source_roots:
                    candidate = (source_root / filename).resolve()
                    try:
                        relative_path = to_posix_path(candidate.relative_to(repo_root))
                        break
                    except ValueError:
                        continue

                if relative_path is None:
                    relative_path = normalize_changed_path(filename)

            line_hits = file_lines.setdefault(relative_path, {})
            for line_node in class_node.findall("./lines/line"):
                line_number = int(line_node.attrib["number"])
                covered = int(line_node.attrib.get("hits", "0")) > 0
                line_hits[line_number] = line_hits.get(line_number, False) or covered

    return file_lines


def parse_frontend_lcov(report_path: str, repo_root: Path) -> dict[str, dict[int, bool]]:
    file_lines: dict[str, dict[int, bool]] = {}
    current_file: str | None = None

    with open(report_path, encoding="utf-8") as handle:
        for raw_line in handle:
            line = raw_line.strip()

            if line.startswith("SF:"):
                current_file = resolve_repo_relative_path(line[3:], repo_root)
                file_lines.setdefault(current_file, {})
                continue

            if line == "end_of_record":
                current_file = None
                continue

            if current_file is None or not line.startswith("DA:"):
                continue

            line_number_text, hits_text = line[3:].split(",", 1)
            line_number = int(line_number_text)
            covered = int(hits_text) > 0
            file_lines[current_file][line_number] = file_lines[current_file].get(line_number, False) or covered

    return file_lines


def check_threshold(
    changed_lines_by_file: dict[str, set[int]],
    coverage_by_file: dict[str, dict[int, bool]],
    threshold: float,
    label: str,
) -> int:
    if not changed_lines_by_file:
        print(f"No changed {label} source files require coverage enforcement; skipping threshold check.")
        return 0

    target_files = sorted(changed_lines_by_file)
    missing_files = [path for path in target_files if path not in coverage_by_file]
    if missing_files:
        print(f"Coverage data did not include these changed {label} source files:")
        for path in missing_files:
            print(f"  - {path}")
        return 1

    total_covered = 0
    total_lines = 0
    per_file_results: list[tuple[str, int, int]] = []
    files_without_executable_lines: list[str] = []

    for path in target_files:
        executable_lines = sorted(
            line_number
            for line_number in changed_lines_by_file[path]
            if line_number in coverage_by_file[path]
        )

        if not executable_lines:
            files_without_executable_lines.append(path)
            continue

        covered_lines = sum(1 for line_number in executable_lines if coverage_by_file[path][line_number])
        total_covered += covered_lines
        total_lines += len(executable_lines)
        per_file_results.append((path, covered_lines, len(executable_lines)))

    if files_without_executable_lines:
        print(f"Skipping changed {label} lines that were not executable according to the coverage report:")
        for path in files_without_executable_lines:
            print(f"  - {path}")

    if total_lines == 0:
        print(f"Changed {label} source files had no executable lines in the coverage report; skipping threshold check.")
        return 0

    percent = total_covered / total_lines * 100.0
    print(f"Changed {label} line coverage: {percent:.2f}% ({total_covered}/{total_lines})")

    if percent + 1e-9 >= threshold:
        return 0

    print(f"{label.capitalize()} coverage is below the required {threshold:.2f}% threshold.")
    for path, covered_lines, line_count in per_file_results:
        percent = covered_lines / line_count * 100.0
        print(
            f"  - {path}: {percent:.2f}% "
            f"({covered_lines}/{line_count})"
        )
    return 1


def build_parser() -> argparse.ArgumentParser:
    parser = argparse.ArgumentParser(description="Check coverage for changed files.")
    subparsers = parser.add_subparsers(dest="mode", required=True)

    backend = subparsers.add_parser("backend")
    backend.add_argument("--report", action="append", required=True, dest="reports")
    backend.add_argument("--changed-file", action="append", default=[], dest="changed_files")
    backend.add_argument("--diff-base", required=True)
    backend.add_argument("--threshold", type=float, required=True)

    frontend = subparsers.add_parser("frontend")
    frontend.add_argument("--lcov", required=True)
    frontend.add_argument("--changed-file", action="append", default=[], dest="changed_files")
    frontend.add_argument("--diff-base", required=True)
    frontend.add_argument("--threshold", type=float, required=True)

    return parser


def main() -> int:
    parser = build_parser()
    args = parser.parse_args()
    repo_root = repository_root()

    if args.mode == "backend":
        target_files = selected_changed_files(args.changed_files, is_backend_source_file)
        changed_lines_by_file = collect_changed_line_numbers(target_files, args.diff_base, repo_root, "backend")
        coverage_by_file = parse_backend_reports(args.reports, repo_root)
        return check_threshold(changed_lines_by_file, coverage_by_file, args.threshold, "backend")

    if args.mode == "frontend":
        target_files = selected_changed_files(args.changed_files, is_frontend_source_file)
        changed_lines_by_file = collect_changed_line_numbers(target_files, args.diff_base, repo_root, "frontend")
        coverage_by_file = parse_frontend_lcov(args.lcov, repo_root)
        return check_threshold(changed_lines_by_file, coverage_by_file, args.threshold, "frontend")

    parser.error(f"Unsupported mode: {args.mode}")
    return 2


if __name__ == "__main__":
    sys.exit(main())
