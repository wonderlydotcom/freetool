# Foundation OpenTofu

This stack bootstraps the repo-owned Google Cloud identity used by GitHub Actions deployments.

It creates:

- one GCS bucket for the foundation stack's own remote state
- one repo-specific GitHub Actions Workload Identity Provider attached to the shared pool
- one deploy service account for this repository

It reuses the existing shared Workload Identity Pool `github-actions`; it does not try to create a new project-global pool per repo.

It does **not** create the app-owned state bucket used by `infra/opentofu`. The app stack now checks in its own remote backend target for this repo.

## Usage

First-time bootstrap must start from local state because this stack creates its own backend bucket.

1. Copy `terraform.tfvars.example` to `terraform.tfvars`.
2. Create a scratch copy of this directory without the `backend "gcs"` block and apply there.
3. Copy the resulting `terraform.tfstate` back into this directory.
4. Run `tofu init -migrate-state -force-copy -input=false`.

After that migration, normal commands can run directly from `infra/foundation/opentofu`.

## Outputs

Use these outputs to wire the deploy workflow:

- `project_id` -> `GCP_PROJECT_ID`
- `github_workload_identity_provider_name` -> `GCP_WORKLOAD_IDENTITY_PROVIDER`
- `github_deploy_service_account_email` -> `GCP_DEPLOY_SERVICE_ACCOUNT`
- `app_catalog_deployer_subject` -> copy into `deployer_subjects` in `../internal-tools-infra/catalog/apps/freetool.yaml`

The app deploy workflow still needs cluster and app-contract values:

- `GKE_CLUSTER_NAME`
- `GKE_CLUSTER_LOCATION`

The app deploy workflow now reads committed `infra/opentofu/terraform.tfvars` directly from checkout.
