# GCE + IAP Deployment With OpenTofu

This repo now includes infrastructure as code under `infra/opentofu`.

## What OpenTofu Provisions

- Custom VPC + subnet
- Artifact Registry Docker repo
- `e2-small` Compute Engine VM
- Dedicated persistent disk for app data (default 30 GB)
- VM service account + IAM for Artifact Registry pull
- VM startup bootstrap (Docker, Compose, gcloud, stack startup)
- Instance group + health check
- Global external HTTPS load balancer
- URL routing for root path (`/`)
- IAP enabled on backend service
- Managed SSL certificate
- Optional DNS A record in Cloud DNS

## Prereqs

- `tofu` installed locally
- `gcloud` installed and authenticated
- Billing-enabled GCP project
- A domain for TLS (or prepare one)
- IAP OAuth client ID/secret (pre-created in GCP Console)

## 1. Apply Infra (one-time, then incremental)

```bash
cd infra/opentofu
cp environments/dev/terraform.tfvars.example terraform.tfvars
# edit terraform.tfvars

tofu init
tofu plan
tofu apply
```

After apply, use output `load_balancer_ip` for DNS if you did not set `dns_managed_zone`.

For `Auth:IAP:JwtAudience`, do a second pass after backend creation:

```bash
# from repo root
BACKEND_NAME="$(cd infra/opentofu && tofu output -raw backend_service_name)"
PROJECT_ID="$(cd infra/opentofu && tofu output -raw project_id)"
BACKEND_ID="$(gcloud compute backend-services describe "$BACKEND_NAME" --global --project "$PROJECT_ID" --format='value(id)')"
PROJECT_NUMBER="$(gcloud projects describe "$PROJECT_ID" --format='value(projectNumber)')"
echo "/projects/${PROJECT_NUMBER}/global/backendServices/${BACKEND_ID}"
```

Set that value as `iap_jwt_audience` in `terraform.tfvars`, then run `tofu apply` again.

## 2. One-Command App Deploy (for each merge)

Recommended from repo root (auto-loads infra outputs):

```bash
./scripts/deploy-gce-from-tofu.sh
```

Optional override:

```bash
FREETOOL_ORG_ADMIN_EMAIL=you@example.com ./scripts/deploy-gce-from-tofu.sh
```

This builds/pushes image, uploads compose/env to VM, and runs `docker compose up -d`.

Manual mode is still available via `./scripts/deploy-gce.sh` if you want to pass env vars yourself.

## 3. Notes

- API is configured to run at root path (`/`) with no path base.
- Runtime data is bind-mounted from `${data_mount_path}` on a dedicated persistent disk.
- Disk defaults: `30 GB` (`pd-balanced`) with `preserve_data_disk_on_destroy=true`.
- `e2-small` is the default because the app and OpenFGA are too tight on `e2-micro` once production traffic is enabled.
