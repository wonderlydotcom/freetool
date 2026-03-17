#!/usr/bin/env bash
set -euo pipefail

if command -v tailscale >/dev/null 2>&1; then
  tailscale down || true
fi

# Flow:
# 1) Preflight local sqlite backup from old VM
# 2) Build/push image
# 3) Provision green VM + backend attachment via OpenTofu (capacity 0)
# 4) Drain old backend via OpenTofu (small non-zero primary capacity)
# 5) Stop both stacks, copy sqlite dirs (freetool-db + openfga)
# 6) Restart green, then switch traffic via OpenTofu (green capacity 1, primary MIG warm standby size 1)

require_cmd() { command -v "$1" >/dev/null 2>&1 || { echo "Missing: $1" >&2; exit 1; }; }
log() { printf '[%s] %s\n' "$(date '+%Y-%m-%d %H:%M:%S')" "$*"; }
now() { date +%s; }

cleanup_files=()
OLD_BACKEND_DRAINED=0
GREEN_BACKEND_ENABLED=0

cleanup() {
  local f
  for f in "${cleanup_files[@]:-}"; do
    [[ -n "$f" ]] && rm -f "$f" || true
  done
}

validate_google_auth() {
  local adc_err active_account

  active_account="$(gcloud auth list --filter=status:ACTIVE --format='value(account)' 2>/dev/null | head -n1 || true)"
  if [[ -z "${active_account}" ]]; then
    cat >&2 <<'EOF'
No active gcloud account found.

Authenticate first with either:
  gcloud auth login
  gcloud auth application-default login

For non-interactive deploys, prefer a service account credential file or service account impersonation.
EOF
    exit 1
  fi

  adc_err="$(mktemp /tmp/freetool-gcloud-adc.XXXXXX)"
  cleanup_files+=("${adc_err}")
  if ! gcloud auth application-default print-access-token >/dev/null 2>"${adc_err}"; then
    cat >&2 <<EOF
Google Application Default Credentials are not usable for OpenTofu.
Active gcloud account: ${active_account}

OpenTofu in this repo uses the Google provider's default credential chain, so stale user ADC will fail with errors like:
  oauth2: "invalid_grant" "reauth related error (invalid_rapt)"

Remediation:
  1. Reauthenticate user ADC:
     gcloud auth application-default login
  2. If your login session also expired, refresh it:
     gcloud auth login
  3. For CI or repeatable deploys, prefer a service account:
     export GOOGLE_APPLICATION_CREDENTIALS=/path/to/key.json
     # or configure provider/service-account impersonation

Original gcloud error:
$(sed 's/^/  /' "${adc_err}")
EOF
    exit 1
  fi
}

