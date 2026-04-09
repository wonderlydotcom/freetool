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

read_secret_provider_resource_names() {
  local namespace="$1"
  local secret_provider_class_name="$2"

  kubectl -n "${namespace}" get secretproviderclass "${secret_provider_class_name}" -o json     | jq -r '.spec.parameters.secrets // ""'     | awk -F'"' '/resourceName:/ {print $2}'
}

is_secret_manager_versions_permission_denied() {
  local output="$1"

  [[ "${output}" == *"PERMISSION_DENIED"* ]] || [[ "${output}" == *"secretmanager.versions.list"* ]]
}

validate_runtime_secret_versions() {
  local project_id="$1"
  local namespace="$2"
  local secret_provider_class_name="$3"
  local resource_name=""
  local secret_id=""
  local enabled_version=""
  local gcloud_output=""
  local missing_secret_ids=()

  if [[ -z "${secret_provider_class_name}" || "${secret_provider_class_name}" == "null" ]]; then
    return 0
  fi

  if ! kubectl -n "${namespace}" get secretproviderclass "${secret_provider_class_name}" >/dev/null 2>&1; then
    echo "Missing SecretProviderClass ${secret_provider_class_name} in namespace ${namespace}." >&2
    exit 1
  fi

  while IFS= read -r resource_name; do
    [[ -n "${resource_name}" ]] || continue

    if [[ "${resource_name}" =~ ^projects/[^/]+/secrets/([^/]+)/versions/[^/]+$ ]]; then
      secret_id="${BASH_REMATCH[1]}"
    else
      echo "Could not parse Secret Manager resource name: ${resource_name}" >&2
      exit 1
    fi

    if ! gcloud_output="$(
      gcloud secrets versions list "${secret_id}"         --project "${project_id}"         --filter "state=enabled"         --format 'value(name)'         --limit 1 2>&1
    )"; then
      if is_secret_manager_versions_permission_denied "${gcloud_output}"; then
        log "Skipping runtime secret version validation because the current Google Cloud identity cannot list Secret Manager versions. Shared platform filtering remains the authoritative zero-version guard in CI."
        return 0
      fi

      echo "Failed to inspect enabled Secret Manager versions for ${secret_id}: ${gcloud_output}" >&2
      exit 1
    fi

    enabled_version="${gcloud_output}"

    if [[ -z "${enabled_version}" ]]; then
      missing_secret_ids+=("${secret_id}")
    fi
  done < <(read_secret_provider_resource_names "${namespace}" "${secret_provider_class_name}")

  if [[ "${#missing_secret_ids[@]}" -gt 0 ]]; then
    echo "Refusing to deploy: SecretProviderClass ${secret_provider_class_name} in namespace ${namespace} references secrets with no enabled versions:" >&2
    printf '  - %s\n' "${missing_secret_ids[@]}" >&2
    echo "Upload a secret version or remove the secret from the platform app catalog before rolling pods." >&2
    exit 1
  fi
}

selector_from_workload() {
  local namespace="$1"
  local workload_kind="$2"
  local workload_name="$3"

  kubectl -n "${namespace}" get "${workload_kind}" "${workload_name}" -o json \
    | jq -r '.spec.selector.matchLabels // {} | to_entries | map("\(.key)=\(.value)") | join(",")'
}

print_rollout_debug() {
  local namespace="$1"
  local workload_kind="$2"
  local workload_name="$3"
  local selector=""
  local pod_name=""

  echo "Rollout diagnostics for ${workload_kind}/${workload_name} in namespace ${namespace}:" >&2
  kubectl -n "${namespace}" get "${workload_kind}" "${workload_name}" -o wide >&2 || true
  kubectl -n "${namespace}" describe "${workload_kind}" "${workload_name}" >&2 || true

  selector="$(selector_from_workload "${namespace}" "${workload_kind}" "${workload_name}" 2>/dev/null || true)"

  if [[ -n "${selector}" ]]; then
    kubectl -n "${namespace}" get pods -l "${selector}" -o wide >&2 || true

    while IFS= read -r pod_name; do
      [[ -n "${pod_name}" ]] || continue
      kubectl -n "${namespace}" describe pod "${pod_name}" >&2 || true
      kubectl -n "${namespace}" logs "${pod_name}" --all-containers=true --tail=200 >&2 || true
      kubectl -n "${namespace}" logs "${pod_name}" --all-containers=true --previous --tail=200 >&2 || true
    done < <(
      kubectl -n "${namespace}" get pods -l "${selector}" -o json \
        | jq -r '.items | sort_by(.metadata.creationTimestamp) | .[].metadata.name'
    )
  else
    kubectl -n "${namespace}" get pods -o wide >&2 || true
  fi

  kubectl -n "${namespace}" get events --sort-by=.metadata.creationTimestamp >&2 || true
}

