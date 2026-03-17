variable "project_id" {
  description = "GCP project ID"
  type        = string
}

variable "region" {
  description = "GCP region"
  type        = string
}

variable "zone" {
  description = "GCP zone"
  type        = string
}

variable "name_prefix" {
  description = "Prefix used for resource names"
  type        = string
  default     = "freetool"
}

variable "domain_name" {
  description = "Public DNS name used for HTTPS (e.g. freetool.example.com)"
  type        = string
}

variable "dns_managed_zone" {
  description = "Optional Cloud DNS managed zone name for creating an A record"
  type        = string
  default     = ""
}

variable "oauth2_client_id" {
  description = "OAuth client ID used by IAP"
  type        = string
  default     = ""

  validation {
    condition     = trimspace(var.oauth2_client_id) != ""
    error_message = "Set oauth2_client_id."
  }
}

variable "oauth2_client_secret" {
  description = "OAuth client secret used by IAP"
  type        = string
  sensitive   = true
  default     = ""

  validation {
    condition     = trimspace(var.oauth2_client_secret) != ""
    error_message = "Set oauth2_client_secret."
  }
}

variable "machine_type" {
  description = "GCE machine type"
  type        = string
  default     = "e2-small"
}

variable "boot_disk_size_gb" {
  description = "Boot disk size in GB for the VM"
  type        = number
  default     = 10
}

variable "primary_backend_capacity" {
  description = "Capacity scaler for the primary MIG backend (0..1)"
  type        = number
  default     = 1

  validation {
    condition     = var.primary_backend_capacity >= 0 && var.primary_backend_capacity <= 1
    error_message = "primary_backend_capacity must be between 0 and 1."
  }
}

variable "bluegreen_enabled" {
  description = "Whether the OpenTofu-managed green VM/instance-group backend is provisioned"
  type        = bool
  default     = false

  validation {
    condition     = !var.bluegreen_enabled || var.preserve_data_disk_on_destroy
    error_message = "When bluegreen_enabled is true, preserve_data_disk_on_destroy must be true."
  }
}

variable "bluegreen_backend_capacity" {
  description = "Capacity scaler for the green backend (0..1)"
  type        = number
  default     = 0

  validation {
    condition     = var.bluegreen_backend_capacity >= 0 && var.bluegreen_backend_capacity <= 1
    error_message = "bluegreen_backend_capacity must be between 0 and 1."
  }
}

variable "bluegreen_image_tag" {
  description = "Image tag for the green environment. If empty, initial_image_tag is used."
  type        = string
  default     = ""
}

variable "primary_mig_target_size" {
  description = "Target size for the primary managed instance group."
  type        = number
  default     = 1

  validation {
    condition     = var.primary_mig_target_size >= 0
    error_message = "primary_mig_target_size must be >= 0."
  }
}

variable "primary_mig_autohealing_initial_delay_sec" {
  description = "Initial delay before managed instance group autohealing starts checking a new instance."
  type        = number
  default     = 300

  validation {
    condition     = var.primary_mig_autohealing_initial_delay_sec >= 0
    error_message = "primary_mig_autohealing_initial_delay_sec must be >= 0."
  }
}

variable "artifact_registry_repo" {
  description = "Artifact Registry Docker repository name"
  type        = string
  default     = "freetool"
}

variable "artifact_registry_location" {
  description = "Artifact Registry location"
  type        = string
  default     = "us-central1"
}

variable "image_name" {
  description = "Container image name within Artifact Registry"
  type        = string
  default     = "freetool-api"
}

variable "initial_image_tag" {
  description = "Initial image tag used on first boot"
  type        = string
  default     = "latest"
}

variable "org_admin_email" {
  description = "Optional Freetool org admin email"
  type        = string
  default     = ""
}

variable "validate_iap_jwt" {
  description = "Whether API should validate IAP JWT assertions"
  type        = bool
  default     = true
}

variable "iap_jwt_audience" {
  description = "IAP JWT audience passed to the API (Auth:IAP:JwtAudience)"
  type        = string
  default     = ""
}

variable "google_directory_enabled" {
  description = "Enable Google Workspace Directory lookup for OU/custom-attribute group keys"
  type        = bool
  default     = false
}

variable "google_directory_admin_user_email" {
  description = "Delegated admin user email for Google Directory domain-wide delegation"
  type        = string
  default     = ""
}

variable "google_directory_scope" {
  description = "OAuth scope used for Google Directory lookups"
  type        = string
  default     = "https://www.googleapis.com/auth/admin.directory.user.readonly"
}

variable "google_directory_org_unit_key_prefix" {
  description = "Group-key prefix used for org unit derived mappings"
  type        = string
  default     = "ou"
}

variable "google_directory_include_org_unit_hierarchy" {
  description = "Whether OU hierarchy keys should be emitted (e.g. /Eng and /Eng/Support)"
  type        = bool
  default     = true
}

variable "google_directory_custom_attribute_key_prefix" {
  description = "Group-key prefix used for custom schema derived mappings"
  type        = string
  default     = "custom"
}

variable "google_directory_credentials_secret_name" {
  description = "Secret Manager secret ID containing a JSON service account key used for Google Directory Domain-Wide Delegation. If empty and google_directory_service_account_key_json is set, this module creates a secret."
  type        = string
  default     = ""
}

variable "google_directory_service_account_key_json" {
  description = "Optional JSON key contents for a Google Directory DWD service account. Stored as a Secret Manager secret version when provided."
  type        = string
  sensitive   = true
  default     = ""

  validation {
    condition = !(
      trimspace(var.google_directory_credentials_secret_name) != "" &&
      trimspace(var.google_directory_service_account_key_json) != ""
    )
    error_message = "Set either google_directory_credentials_secret_name or google_directory_service_account_key_json, not both."
  }
}

variable "iap_access_members" {
  description = "Optional override for principals granted IAP-secured Web App User access. Leave empty to derive from domain_name."
  type        = list(string)
  default     = []
}

variable "allow_ssh_from" {
  description = "CIDRs allowed to SSH directly to VM"
  type        = list(string)
  default     = ["35.235.240.0/20"]
}

variable "data_disk_size_gb" {
  description = "Persistent data disk size in GB for Freetool and OpenFGA state"
  type        = number
  default     = 30
}

variable "data_disk_type" {
  description = "Persistent disk type for app data"
  type        = string
  default     = "pd-balanced"
}

variable "data_mount_path" {
  description = "Mount path used for the persistent data disk"
  type        = string
  default     = "/mnt/freetool-data"
}

variable "preserve_data_disk_on_destroy" {
  description = "Prevent accidental deletion of persistent data disk on tofu destroy"
  type        = bool
  default     = true
}

variable "artifact_cleanup_policy_dry_run" {
  description = "Whether Artifact Registry cleanup policy runs in dry-run mode"
  type        = bool
  default     = false
}

variable "artifact_keep_recent_count" {
  description = "How many recent versions to keep for the main app image package"
  type        = number
  default     = 5
}

variable "artifact_delete_older_than" {
  description = "Delete threshold for old image versions (e.g. 1d, 7d, 30d)"
  type        = string
  default     = "7d"
}