hydrate_from_tofu_output() {
  export GCP_PROJECT_ID="${GCP_PROJECT_ID:-$(jq -r '.project_id.value' <<<"$OUT")}"
  export GCP_REGION="${GCP_REGION:-$(jq -r '.artifact_registry_location.value' <<<"$OUT")}"
  export GCP_ARTIFACT_REPO="${GCP_ARTIFACT_REPO:-$(jq -r '.artifact_registry_repo_id.value' <<<"$OUT")}"
  export GCP_VM_ZONE="${GCP_VM_ZONE:-$(jq -r '.vm_zone.value' <<<"$OUT")}"
  export GCP_BACKEND_SERVICE="${GCP_BACKEND_SERVICE:-$(jq -r '.backend_service_name.value' <<<"$OUT")}"
  export GCP_MIG_NAME="${GCP_MIG_NAME:-$(jq -r '.managed_instance_group_name.value' <<<"$OUT")}"
  export FREETOOL_IAP_JWT_AUDIENCE="${FREETOOL_IAP_JWT_AUDIENCE:-$(jq -r '.iap_jwt_audience.value' <<<"$OUT")}"
  export FREETOOL_DATA_ROOT="${FREETOOL_DATA_ROOT:-$(jq -r '.data_mount_path.value' <<<"$OUT")}"
  export FREETOOL_ORG_ADMIN_EMAIL="${FREETOOL_ORG_ADMIN_EMAIL:-$(jq -r '.org_admin_email.value' <<<"$OUT")}"
  export FREETOOL_VALIDATE_IAP_JWT="${FREETOOL_VALIDATE_IAP_JWT:-$(jq -r '.validate_iap_jwt.value' <<<"$OUT")}"
  export FREETOOL_GOOGLE_DIRECTORY_ENABLED="${FREETOOL_GOOGLE_DIRECTORY_ENABLED:-$(jq -r '.google_directory_enabled.value' <<<"$OUT")}"
  export FREETOOL_GOOGLE_DIRECTORY_ADMIN_USER_EMAIL="${FREETOOL_GOOGLE_DIRECTORY_ADMIN_USER_EMAIL:-$(jq -r '.google_directory_admin_user_email.value' <<<"$OUT")}"
  export FREETOOL_GOOGLE_DIRECTORY_SCOPE="${FREETOOL_GOOGLE_DIRECTORY_SCOPE:-$(jq -r '.google_directory_scope.value' <<<"$OUT")}"
  export FREETOOL_GOOGLE_DIRECTORY_OU_KEY_PREFIX="${FREETOOL_GOOGLE_DIRECTORY_OU_KEY_PREFIX:-$(jq -r '.google_directory_org_unit_key_prefix.value' <<<"$OUT")}"
  export FREETOOL_GOOGLE_DIRECTORY_INCLUDE_OU_HIERARCHY="${FREETOOL_GOOGLE_DIRECTORY_INCLUDE_OU_HIERARCHY:-$(jq -r '.google_directory_include_org_unit_hierarchy.value' <<<"$OUT")}"
  export FREETOOL_GOOGLE_DIRECTORY_CUSTOM_KEY_PREFIX="${FREETOOL_GOOGLE_DIRECTORY_CUSTOM_KEY_PREFIX:-$(jq -r '.google_directory_custom_attribute_key_prefix.value' <<<"$OUT")}"
  export FREETOOL_GOOGLE_DIRECTORY_CREDENTIALS_SECRET_NAME="${FREETOOL_GOOGLE_DIRECTORY_CREDENTIALS_SECRET_NAME:-$(jq -r '.google_directory_credentials_secret_name.value' <<<"$OUT")}"
}

load_tofu_outputs() {
  pushd "${INFRA_DIR}" >/dev/null
  OUT="$(tofu output -json)"
  popd >/dev/null
  hydrate_from_tofu_output
}

apply_tofu_state() {
  local primary_capacity="$1"
  local green_capacity="$2"
  local green_enabled="$3"
  local image_tag="$4"
  local primary_mig_size="$5"

  pushd "${INFRA_DIR}" >/dev/null
  tofu apply -auto-approve \
    -var "primary_backend_capacity=${primary_capacity}" \
    -var "bluegreen_backend_capacity=${green_capacity}" \
    -var "bluegreen_enabled=${green_enabled}" \
    -var "bluegreen_image_tag=${image_tag}" \
    -var "primary_mig_target_size=${primary_mig_size}" >/dev/null
  OUT="$(tofu output -json)"
  popd >/dev/null

  hydrate_from_tofu_output

  GREEN_VM_NAME="$(jq -r '.bluegreen_vm_name.value // empty' <<<"$OUT")"
  GREEN_IG_NAME="$(jq -r '.bluegreen_instance_group_name.value // empty' <<<"$OUT")"
}

wait_for_ssh() {
  local vm="$1" start
  start="$(now)"
  while true; do
    if gcloud compute ssh "$vm" --project "$GCP_PROJECT_ID" --zone "$GCP_VM_ZONE" --tunnel-through-iap --command "echo ok" >/dev/null 2>&1; then
      return 0
    fi
    if (( "$(now)" - start > 900 )); then
      echo "SSH timeout: $vm" >&2
      exit 1
    fi
    sleep 10
  done
}

wait_local_health() {
  local vm="$1" start
  start="$(now)"
  while true; do
    if gcloud compute ssh "$vm" --project "$GCP_PROJECT_ID" --zone "$GCP_VM_ZONE" --tunnel-through-iap --command "curl -fsS http://127.0.0.1:8080/swagger/index.html >/dev/null" >/dev/null 2>&1; then
      return 0
    fi
    if (( "$(now)" - start > 600 )); then
      echo "Health timeout: $vm" >&2
      exit 1
    fi
    sleep 5
  done
}