find_stale_blocking_pod() {
  local namespace="$1"
  local workload_kind="$2"
  local workload_name="$3"
  local workload_json=""
  local selector=""
  local pods_json=""
  local replicas=""
  local pod_count=""
  local pod_name=""
  local desired_images=""
  local current_images=""
  local pod_ready=""

  workload_json="$(kubectl -n "${namespace}" get "${workload_kind}" "${workload_name}" -o json 2>/dev/null)" || return 1
  replicas="$(jq -r '.spec.replicas // 1' <<<"${workload_json}")"

  if [[ "${replicas}" != "1" ]]; then
    return 1
  fi

  selector="$(jq -r '.spec.selector.matchLabels // {} | to_entries | map("\(.key)=\(.value)") | join(",")' <<<"${workload_json}")"

  if [[ -z "${selector}" ]]; then
    return 1
  fi

  pods_json="$(kubectl -n "${namespace}" get pods -l "${selector}" -o json 2>/dev/null)" || return 1
  pod_count="$(jq -r '.items | length' <<<"${pods_json}")"

  if [[ "${pod_count}" != "1" ]]; then
    return 1
  fi

  desired_images="$(jq -r '.spec.template.spec.containers | map("\(.name)=\(.image)") | join("\n")' <<<"${workload_json}")"
  current_images="$(jq -r '.items[0].spec.containers | map("\(.name)=\(.image)") | join("\n")' <<<"${pods_json}")"

  if [[ "${desired_images}" == "${current_images}" ]]; then
    return 1
  fi

  pod_ready="$(
    jq -r '
      if (.items[0].status.containerStatuses // [] | length) == 0
      then "false"
      else ((.items[0].status.containerStatuses | map(.ready) | all) | tostring)
      end
    ' <<<"${pods_json}"
  )"

  if [[ "${pod_ready}" == "true" ]]; then
    return 1
  fi

  pod_name="$(jq -r '.items[0].metadata.name' <<<"${pods_json}")"

  if [[ -z "${pod_name}" || "${pod_name}" == "null" ]]; then
    return 1
  fi

  printf '%s' "${pod_name}"
}

wait_for_rollout_with_recovery() {
  local namespace="$1"
  local workload_kind="$2"
  local workload_name="$3"
  local stale_pod=""

  if kubectl -n "${namespace}" rollout status "${workload_kind}/${workload_name}" --timeout "${ROLLOUT_TIMEOUT}"; then
    return 0
  fi

  stale_pod="$(find_stale_blocking_pod "${namespace}" "${workload_kind}" "${workload_name}" || true)"

  if [[ -n "${stale_pod}" ]]; then
    log "Scaling ${workload_kind}/${workload_name} down to 0 to recycle stale unhealthy pod ${stale_pod}"
    kubectl -n "${namespace}" scale "${workload_kind}/${workload_name}" --replicas=0
    kubectl -n "${namespace}" rollout status "${workload_kind}/${workload_name}" --timeout "${ROLLOUT_TIMEOUT}"
    log "Scaling ${workload_kind}/${workload_name} back to 1 and retrying rollout"
    kubectl -n "${namespace}" scale "${workload_kind}/${workload_name}" --replicas=1

    if kubectl -n "${namespace}" rollout status "${workload_kind}/${workload_name}" --timeout "${ROLLOUT_TIMEOUT}"; then
      return 0
    fi
  fi

  print_rollout_debug "${namespace}" "${workload_kind}" "${workload_name}"
  return 1
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
NAMESPACE="$(read_output namespace)"
SECRET_PROVIDER_CLASS_NAME="$(read_output secret_provider_class_name)"

if [[ -n "${NAMESPACE}" && "${NAMESPACE}" != "null" && -n "${SECRET_PROVIDER_CLASS_NAME}" && "${SECRET_PROVIDER_CLASS_NAME}" != "null" ]]; then
  log "Validating runtime secrets from ${SECRET_PROVIDER_CLASS_NAME} in namespace ${NAMESPACE}"
  validate_runtime_secret_versions "${PROJECT_ID}" "${NAMESPACE}" "${SECRET_PROVIDER_CLASS_NAME}"
else
  log "Skipping runtime secret validation because namespace or SecretProviderClass output is not available yet"
fi

NAMESPACE="$(resolve_value APP_NAMESPACE namespace "app namespace")"
WORKLOAD_NAME="$(resolve_value WORKLOAD_NAME workload_name "workload name")"
WORKLOAD_KIND="statefulset"
DEPLOYED_IMAGE_REF="$(read_output image_ref)"

log "Waiting for rollout in namespace ${NAMESPACE}"
wait_for_rollout_with_recovery "${NAMESPACE}" "${WORKLOAD_KIND}" "${WORKLOAD_NAME}"

cat <<EOF
Deployment finished.

namespace=${NAMESPACE}
workload=${WORKLOAD_NAME}
image=${DEPLOYED_IMAGE_REF}
domain=$(read_output domain_name)
EOF
