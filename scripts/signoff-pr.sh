#!/usr/bin/env bash
set -euo pipefail

ROOT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
cd "$ROOT_DIR"

TMP_ROOT="$(mktemp -d "${TMPDIR:-/tmp}/signoff-pr.XXXXXX")"
trap 'rm -rf "$TMP_ROOT"' EXIT

BACKEND_COVERAGE_THRESHOLD="${SIGNOFF_BACKEND_COVERAGE_THRESHOLD:-90}"
FRONTEND_COVERAGE_THRESHOLD="${SIGNOFF_FRONTEND_COVERAGE_THRESHOLD:-90}"
FRONTEND_RUNNER=()
DOCKER_COMPOSE_CMD=()

run_step() {
  local label="$1"
  shift

  echo
  echo "==> $label"
  "$@"
}

require_cmd() {
  local cmd="$1"
  local message="$2"

  if command -v "$cmd" >/dev/null 2>&1; then
    return
  fi

  echo "$message"
  exit 1
}

ensure_bun_available() {
  require_cmd bun "Frontend checks require 'bun' in PATH."
}

ensure_npm_available() {
  require_cmd npm "Frontend checks require 'npm' in PATH."
}

ensure_ruby_available() {
  require_cmd ruby "YAML validation requires 'ruby' in PATH."
}

ensure_tofu_available() {
  require_cmd tofu "Infrastructure validation requires 'tofu' in PATH."
}

ensure_python_available() {
  require_cmd python3 "Python script validation requires 'python3' in PATH."
}

capture_tracked_status() {
  git status --porcelain=v1 --untracked-files=no
}

ensure_tracked_status_unchanged() {
  local before="$1"
  local after
  after="$(capture_tracked_status)"

  if [ "$before" = "$after" ]; then
    return
  fi

  echo "Validation commands changed tracked files. Fix or commit those changes before signoff."
  git diff --stat
  exit 1
}

run_in_repo_subdir() {
  local relative_dir="$1"
  shift

  (
    cd "$ROOT_DIR/$relative_dir"
    "$@"
  )
}

resolve_solution_file() {
  local solutions=()
  local solution

  mapfile -t solutions < <(find "$ROOT_DIR" -maxdepth 1 -name '*.sln' -print | sed 's#^.*/##' | sort)

  if [ "${#solutions[@]}" -eq 0 ]; then
    echo "No solution file found at the repository root."
    exit 1
  fi

  for solution in "${solutions[@]}"; do
    if [[ "$solution" != *.Production.sln ]]; then
      echo "$solution"
      return
    fi
  done

  echo "${solutions[0]}"
}

ensure_frontend_runner() {
  if [ "${#FRONTEND_RUNNER[@]}" -gt 0 ]; then
    return
  fi

  if [ -f "$ROOT_DIR/www/bun.lock" ] || [ -f "$ROOT_DIR/www/bun.lockb" ]; then
    ensure_bun_available
    FRONTEND_RUNNER=(bun run)
    return
  fi

  ensure_npm_available
  FRONTEND_RUNNER=(npm run)
}

run_frontend_script() {
  local script="$1"
  shift

  ensure_frontend_runner

  (
    cd "$ROOT_DIR/www"
    "${FRONTEND_RUNNER[@]}" "$script" "$@"
  )
}

resolve_docker_compose_cmd() {
  if [ "${#DOCKER_COMPOSE_CMD[@]}" -gt 0 ]; then
    return
  fi

  if command -v docker >/dev/null 2>&1 && docker compose version >/dev/null 2>&1; then
    DOCKER_COMPOSE_CMD=(docker compose)
    return
  fi

  if command -v docker-compose >/dev/null 2>&1; then
    DOCKER_COMPOSE_CMD=(docker-compose)
    return
  fi

  echo "Docker Compose is required to start the local OpenFGA test dependency."
  echo "Install Docker Desktop (or docker-compose) and retry."
  exit 1
}

repo_uses_openfga() {
  grep -qi 'openfga' docker-compose.yml docker-compose.yaml docker-compose.dev.yml 2>/dev/null
}

