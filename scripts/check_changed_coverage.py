#!/usr/bin/env python3
from __future__ import annotations

import argparse
import json
import sys
import xml.etree.ElementTree as ET
from dataclasses import dataclass
from pathlib import Path, PurePosixPath
from typing import Iterable

BACKEND_SOURCE_EXTENSIONS = {".cs", ".fs"}
FRONTEND_SOURCE_EXTENSIONS = {".js", ".jsx", ".ts", ".tsx"}
FRONTEND_IGNORED_FILE_NAMES = {"schema.d.ts", "setupTests.ts", "vite-env.d.ts"}


@dataclass(frozen=True)
class FileCoverage:
    covered: int
    total: int

    @property
    def percent(self) -> float:
        if self.total == 0:
            return 0.0

        return self.covered / self.total * 100.0


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


def parse_backend_reports(report_paths: list[str], repo_root: Path) -> dict[str, FileCoverage]:
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

    return {
        relative_path: FileCoverage(
            covered=sum(1 for covered in line_hits.values() if covered),
            total=len(line_hits),
        )
        for relative_path, line_hits in file_lines.items()
    }


def parse_frontend_summary(summary_path: str, repo_root: Path) -> dict[str, FileCoverage]:
    with open(summary_path, encoding="utf-8") as handle:
        payload = json.load(handle)

    coverage_by_file: dict[str, FileCoverage] = {}

    for raw_path, stats in payload.items():
        if raw_path == "total" or not isinstance(stats, dict):
            continue

        lines = stats.get("lines")
        if not isinstance(lines, dict):
            continue

        relative_path = resolve_repo_relative_path(raw_path, repo_root)
        coverage_by_file[relative_path] = FileCoverage(
            covered=int(lines.get("covered", 0)),
            total=int(lines.get("total", 0)),
        )

    return coverage_by_file


def check_threshold(target_files: list[str], coverage_by_file: dict[str, FileCoverage], threshold: float, label: str) -> int:
    if not target_files:
        print(f"No changed {label} source files require coverage enforcement; skipping threshold check.")
        return 0

    missing_files = [path for path in target_files if path not in coverage_by_file]
    if missing_files:
        print(f"Coverage data did not include these changed {label} source files:")
        for path in missing_files:
            print(f"  - {path}")
        return 1

    total_covered = 0
    total_lines = 0

    for path in target_files:
        file_coverage = coverage_by_file[path]
        total_covered += file_coverage.covered
        total_lines += file_coverage.total

    if total_lines == 0:
        print(f"Changed {label} source files had no executable lines in the coverage report; skipping threshold check.")
        return 0

    percent = total_covered / total_lines * 100.0
    print(f"Changed {label} line coverage: {percent:.2f}% ({total_covered}/{total_lines})")

    if percent + 1e-9 >= threshold:
        return 0

    print(f"{label.capitalize()} coverage is below the required {threshold:.2f}% threshold.")
    for path in target_files:
        file_coverage = coverage_by_file[path]
        print(
            f"  - {path}: {file_coverage.percent:.2f}% "
            f"({file_coverage.covered}/{file_coverage.total})"
        )
    return 1


def build_parser() -> argparse.ArgumentParser:
    parser = argparse.ArgumentParser(description="Check coverage for changed files.")
    subparsers = parser.add_subparsers(dest="mode", required=True)

    backend = subparsers.add_parser("backend")
    backend.add_argument("--report", action="append", required=True, dest="reports")
    backend.add_argument("--changed-file", action="append", default=[], dest="changed_files")
    backend.add_argument("--threshold", type=float, required=True)

    frontend = subparsers.add_parser("frontend")
    frontend.add_argument("--summary", required=True)
    frontend.add_argument("--changed-file", action="append", default=[], dest="changed_files")
    frontend.add_argument("--threshold", type=float, required=True)

    return parser


def main() -> int:
    parser = build_parser()
    args = parser.parse_args()
    repo_root = repository_root()

    if args.mode == "backend":
        target_files = selected_changed_files(args.changed_files, is_backend_source_file)
        coverage_by_file = parse_backend_reports(args.reports, repo_root)
        return check_threshold(target_files, coverage_by_file, args.threshold, "backend")

    if args.mode == "frontend":
        target_files = selected_changed_files(args.changed_files, is_frontend_source_file)
        coverage_by_file = parse_frontend_summary(args.summary, repo_root)
        return check_threshold(target_files, coverage_by_file, args.threshold, "frontend")

    parser.error(f"Unsupported mode: {args.mode}")
    return 2


if __name__ == "__main__":
    sys.exit(main())