wait_primary_mig_target_instances() {
  local expected="$1" start elapsed target_size instance_count
  start="$(now)"
  while true; do
    target_size="$(gcloud compute instance-groups managed describe "${GCP_MIG_NAME}" \
      --project "${GCP_PROJECT_ID}" \
      --zone "${GCP_VM_ZONE}" \
      --format='value(targetSize)')"
    instance_count="$(gcloud compute instance-groups managed list-instances "${GCP_MIG_NAME}" \
      --project "${GCP_PROJECT_ID}" \
      --zone "${GCP_VM_ZONE}" \
      --format='value(instance)' | wc -l | tr -d ' ')"

    if [[ "${target_size}" == "${expected}" && "${instance_count}" -ge "${expected}" ]]; then
      return 0
    fi

    elapsed="$(( $(now) - start ))"
    if (( elapsed > 900 )); then
      echo "Primary MIG did not converge to targetSize=${expected} within 15 minutes (targetSize=${target_size}, instances=${instance_count})" >&2
      exit 1
    fi
    sleep 10
  done
}

resolve_old_vm() {
  local mig_vm bluegreen_vm backend_vm labeled_vm

  # The blue/green VM owns the persistent data disk after cutover, so prefer it
  # whenever it exists instead of the primary MIG's stateless instance.
  bluegreen_vm="$(jq -r '.bluegreen_vm_name.value // empty' <<<"${OUT:-}")"
  if [[ -n "${bluegreen_vm}" ]]; then
    echo "${bluegreen_vm}"
    return 0
  fi

  mig_vm="$(gcloud compute instance-groups managed list-instances "${GCP_MIG_NAME}" \
    --project "${GCP_PROJECT_ID}" \
    --zone "${GCP_VM_ZONE}" \
    --format='value(instance.basename())' | head -n1)"
  if [[ -n "${mig_vm}" ]]; then
    echo "${mig_vm}"
    return 0
  fi

  backend_vm="$(
    gcloud compute backend-services get-health "${GCP_BACKEND_SERVICE}" \
      --project "${GCP_PROJECT_ID}" \
      --global \
      --format=json 2>/dev/null \
      | jq -r '.. | .instance? // empty | capture(".*/instances/(?<name>[^/]+)$").name' 2>/dev/null \
      | head -n1
  )"
  if [[ -n "${backend_vm}" ]]; then
    echo "${backend_vm}"
    return 0
  fi

  labeled_vm="$(
    gcloud compute instances list \
      --project "${GCP_PROJECT_ID}" \
      --filter="zone:(${GCP_VM_ZONE}) AND labels.app=freetool AND status=RUNNING" \
      --format='value(name)' 2>/dev/null \
      | head -n1
  )"
  if [[ -n "${labeled_vm}" ]]; then
    echo "${labeled_vm}"
    return 0
  fi

  return 1
}

stop_stack() {
  local vm="$1"
  gcloud compute ssh "$vm" --project "$GCP_PROJECT_ID" --zone "$GCP_VM_ZONE" --tunnel-through-iap --command "
    set -euo pipefail
    if [[ -f ${REMOTE_DIR}/docker-compose.gce.yml ]]; then
      cd ${REMOTE_DIR}
    elif [[ -f /opt/freetool/docker-compose.gce.yml ]]; then
      cd /opt/freetool
    fi

    if [[ -f docker-compose.gce.yml ]]; then
      if sudo docker compose version >/dev/null 2>&1; then
        sudo docker compose -f docker-compose.gce.yml --env-file .env down
      else
        sudo docker-compose -f docker-compose.gce.yml --env-file .env down
      fi
    else
      sudo systemctl stop freetool-compose.service || true
    fi
  "
}

start_stack() {
  local vm="$1"
  gcloud compute ssh "$vm" --project "$GCP_PROJECT_ID" --zone "$GCP_VM_ZONE" --tunnel-through-iap --command "
    set -euo pipefail
    if [[ -f ${REMOTE_DIR}/docker-compose.gce.yml ]]; then
      cd ${REMOTE_DIR}
    elif [[ -f /opt/freetool/docker-compose.gce.yml ]]; then
      cd /opt/freetool
    fi

    if [[ -f docker-compose.gce.yml ]]; then
      if sudo docker compose version >/dev/null 2>&1; then
        sudo docker compose -f docker-compose.gce.yml --env-file .env up -d --remove-orphans
      else
        sudo docker-compose -f docker-compose.gce.yml --env-file .env up -d --remove-orphans
      fi
    else
      sudo systemctl start freetool-compose.service
    fi
  "
}

