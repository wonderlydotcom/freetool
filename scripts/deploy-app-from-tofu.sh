#!/usr/bin/env bash
set -euo pipefail

usage() {
  cat <<'EOF'
Usage: scripts/deploy-app-from-tofu.sh [additional tofu apply args]

Builds and pushes the application image, applies the app-owned OpenTofu stack,
and waits for the StatefulSet rollout.

Environment overrides:
  INFRA_DIR                    OpenTofu directory (default: infra/opentofu)
  IMAGE_TAG                    Tag to build and deploy (default: git sha or timestamp)
  IMAGE_NAME                   Image name inside Artifact Registry
  GCP_PROJECT_ID               Shared GCP project ID
  ARTIFACT_REGISTRY_LOCATION   Artifact Registry location
  ARTIFACT_REGISTRY_REPO       Per-app Artifact Registry repo ID
  ROLLOUT_TIMEOUT              kubectl rollout timeout (default: 5m)
  DOCKER_PLATFORM              docker build platform (default: linux/amd64)
  PUBLISH_LATEST               Also push :latest when IMAGE_TAG != latest (default: false)

If this is the first deployment and tofu outputs do not exist yet, set
GCP_PROJECT_ID, ARTIFACT_REGISTRY_LOCATION, and ARTIFACT_REGISTRY_REPO
explicitly or run an initial tofu apply first.

If KUBECONFIG is set and TF_VAR_kubeconfig_path is not, the script exports the
first kubeconfig path into TF_VAR_kubeconfig_path so OpenTofu uses the same
cluster credentials as kubectl.
EOF
}

require_cmd() {
  command -v "$1" >/dev/null 2>&1 || {
    echo "Missing required command: $1" >&2
    exit 1
  }
}

log() {
  printf '[%s] %s\n' "$(date '+%Y-%m-%d %H:%M:%S')" "$*"
}

ensure_gke_auth_plugin() {
  local kubeconfig_paths="${KUBECONFIG:-${HOME}/.kube/config}"
  local kubeconfig_uses_plugin=false
  local previous_ifs="${IFS}"
  IFS=':'

  for kubeconfig_path in ${kubeconfig_paths}; do
    if [[ -f "${kubeconfig_path}" ]] && grep -q "gke-gcloud-auth-plugin" "${kubeconfig_path}"; then
      kubeconfig_uses_plugin=true
      break
    fi
  done

  IFS="${previous_ifs}"

  if [[ "${kubeconfig_uses_plugin}" != "true" ]]; then
    return 0
  fi

  if command -v gke-gcloud-auth-plugin >/dev/null 2>&1; then
    return 0
  fi

  local sdk_root=""
  sdk_root="$(gcloud info --format='value(installation.sdk_root)' 2>/dev/null || true)"

  if [[ -n "${sdk_root}" && -x "${sdk_root}/bin/gke-gcloud-auth-plugin" ]]; then
    export PATH="${sdk_root}/bin:${PATH}"
  fi

  if ! command -v gke-gcloud-auth-plugin >/dev/null 2>&1; then
    echo "Missing required command: gke-gcloud-auth-plugin" >&2
    echo "Install it or add the Google Cloud SDK bin directory to PATH before deploying." >&2
    exit 1
  fi
}

sync_tf_var_kubeconfig_path() {
  if [[ -n "${TF_VAR_kubeconfig_path:-}" || -z "${KUBECONFIG:-}" ]]; then
    return 0
  fi

  local kubeconfig_path="${KUBECONFIG%%:*}"

  if [[ -z "${kubeconfig_path}" ]]; then
    return 0
  fi

  export TF_VAR_kubeconfig_path="${kubeconfig_path}"
  log "Using kubeconfig ${TF_VAR_kubeconfig_path} for OpenTofu"
}

read_output() {
  local key="$1"
  jq -r --arg key "$key" '.[$key].value // empty' <<<"${OUTPUT_JSON}"
}

resolve_value() {
  local env_name="$1"
  local output_key="$2"
  local label="$3"
  local value="${!env_name:-}"

  if [[ -z "${value}" ]]; then
    value="$(read_output "${output_key}")"
  fi

  if [[ -z "${value}" || "${value}" == "null" ]]; then
    echo "Missing ${label}. Set ${env_name} or run 'tofu -chdir=${INFRA_DIR} apply' once first." >&2
    exit 1
  fi

  printf '%s' "${value}"
}

