locals {
  project_id        = "wonderly-idp-sso"
  region            = "us-central1"
  project_slug      = replace(replace(lower(trimspace(var.project_name)), " ", "-"), "_", "-")
  state_bucket_name = trimspace(var.state_bucket_name) != "" ? trimspace(var.state_bucket_name) : "iac-state-${local.project_slug}"
  github_repository_name = (
    trimspace(var.github_repository_name) != ""
    ? trimspace(var.github_repository_name)
    : "internal-tool-freetool"
  )
  github_repository = "${var.github_repository_owner}/${local.github_repository_name}"
  deploy_branch_ref = startswith(var.github_deploy_branch, "refs/") ? var.github_deploy_branch : "refs/heads/${var.github_deploy_branch}"
  github_workload_identity_provider_id = (
    trimspace(var.github_workload_identity_provider_id) != ""
    ? trimspace(var.github_workload_identity_provider_id)
    : local.project_slug
  )
  deploy_service_account_id = (
    trimspace(var.deploy_service_account_id) != ""
    ? trimspace(var.deploy_service_account_id)
    : "${local.project_slug}-deploy"
  )
}

data "google_project" "current" {
  project_id = local.project_id
}

resource "google_project_service" "required" {
  for_each = toset([
    "artifactregistry.googleapis.com",
    "container.googleapis.com",
    "iam.googleapis.com",
    "iamcredentials.googleapis.com",
    "storage.googleapis.com",
    "sts.googleapis.com",
  ])

  service            = each.value
  disable_on_destroy = false
}

resource "google_storage_bucket" "terraform_state" {
  name                        = local.state_bucket_name
  location                    = local.region
  storage_class               = var.state_bucket_storage_class
  uniform_bucket_level_access = true
  public_access_prevention    = "enforced"
  force_destroy               = var.state_bucket_force_destroy
  project                     = local.project_id

  versioning {
    enabled = true
  }

  lifecycle {
    prevent_destroy = true
  }

  depends_on = [google_project_service.required]
}

resource "google_iam_workload_identity_pool_provider" "github_actions" {
  workload_identity_pool_id          = var.github_workload_identity_pool_id
  workload_identity_pool_provider_id = local.github_workload_identity_provider_id
  display_name                       = var.github_workload_identity_provider_display_name
  description                        = "GitHub Actions OIDC provider for Freetool deploys"
  project                            = data.google_project.current.number

  attribute_mapping = {
    "google.subject"             = "assertion.sub"
    "attribute.actor"            = "assertion.actor"
    "attribute.aud"              = "assertion.aud"
    "attribute.ref"              = "assertion.ref"
    "attribute.repository"       = "assertion.repository"
    "attribute.repository_owner" = "assertion.repository_owner"
  }

  attribute_condition = "assertion.repository=='${local.github_repository}' && assertion.ref=='${local.deploy_branch_ref}'"

  oidc {
    issuer_uri = "https://token.actions.githubusercontent.com"
  }
}

resource "google_service_account" "deploy" {
  account_id   = local.deploy_service_account_id
  display_name = "Freetool Deploy"
  project      = local.project_id
}

resource "google_service_account_iam_member" "deploy_workload_identity_user" {
  service_account_id = google_service_account.deploy.name
  role               = "roles/iam.workloadIdentityUser"
  member             = "principalSet://iam.googleapis.com/projects/${data.google_project.current.number}/locations/global/workloadIdentityPools/${var.github_workload_identity_pool_id}/attribute.repository/${local.github_repository}"
}

resource "google_project_iam_member" "deploy_roles" {
  for_each = toset([
    "roles/artifactregistry.writer",
    "roles/container.clusterViewer",
  ])

  project = local.project_id
  role    = each.value
  member  = "serviceAccount:${google_service_account.deploy.email}"
}

resource "google_storage_bucket_iam_member" "deploy_state_bucket_object_admin" {
  bucket = google_storage_bucket.terraform_state.name
  role   = "roles/storage.objectAdmin"
  member = "serviceAccount:${google_service_account.deploy.email}"
}

resource "google_storage_bucket_iam_member" "deploy_state_bucket_reader" {
  bucket = google_storage_bucket.terraform_state.name
  role   = "roles/storage.legacyBucketReader"
  member = "serviceAccount:${google_service_account.deploy.email}"
}
