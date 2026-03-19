# App Repo OpenTofu

This stack is the app-side companion to `../internal-tools-infra`.

It intentionally does **not** create cluster, namespace, ingress, DNS, IAP, service accounts, PVCs, services, network policies, or `platform-contract`. Those are platform-owned resources created from the shared app catalog.

This stack manages only app-owned deployment resources:

- one `StatefulSet` that runs in the platform-created namespace
- one app-owned `ConfigMap` exposed through `envFrom`

Unlike the thinner control-plane pattern, the Freetool workload needs two persistent SQLite stores. The shared PVC is therefore split into two subdirectories that preserve the current VM disk layout:

- `freetool-db/` mounted into the API container at `/app/data`
- `openfga/` mounted into the OpenFGA sidecar at `/home/nonroot`

## Expected Input

Get the contract for your app from the shared infra repo:

```bash
tofu -chdir=../internal-tools-infra/platform/apps output -json app_contracts
```

Copy the entry for `freetool` into `platform_contract` in `terraform.tfvars`.

The values that matter here are:

- `namespace`
- `runtime_service_account`
- `service_name`
- `pvc_name`
- `health_check_path`
- `runtime_contract_config_map`
- `secret_provider_class`
- `artifact_registry_repo`
- `iap_jwt_audience`
- `required_pod_labels`

## Backend

The shared platform creates a per-app GCS bucket for this repo's state, and this stack now checks in the matching backend configuration. Initialize with:

```bash
tofu init -input=false
```

If this working copy was previously initialized against another backend, run `tofu init -input=false -migrate-state` once instead.

For syntax-only validation without a configured backend, use:

```bash
tofu init -backend=false
```

## Runtime Contract

The deployed workload is expected to:

- run in the platform-created `app-*` namespace
- use `serviceAccountName: runtime`
- mount the platform PVC
- expose the platform-required pod label `internal-tools.wonderly.io/service=app`
- answer the platform health check path on port `8080`
- run the Freetool API and OpenFGA in the same pod so the API can use `http://127.0.0.1:8090`
- mount the platform-managed `SecretProviderClass` when the contract declares runtime secrets

The `platform-contract` ConfigMap is injected with `envFrom` exactly as the shared platform docs describe. This stack also injects a derived app config map that wires:

- `ConnectionStrings__DefaultConnection`
- `OpenFGA__ApiUrl`
- `Auth__IAP__JwtAudience`
- `Auth__GoogleDirectory__CredentialsFile` when runtime secrets are present

## Deploy

After the stack has been initialized, the normal app repo deployment path is:

```bash
scripts/deploy-app-from-tofu.sh
```

The script:

- builds and pushes the app image to the per-app Artifact Registry repo
- applies `infra/opentofu` with the selected immutable `image_tag`
- waits for the `StatefulSet` rollout in the platform-created namespace

Useful overrides:

```bash
IMAGE_TAG=$(git rev-parse --short HEAD) scripts/deploy-app-from-tofu.sh
PUBLISH_LATEST=true scripts/deploy-app-from-tofu.sh
scripts/deploy-app-from-tofu.sh -var-file=environments/dev/terraform.tfvars.example
```
