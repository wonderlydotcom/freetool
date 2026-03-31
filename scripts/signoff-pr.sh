#!/usr/bin/env bash
set -euo pipefail

ROOT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
cd "$ROOT_DIR"

DOCKER_COMPOSE_CMD=()

run_step() {
  local label="$1"
  shift

  echo
  echo "==> $label"
  "$@"
}

resolve_docker_compose_cmd() {
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

ensure_openfga_ready() {
  local readiness_url="http://127.0.0.1:8090/stores"
  local max_attempts=30
  local attempt=1

  if ! command -v curl >/dev/null 2>&1; then
    echo "'curl' is required to verify OpenFGA readiness."
    exit 1
  fi

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

  if git show-ref --verify --quiet "refs/remotes/$remote/$branch"; then
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
  local branch remote
  branch="$(git branch --show-current)"
  remote="$(pick_remote)"

  if [ -z "$remote" ]; then
    echo "No git remotes found. Add a remote and retry."
    exit 1
  fi

  if git rev-parse --abbrev-ref --symbolic-full-name "@{upstream}" >/dev/null 2>&1; then
    return
  fi

  echo "No upstream configured for '$branch'; pushing and setting upstream on '$remote'."
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

ensure_not_default_branch

CHANGED_FILES="$(git diff --name-only "$DIFF_BASE_REF"...HEAD)"
RUN_BACKEND=false
RUN_FRONTEND=false
RUN_WORKFLOW_VALIDATION=false

mapfile -t CHANGED_WORKFLOW_FILES < <(
  grep -E '^\.github/workflows/[^[:space:]]+\.ya?ml$' <<<"$CHANGED_FILES" || true
)

if grep -qE '^src/' <<<"$CHANGED_FILES"; then
  RUN_BACKEND=true
fi

if grep -qE '^www/' <<<"$CHANGED_FILES"; then
  RUN_FRONTEND=true
fi

if [ "${#CHANGED_WORKFLOW_FILES[@]}" -gt 0 ]; then
  RUN_WORKFLOW_VALIDATION=true
fi

if [ "$RUN_BACKEND" = true ]; then
  run_step "Running formatter" dotnet tool run fantomas .
  run_step "Building solution (Release)" dotnet build Freetool.sln -c Release
  run_step "Ensuring OpenFGA is running for backend integration tests" ensure_openfga_ready
  run_step "Running backend tests" dotnet test Freetool.sln
else
  echo "No changes under src/; skipping backend verification steps."
fi

if [ "$RUN_FRONTEND" = true ]; then
  run_step "Running frontend type checks" bash -lc "cd www && npm run check"
  run_step "Running frontend lint" bash -lc "cd www && npm run lint"
  run_step "Running frontend format" bash -lc "cd www && npm run format"
else
  echo "No changes under www/; skipping frontend verification steps."
fi

if [ "$RUN_WORKFLOW_VALIDATION" = true ]; then
  if ! command -v actionlint >/dev/null 2>&1; then
    echo "actionlint is required to validate changed GitHub Actions workflows."
    echo "Install: https://github.com/rhysd/actionlint"
    exit 1
  fi

  run_step "Validating changed GitHub Actions workflows" actionlint "${CHANGED_WORKFLOW_FILES[@]}"
else
  echo "No changes under .github/workflows/; skipping workflow validation."
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

ensure_upstream_tracking
ensure_pull_request

echo "Signing off PR with gh-signoff..."
if [ "$#" -gt 0 ]; then
  echo "Ignoring unexpected arguments to signoff-pr.sh: $*"
fi

gh signoff