if [[ "${1:-}" == "--help" || "${1:-}" == "-h" ]]; then
  usage
  exit 0
fi

for cmd in docker gcloud jq kubectl tofu; do
  require_cmd "${cmd}"
done

ensure_gke_auth_plugin
sync_tf_var_kubeconfig_path

INFRA_DIR="${INFRA_DIR:-infra/opentofu}"
ROLLOUT_TIMEOUT="${ROLLOUT_TIMEOUT:-5m}"
DOCKER_PLATFORM="${DOCKER_PLATFORM:-linux/amd64}"
PUBLISH_LATEST="${PUBLISH_LATEST:-false}"
TOFU_APPLY_ARGS=("$@")

if [[ ! -d "${INFRA_DIR}" ]]; then
  echo "Infra directory not found: ${INFRA_DIR}" >&2
  exit 1
fi

if git rev-parse --git-dir >/dev/null 2>&1; then
  DEFAULT_IMAGE_TAG="$(git rev-parse --short HEAD)"
else
  DEFAULT_IMAGE_TAG="$(date '+%Y%m%d%H%M%S')"
fi

IMAGE_TAG="${IMAGE_TAG:-${DEFAULT_IMAGE_TAG}}"

if ! OUTPUT_JSON="$(tofu -chdir="${INFRA_DIR}" output -json 2>/dev/null)"; then
  OUTPUT_JSON='{}'
fi

PROJECT_ID="$(resolve_value GCP_PROJECT_ID project_id "GCP project ID")"
ARTIFACT_REGISTRY_LOCATION="$(resolve_value ARTIFACT_REGISTRY_LOCATION artifact_registry_location "Artifact Registry location")"
ARTIFACT_REGISTRY_REPO="$(resolve_value ARTIFACT_REGISTRY_REPO artifact_registry_repo "Artifact Registry repository")"

IMAGE_NAME="${IMAGE_NAME:-$(read_output image_name)}"
if [[ -z "${IMAGE_NAME}" || "${IMAGE_NAME}" == "null" ]]; then
  IMAGE_NAME="freetool-api"
fi

REGISTRY_HOST="${ARTIFACT_REGISTRY_LOCATION}-docker.pkg.dev"
IMAGE_URI="${REGISTRY_HOST}/${PROJECT_ID}/${ARTIFACT_REGISTRY_REPO}/${IMAGE_NAME}:${IMAGE_TAG}"
LATEST_IMAGE_URI="${REGISTRY_HOST}/${PROJECT_ID}/${ARTIFACT_REGISTRY_REPO}/${IMAGE_NAME}:latest"

log "Building image ${IMAGE_URI}"
gcloud auth configure-docker "${REGISTRY_HOST}" --quiet
docker build --platform "${DOCKER_PLATFORM}" -f src/Freetool.Api/Dockerfile -t "${IMAGE_URI}" .
docker push "${IMAGE_URI}"

if [[ "${PUBLISH_LATEST}" == "true" && "${IMAGE_TAG}" != "latest" ]]; then
  log "Publishing latest tag ${LATEST_IMAGE_URI}"
  docker tag "${IMAGE_URI}" "${LATEST_IMAGE_URI}"
  docker push "${LATEST_IMAGE_URI}"
fi

log "Applying OpenTofu with image_tag=${IMAGE_TAG}"
tofu -chdir="${INFRA_DIR}" apply -auto-approve \
  "${TOFU_APPLY_ARGS[@]}" \
  -var "image_name=${IMAGE_NAME}" \
  -var "image_tag=${IMAGE_TAG}"

OUTPUT_JSON="$(tofu -chdir="${INFRA_DIR}" output -json)"
NAMESPACE="$(resolve_value APP_NAMESPACE namespace "app namespace")"
WORKLOAD_NAME="$(resolve_value WORKLOAD_NAME workload_name "workload name")"
DEPLOYED_IMAGE_REF="$(read_output image_ref)"

log "Waiting for rollout in namespace ${NAMESPACE}"
kubectl -n "${NAMESPACE}" rollout status "statefulset/${WORKLOAD_NAME}" --timeout "${ROLLOUT_TIMEOUT}"

cat <<EOF
Deployment finished.

namespace=${NAMESPACE}
workload=${WORKLOAD_NAME}
image=${DEPLOYED_IMAGE_REF}
domain=$(read_output domain_name)
EOF