ensure_openfga_ready() {
  local readiness_url="http://127.0.0.1:8090/stores"
  local max_attempts=30
  local attempt=1

  if ! repo_uses_openfga; then
    return
  fi

  require_cmd curl "'curl' is required to verify OpenFGA readiness."

  if curl -fsS "$readiness_url" >/dev/null 2>&1; then
    echo "OpenFGA is already reachable at $readiness_url"
    return
  fi

  resolve_docker_compose_cmd
  "${DOCKER_COMPOSE_CMD[@]}" up -d openfga

  until curl -fsS "$readiness_url" >/dev/null 2>&1; do
    if [ "$attempt" -ge "$max_attempts" ]; then
      echo "OpenFGA did not become ready at $readiness_url after $((max_attempts * 2)) seconds."
      "${DOCKER_COMPOSE_CMD[@]}" ps openfga || true
      exit 1
    fi

    sleep 2
    attempt=$((attempt + 1))
  done

  echo "OpenFGA is ready at $readiness_url"
}

validate_yaml_files() {
  if [ "${#CHANGED_YAML_FILES[@]}" -eq 0 ]; then
    echo "No non-workflow YAML files changed; skipping YAML validation."
    return
  fi

  ruby -e '
    require "psych"
    ARGV.each do |path|
      Psych.parse_stream(File.read(path))
      puts "Validated #{path}"
    end
  ' "${CHANGED_YAML_FILES[@]}"
}

validate_shell_scripts() {
  if [ "${#CHANGED_SHELL_FILES[@]}" -eq 0 ]; then
    echo "No shell scripts changed; skipping shell validation."
    return
  fi

  bash -n "${CHANGED_SHELL_FILES[@]}"
}

validate_python_scripts() {
  if [ "${#CHANGED_PYTHON_FILES[@]}" -eq 0 ]; then
    echo "No Python scripts changed; skipping Python validation."
    return
  fi

  PYTHONPYCACHEPREFIX="$TMP_ROOT/python-pycache" python3 -m py_compile "${CHANGED_PYTHON_FILES[@]}"
}

validate_tofu_root() {
  local root="$1"
  local slug
  slug="$(tr '/.' '__' <<<"$root")"
  local data_dir="$TMP_ROOT/tofu-$slug"
  local plugin_dir="$ROOT_DIR/$root/.terraform/providers"

  mkdir -p "$data_dir"

  if [ -d "$plugin_dir" ]; then
    TF_DATA_DIR="$data_dir" tofu -chdir="$root" init -backend=false -input=false -lockfile=readonly -plugin-dir="$plugin_dir"
  else
    TF_DATA_DIR="$data_dir" tofu -chdir="$root" init -backend=false -input=false -lockfile=readonly
  fi

  TF_DATA_DIR="$data_dir" tofu -chdir="$root" validate
}

validate_frontend_schema() {
  local generated_schema="$TMP_ROOT/schema.d.ts"

  run_in_repo_subdir www ./node_modules/.bin/openapi-typescript ../openapi.spec.json -o "$generated_schema" >/dev/null
  run_in_repo_subdir www ./node_modules/.bin/biome format "$generated_schema" --write >/dev/null

  if cmp -s www/src/schema.d.ts "$generated_schema"; then
    return
  fi

  echo "Checked-in www/src/schema.d.ts is stale. Regenerate it and commit the result."
  diff -u www/src/schema.d.ts "$generated_schema" || true
  exit 1
}

validate_backend_changed_coverage() {
  local results_dir="$1"
  local reports=()
  local args=(backend --threshold "$BACKEND_COVERAGE_THRESHOLD")

  mapfile -t reports < <(find "$results_dir" -name 'coverage.cobertura.xml' -print | sort)

  if [ "${#reports[@]}" -eq 0 ]; then
    echo "Backend coverage reports were not produced."
    exit 1
  fi

  for report in "${reports[@]}"; do
    args+=(--report "$report")
  done

  for file in "${CHANGED_FILES[@]}"; do
    args+=(--changed-file "$file")
  done

  PYTHONPYCACHEPREFIX="$TMP_ROOT/python-pycache" python3 scripts/check_changed_coverage.py "${args[@]}"
}