on_error() {
  local exit_code="$1"
  set +e

  if (( OLD_BACKEND_DRAINED == 1 && GREEN_BACKEND_ENABLED == 0 )); then
    log "Error encountered after draining old backend; restoring capacities via OpenTofu"
    apply_tofu_state 1 0 true "${TAG:-latest}" 1 || true
    if [[ -n "${OLD_VM_NAME:-}" ]]; then
      start_stack "${OLD_VM_NAME}" || true
      wait_local_health "${OLD_VM_NAME}" || true
    fi
  fi

  cleanup
  exit "$exit_code"
}

for c in gcloud docker jq tofu; do require_cmd "$c"; done

INFRA_DIR="${INFRA_DIR:-infra/opentofu}"
LOCAL_SQLITE_BACKUP_ROOT="${LOCAL_SQLITE_BACKUP_ROOT:-$HOME/Desktop/freetool-sqlite-backups}"
DRAIN_SECONDS_TARGET="${DRAIN_SECONDS_TARGET:-120}"
PRIMARY_DRAIN_CAPACITY="${PRIMARY_DRAIN_CAPACITY:-0.01}"
REMOTE_DIR="${REMOTE_DIR:-/opt/freetool}"

trap cleanup EXIT
trap 'on_error $?' ERR

validate_google_auth
load_tofu_outputs

: "${GCP_PROJECT_ID:?}"
: "${GCP_REGION:?}"
: "${GCP_ARTIFACT_REPO:?}"
: "${GCP_VM_ZONE:?}"
: "${GCP_MIG_NAME:?}"
: "${FREETOOL_IAP_JWT_AUDIENCE:?}"

IMAGE_NAME="${IMAGE_NAME:-freetool-api}"
TAG="${TAG:-$(git rev-parse --short HEAD)}"
REGISTRY_HOST="${GCP_REGION}-docker.pkg.dev"
IMAGE_URI="${REGISTRY_HOST}/${GCP_PROJECT_ID}/${GCP_ARTIFACT_REPO}/${IMAGE_NAME}:${TAG}"
OLD_DATA_ROOT="${OLD_DATA_ROOT:-${FREETOOL_DATA_ROOT:-/mnt/freetool-data}}"
GREEN_DATA_ROOT="${GREEN_DATA_ROOT:-${FREETOOL_DATA_ROOT:-/mnt/freetool-data}}"
LOCAL_SQLITE_BACKUP_DIR=""

OLD_VM_NAME="$(resolve_old_vm || true)"
HAS_OLD_VM=1
if [[ -z "${OLD_VM_NAME}" ]]; then
  HAS_OLD_VM=0
  log "No existing VM resolved from MIG, OpenTofu, backend health, or labels; continuing in bootstrap mode (no old-state copy)"
else
  log "Preflight: create paranoid local SQLite backup before deploy actions"
  PRE_FLIGHT_ARCHIVE="$(mktemp /tmp/freetool-db-preflight.XXXXXX.tgz)"
  cleanup_files+=("${PRE_FLIGHT_ARCHIVE}")
  gcloud compute ssh "${OLD_VM_NAME}" --project "${GCP_PROJECT_ID}" --zone "${GCP_VM_ZONE}" --tunnel-through-iap --command "sudo tar -C ${OLD_DATA_ROOT} -czf - freetool-db openfga" > "${PRE_FLIGHT_ARCHIVE}"
  LOCAL_SQLITE_BACKUP_DIR="${LOCAL_SQLITE_BACKUP_ROOT}/preflight-$(date +%Y%m%d%H%M%S)-${OLD_VM_NAME}"
  mkdir -p "${LOCAL_SQLITE_BACKUP_DIR}"
  tar -C "${LOCAL_SQLITE_BACKUP_DIR}" -xzf "${PRE_FLIGHT_ARCHIVE}" freetool-db openfga
  find "${LOCAL_SQLITE_BACKUP_DIR}" -type f \( -name '*.db' -o -name '*.db-*' -o -name '*.sqlite' -o -name '*.sqlite-*' \) | sort > "${LOCAL_SQLITE_BACKUP_DIR}/sqlite-files.txt"
  if [[ ! -s "${LOCAL_SQLITE_BACKUP_DIR}/sqlite-files.txt" ]]; then
    echo "Preflight backup validation failed: no sqlite files found in ${LOCAL_SQLITE_BACKUP_DIR}" >&2
    exit 1
  fi
  log "Preflight SQLite backup complete at ${LOCAL_SQLITE_BACKUP_DIR}"
fi

T0="$(now)"

