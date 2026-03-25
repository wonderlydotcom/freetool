project_id                 = "wonderly-idp-sso"
artifact_registry_location = "us-central1"
image_name                 = "freetool-api"
workload_name              = "app"
data_mount_path            = "/app/data"
runtime_secrets_mount_path = "/var/run/secrets/app"
openfga_image              = "openfga/openfga:latest"
openfga_api_url            = "http://127.0.0.1:8090"
openfga_data_mount_path    = "/home/nonroot"
sqlite_pvc_subpath         = "freetool-db"
openfga_pvc_subpath        = "openfga"

app_config = {
  ASPNETCORE_ENVIRONMENT                           = "Production"
  OpenFGA__OrgAdminEmail                          = "chander@wonderly.com"
  Auth__IAP__ValidateJwt                          = "true"
  Auth__GoogleDirectory__Enabled                  = "true"
  Auth__GoogleDirectory__AdminUserEmail           = "chander@wonderly.com"
  Auth__GoogleDirectory__Scope                    = "https://www.googleapis.com/auth/admin.directory.user.readonly"
  Auth__GoogleDirectory__OrgUnitKeyPrefix         = "ou"
  Auth__GoogleDirectory__IncludeOrgUnitHierarchy  = "true"
  Auth__GoogleDirectory__CustomAttributeKeyPrefix = "custom"
}

platform_contract = {
  namespace                   = "app-freetool"
  domain_name                 = "freetool.wonderly.info"
  runtime_service_account     = "runtime"
  service_name                = "app"
  pvc_name                    = "data"
  health_check_path           = "/healthy"
  runtime_contract_config_map = "platform-contract"
  secret_provider_class       = "app-secrets"
  artifact_registry_repo      = "freetool"
  state_bucket_name           = "wonderly-idp-sso-freetool-state"
  iap_jwt_audience            = "/projects/199626281531/global/backendServices/2541127857700558473"
  required_pod_labels = {
    "internal-tools.wonderly.io/service" = "app"
  }
}