validate_frontend_changed_coverage() {
  local summary_path="$1/coverage-summary.json"
  local args=(frontend --threshold "$FRONTEND_COVERAGE_THRESHOLD" --summary "$summary_path")

  if [ ! -f "$summary_path" ]; then
    echo "Frontend coverage summary was not produced at $summary_path."
    exit 1
  fi

  for file in "${CHANGED_FILES[@]}"; do
    args+=(--changed-file "$file")
  done

  PYTHONPYCACHEPREFIX="$TMP_ROOT/python-pycache" python3 scripts/check_changed_coverage.py "${args[@]}"
}

ensure_not_default_branch() {
  local current_branch
  current_branch="$(git branch --show-current)"

  if [ "$current_branch" = "$DEFAULT_BRANCH" ]; then
    echo "Refusing to run on '$DEFAULT_BRANCH'. Create a feature branch and retry."
    exit 1
  fi
}

pick_remote() {
  if git remote get-url origin >/dev/null 2>&1; then
    echo "origin"
    return
  fi

  git remote | head -n 1
}

resolve_default_branch() {
  local remote remote_head
  remote="$(pick_remote)"

  if [ -n "$remote" ]; then
    remote_head="$(git symbolic-ref --quiet --short "refs/remotes/$remote/HEAD" 2>/dev/null || true)"
    if [ -n "$remote_head" ]; then
      echo "${remote_head#"$remote/"}"
      return
    fi
  fi

  if git show-ref --verify --quiet refs/heads/main; then
    echo "main"
    return
  fi

  if git show-ref --verify --quiet refs/heads/master; then
    echo "master"
    return
  fi

  echo "Could not determine the repository default branch."
  echo "Set the remote HEAD, or create a local 'main' or 'master' branch, then retry."
  exit 1
}

resolve_diff_base_ref() {
  local remote branch
  remote="$(pick_remote)"
  branch="$DEFAULT_BRANCH"

  if [ -n "$remote" ] && git show-ref --verify --quiet "refs/remotes/$remote/$branch"; then
    echo "$remote/$branch"
    return
  fi

  if git show-ref --verify --quiet "refs/heads/$branch"; then
    echo "$branch"
    return
  fi

  echo "Could not find a diff baseline for '$branch'."
  echo "Fetch '$remote/$branch' or create a local '$branch', then retry."
  exit 1
}

ensure_upstream_tracking() {
  local branch remote expected_upstream current_upstream
  branch="$(git branch --show-current)"
  remote="$(pick_remote)"

  if [ -z "$remote" ]; then
    echo "No git remotes found. Add a remote and retry."
    exit 1
  fi

  expected_upstream="$remote/$branch"
  current_upstream="$(git rev-parse --abbrev-ref --symbolic-full-name "@{upstream}" 2>/dev/null || true)"

  if [ "$current_upstream" != "$expected_upstream" ]; then
    echo "Configuring '$branch' to track '$expected_upstream'."
    git push -u "$remote" "$branch"
    return
  fi

  if git ls-remote --exit-code --heads "$remote" "$branch" >/dev/null 2>&1; then
    return
  fi

  echo "Remote branch '$expected_upstream' does not exist yet; pushing it now."
  git push -u "$remote" "$branch"
}

ensure_pull_request() {
  local branch existing_pr_url
  branch="$(git branch --show-current)"

  existing_pr_url="$(gh pr list \
    --head "$branch" \
    --base "$DEFAULT_BRANCH" \
    --state open \
    --json url \
    --jq '.[0].url' 2>/dev/null || true)"

  if [ -n "$existing_pr_url" ] && [ "$existing_pr_url" != "null" ]; then
    echo "Found existing pull request: $existing_pr_url"
    return
  fi

  echo "No pull request found for '$branch'; creating one against '$DEFAULT_BRANCH'."
  gh pr create --head "$branch" --base "$DEFAULT_BRANCH" --fill
}

DEFAULT_BRANCH="$(resolve_default_branch)"
DIFF_BASE_REF="$(resolve_diff_base_ref)"
SOLUTION_FILE="$(resolve_solution_file)"