log "Build/push ${IMAGE_URI}"
gcloud auth configure-docker "${REGISTRY_HOST}" --quiet
docker build --platform linux/amd64 -f src/Freetool.Api/Dockerfile -t "${IMAGE_URI}" .
docker push "${IMAGE_URI}"

log "Provision green via OpenTofu (capacity 0)"
if (( HAS_OLD_VM == 1 )); then
  apply_tofu_state 1 0 true "${TAG}" 1
else
  apply_tofu_state 0 0 true "${TAG}" 0
fi
[[ -n "${GREEN_VM_NAME:-}" ]] || { echo "OpenTofu did not return bluegreen_vm_name" >&2; exit 1; }

wait_for_ssh "${GREEN_VM_NAME}"
wait_local_health "${GREEN_VM_NAME}"

T1="$(now)"
PREWARM_SECONDS="$((T1-T0))"

if (( HAS_OLD_VM == 1 )); then
  log "Drain old backend via OpenTofu"
  apply_tofu_state "${PRIMARY_DRAIN_CAPACITY}" 0 true "${TAG}" 1
  OLD_BACKEND_DRAINED=1
  sleep "${DRAIN_SECONDS_TARGET}"

  T2="$(now)"
  DRAIN_SECONDS="$((T2-T1))"

  log "Stop stacks + copy sqlite dirs"
  stop_stack "${OLD_VM_NAME}"
  stop_stack "${GREEN_VM_NAME}"

  ARCHIVE="$(mktemp /tmp/freetool-db-sync.XXXXXX.tgz)"
  cleanup_files+=("${ARCHIVE}")
  gcloud compute ssh "${OLD_VM_NAME}" --project "${GCP_PROJECT_ID}" --zone "${GCP_VM_ZONE}" --tunnel-through-iap --command "sudo tar -C ${OLD_DATA_ROOT} -czf - freetool-db openfga" > "${ARCHIVE}"
  gcloud compute scp "${ARCHIVE}" "${GREEN_VM_NAME}:~/db-sync.tgz" --project "${GCP_PROJECT_ID}" --zone "${GCP_VM_ZONE}" --tunnel-through-iap
  gcloud compute ssh "${GREEN_VM_NAME}" --project "${GCP_PROJECT_ID}" --zone "${GCP_VM_ZONE}" --tunnel-through-iap --command "
    set -euo pipefail
    sudo rm -rf ${GREEN_DATA_ROOT}/freetool-db ${GREEN_DATA_ROOT}/openfga
    sudo mkdir -p ${GREEN_DATA_ROOT}
    sudo tar -C ${GREEN_DATA_ROOT} -xzf ~/db-sync.tgz
    rm -f ~/db-sync.tgz
    sudo chmod -R 0777 ${GREEN_DATA_ROOT}/freetool-db ${GREEN_DATA_ROOT}/openfga
  "

  T3="$(now)"
  COPY_SECONDS="$((T3-T2))"
else
  DRAIN_SECONDS=0
  COPY_SECONDS=0
  T3="$(now)"
fi

log "Restart green + enable traffic via OpenTofu"
start_stack "${GREEN_VM_NAME}"
wait_local_health "${GREEN_VM_NAME}"
apply_tofu_state 0 1 true "${TAG}" 1
GREEN_BACKEND_ENABLED=1
log "Wait for primary MIG to return to warm standby size 1"
wait_primary_mig_target_instances 1

T4="$(now)"
CUTOVER_SECONDS="$((T4-T3))"
TOTAL_SECONDS="$((T4-T0))"

cat <<SUMMARY
Timing summary:
  prewarm_seconds=${PREWARM_SECONDS}
  drain_seconds=${DRAIN_SECONDS}
  copy_seconds=${COPY_SECONDS}
  cutover_seconds=${CUTOVER_SECONDS}
  total_seconds=${TOTAL_SECONDS}

Resources:
  old_vm=${OLD_VM_NAME:-none}
  green_vm=${GREEN_VM_NAME}
  old_backend_group=${GCP_MIG_NAME}
  green_backend_group=${GREEN_IG_NAME}
  backend_service=${GCP_BACKEND_SERVICE}
  preflight_sqlite_backup_dir=${LOCAL_SQLITE_BACKUP_DIR:-none}

Note:
  Old backend traffic is set to capacity 0 via OpenTofu.
  Primary MIG target size is restored to 1 after successful cutover.
  Script waits until the primary MIG reports at least one standby instance.
SUMMARY

if command -v tailscale >/dev/null 2>&1; then
  tailscale up || true
fi