ensure_not_default_branch

mapfile -t CHANGED_FILES < <(
  {
    git diff --name-only "$DIFF_BASE_REF"...HEAD
    git diff --name-only
    git diff --cached --name-only
  } | awk 'NF' | sort -u
)
CHANGED_FILES_TEXT="$(printf '%s\n' "${CHANGED_FILES[@]}")"

RUN_BACKEND=false
RUN_FRONTEND=false
RUN_INFRA=false
RUN_WORKFLOW_VALIDATION=false
RUN_YAML_VALIDATION=false
RUN_SHELL_VALIDATION=false
RUN_PYTHON_VALIDATION=false
RUN_CONTRACT_VALIDATION=false

CHANGED_WORKFLOW_FILES=()
mapfile -t CHANGED_WORKFLOW_FILES < <(
  printf '%s\n' "${CHANGED_FILES[@]}" \
    | grep -E '^\.github/workflows/[^[:space:]]+\.ya?ml$' \
    || true
)

CHANGED_YAML_FILES=()
mapfile -t CHANGED_YAML_FILES < <(
  printf '%s\n' "${CHANGED_FILES[@]}" \
    | grep -E '^[^[:space:]]+\.ya?ml$' \
    | grep -Ev '^\.github/workflows/' \
    || true
)

CHANGED_SHELL_FILES=()
mapfile -t CHANGED_SHELL_FILES < <(
  printf '%s\n' "${CHANGED_FILES[@]}" | grep -E '^[^[:space:]]+\.sh$' || true
)

CHANGED_PYTHON_FILES=()
mapfile -t CHANGED_PYTHON_FILES < <(
  printf '%s\n' "${CHANGED_FILES[@]}" | grep -E '^[^[:space:]]+\.py$' || true
)

if grep -qE '^(src/|scripts/signoff-pr\.sh$|scripts/check_changed_coverage\.py$|[^/]+\.sln$|Directory\.Build\.props$|\.editorconfig$|\.config/dotnet-tools\.json$)' <<<"$CHANGED_FILES_TEXT"; then
  RUN_BACKEND=true
fi

if grep -qE '^(www/|openapi\.spec\.json$)' <<<"$CHANGED_FILES_TEXT"; then
  RUN_FRONTEND=true
fi

if grep -qE '^infra/' <<<"$CHANGED_FILES_TEXT"; then
  RUN_INFRA=true
fi

if [ "${#CHANGED_WORKFLOW_FILES[@]}" -gt 0 ]; then
  RUN_WORKFLOW_VALIDATION=true
fi

if [ "${#CHANGED_YAML_FILES[@]}" -gt 0 ]; then
  RUN_YAML_VALIDATION=true
fi

if [ "${#CHANGED_SHELL_FILES[@]}" -gt 0 ]; then
  RUN_SHELL_VALIDATION=true
fi

if [ "${#CHANGED_PYTHON_FILES[@]}" -gt 0 ]; then
  RUN_PYTHON_VALIDATION=true
fi

if grep -qE '^(openapi\.spec\.json$|www/src/schema\.d\.ts$|www/package\.json$|www/package-lock\.json$|www/bun\.lockb?$)' <<<"$CHANGED_FILES_TEXT"; then
  RUN_CONTRACT_VALIDATION=true
fi

if [ "$RUN_FRONTEND" = true ] || [ "$RUN_CONTRACT_VALIDATION" = true ]; then
  ensure_frontend_runner
fi

VALIDATION_TRACKED_STATUS="$(capture_tracked_status)"

run_step "Checking git diff formatting" git diff --check

if [ "$RUN_BACKEND" = true ]; then
  ensure_python_available
  run_step "Restoring local dotnet tools" dotnet tool restore
  run_step "Running formatter check" dotnet tool run fantomas --check .
  run_step "Building solution (Release)" dotnet build "$SOLUTION_FILE" -c Release

  if repo_uses_openfga; then
    run_step "Ensuring OpenFGA is running for backend integration tests" ensure_openfga_ready
  fi

  BACKEND_COVERAGE_RESULTS_DIR="$TMP_ROOT/backend-coverage"
  run_step "Running backend tests with coverage (Release)" dotnet test "$SOLUTION_FILE" -c Release --no-build --collect:"XPlat Code Coverage" --results-directory "$BACKEND_COVERAGE_RESULTS_DIR"
  run_step "Checking backend changed-file coverage (${BACKEND_COVERAGE_THRESHOLD}% minimum)" validate_backend_changed_coverage "$BACKEND_COVERAGE_RESULTS_DIR"
else
  echo "No backend-related files changed; skipping backend verification steps."
fi

if [ "$RUN_FRONTEND" = true ]; then
  ensure_python_available
  run_step "Running frontend checks" run_frontend_script check
  run_step "Running frontend lint" run_frontend_script lint
  run_step "Building frontend" run_frontend_script build
  FRONTEND_COVERAGE_RESULTS_DIR="$TMP_ROOT/frontend-coverage"
  run_step "Running frontend tests with coverage" run_frontend_script test -- --coverage.enabled=true --coverage.provider=v8 --coverage.reporter=text-summary --coverage.reporter=json-summary --coverage.reportsDirectory "$FRONTEND_COVERAGE_RESULTS_DIR"
  run_step "Checking frontend changed-file coverage (${FRONTEND_COVERAGE_THRESHOLD}% minimum)" validate_frontend_changed_coverage "$FRONTEND_COVERAGE_RESULTS_DIR"
else
  echo "No frontend-related files changed; skipping frontend verification steps."
fi

if [ "$RUN_INFRA" = true ]; then
  ensure_tofu_available
  run_step "Checking OpenTofu formatting" tofu fmt -recursive -check

  if [ -d infra/opentofu ]; then
    run_step "Validating infra/opentofu" validate_tofu_root infra/opentofu
  fi

  if [ -d infra/foundation/opentofu ]; then
    run_step "Validating infra/foundation/opentofu" validate_tofu_root infra/foundation/opentofu
  fi
else
  echo "No changes under infra/; skipping OpenTofu validation."
fi

if [ "$RUN_WORKFLOW_VALIDATION" = true ]; then
  require_cmd actionlint $'actionlint is required to validate changed GitHub Actions workflows.\nInstall: https://github.com/rhysd/actionlint'
  run_step "Validating changed GitHub Actions workflows" actionlint "${CHANGED_WORKFLOW_FILES[@]}"
else
  echo "No workflow files changed; skipping workflow validation."
fi

if [ "$RUN_YAML_VALIDATION" = true ]; then
  ensure_ruby_available
  run_step "Validating changed YAML files" validate_yaml_files
else
  echo "No non-workflow YAML files changed; skipping YAML validation."
fi

if [ "$RUN_SHELL_VALIDATION" = true ]; then
  run_step "Validating changed shell scripts" validate_shell_scripts
else
  echo "No shell scripts changed; skipping shell validation."
fi

if [ "$RUN_PYTHON_VALIDATION" = true ]; then
  ensure_python_available
  run_step "Validating changed Python scripts" validate_python_scripts
else
  echo "No Python scripts changed; skipping Python validation."
fi

if [ "$RUN_CONTRACT_VALIDATION" = true ] && [ -f openapi.spec.json ] && [ -f www/src/schema.d.ts ]; then
  run_step "Validating checked-in frontend API types" validate_frontend_schema
else
  echo "No contract-related files changed; skipping contract validation."
fi

if ! command -v gh >/dev/null 2>&1; then
  echo "GitHub CLI ('gh') is required but was not found in PATH."
  echo "Install: https://cli.github.com/"
  exit 1
fi

if ! gh help signoff >/dev/null 2>&1; then
  cat <<'MSG'
The 'gh signoff' command is not available.
Install the extension and retry:
  gh extension install basecamp/gh-signoff
MSG
  exit 1
fi

ensure_tracked_status_unchanged "$VALIDATION_TRACKED_STATUS"
ensure_upstream_tracking
ensure_pull_request

echo "Signing off PR with gh-signoff..."
if [ "$#" -gt 0 ]; then
  echo "Ignoring unexpected arguments to signoff-pr.sh: $*"
fi

gh signoff
