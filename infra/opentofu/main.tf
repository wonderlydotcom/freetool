locals {
  lb_name                       = "${var.name_prefix}-lb"
  vm_name                       = "${var.name_prefix}-vm"
  bluegreen_vm_name             = "${var.name_prefix}-green-vm"
  bluegreen_ig_name             = "${var.name_prefix}-green-ig"
  data_disk_name                = "${var.name_prefix}-data"
  data_disk_self_link           = var.preserve_data_disk_on_destroy ? google_compute_disk.data_protected[0].self_link : google_compute_disk.data_unprotected[0].self_link
  bluegreen_effective_image_tag = trimspace(var.bluegreen_image_tag) != "" ? trimspace(var.bluegreen_image_tag) : var.initial_image_tag
  google_directory_credentials_secret_name = (
    trimspace(var.google_directory_credentials_secret_name) != ""
    ? trimspace(var.google_directory_credentials_secret_name)
    : try(google_secret_manager_secret.google_directory_credentials[0].secret_id, "")
  )
  domain_parts = split(".", var.domain_name)
  iap_email_domain = length(local.domain_parts) > 2 ? join(
    ".",
    slice(local.domain_parts, length(local.domain_parts) - 2, length(local.domain_parts))
  ) : var.domain_name
  iap_effective_access_members = length(var.iap_access_members) > 0 ? var.iap_access_members : ["domain:${local.iap_email_domain}"]
  labels = {
    app         = "freetool"
    managed_by  = "opentofu"
    environment = "prod"
  }
}

resource "google_project_service" "required" {
  for_each = toset([
    "artifactregistry.googleapis.com",
    "compute.googleapis.com",
    "iap.googleapis.com",
    "dns.googleapis.com",
    "secretmanager.googleapis.com",
  ])

  service            = each.value
  disable_on_destroy = false
}

resource "google_compute_network" "freetool" {
  name                    = "${var.name_prefix}-vpc"
  auto_create_subnetworks = false
}

resource "google_compute_subnetwork" "freetool" {
  name          = "${var.name_prefix}-subnet"
  ip_cidr_range = "10.30.0.0/24"
  network       = google_compute_network.freetool.id
  region        = var.region
}

resource "google_artifact_registry_repository" "freetool" {
  location               = var.artifact_registry_location
  repository_id          = var.artifact_registry_repo
  format                 = "DOCKER"
  cleanup_policy_dry_run = var.artifact_cleanup_policy_dry_run

  cleanup_policies {
    id     = "keep-recent-freetool-api"
    action = "KEEP"

    most_recent_versions {
      package_name_prefixes = [var.image_name]
      keep_count            = var.artifact_keep_recent_count
    }
  }

  cleanup_policies {
    id     = "delete-older-images"
    action = "DELETE"

    condition {
      tag_state  = "ANY"
      older_than = var.artifact_delete_older_than
    }
  }

  depends_on = [google_project_service.required]
}

resource "google_service_account" "vm" {
  account_id   = "${var.name_prefix}-vm-sa"
  display_name = "Freetool VM Service Account"
}

resource "google_project_iam_member" "vm_artifact_reader" {
  project = var.project_id
  role    = "roles/artifactregistry.reader"
  member  = "serviceAccount:${google_service_account.vm.email}"
}

resource "google_project_iam_member" "vm_logging_writer" {
  project = var.project_id
  role    = "roles/logging.logWriter"
  member  = "serviceAccount:${google_service_account.vm.email}"
}

resource "google_secret_manager_secret" "google_directory_credentials" {
  count = trimspace(var.google_directory_service_account_key_json) != "" ? 1 : 0

  project   = var.project_id
  secret_id = "${var.name_prefix}-google-directory-dwd-key"

  replication {
    auto {}
  }

  depends_on = [google_project_service.required]
}

resource "google_secret_manager_secret_version" "google_directory_credentials" {
  count = trimspace(var.google_directory_service_account_key_json) != "" ? 1 : 0

  secret      = google_secret_manager_secret.google_directory_credentials[0].id
  secret_data = var.google_directory_service_account_key_json
}

resource "google_secret_manager_secret_iam_member" "vm_google_directory_credentials_accessor" {
  count = local.google_directory_credentials_secret_name != "" ? 1 : 0

  project   = var.project_id
  secret_id = local.google_directory_credentials_secret_name
  role      = "roles/secretmanager.secretAccessor"
  member    = "serviceAccount:${google_service_account.vm.email}"
}

resource "google_compute_disk" "data_protected" {
  count = var.preserve_data_disk_on_destroy ? 1 : 0

  name = local.data_disk_name
  type = var.data_disk_type
  zone = var.zone
  size = var.data_disk_size_gb

  physical_block_size_bytes = 4096
  labels                    = local.labels

  lifecycle {
    prevent_destroy = true
  }
}

resource "google_compute_disk" "data_unprotected" {
  count = var.preserve_data_disk_on_destroy ? 0 : 1

  name = local.data_disk_name
  type = var.data_disk_type
  zone = var.zone
  size = var.data_disk_size_gb

  physical_block_size_bytes = 4096
  labels                    = local.labels
}

resource "google_compute_instance_template" "freetool" {
  name_prefix  = "${var.name_prefix}-tpl-"
  machine_type = var.machine_type
  tags         = ["${var.name_prefix}-freetool"]

  lifecycle {
    create_before_destroy = true
  }

  disk {
    boot         = true
    auto_delete  = true
    source_image = "ubuntu-os-cloud/ubuntu-2204-lts"
    disk_size_gb = var.boot_disk_size_gb
    disk_type    = "pd-balanced"
  }

  network_interface {
    subnetwork = google_compute_subnetwork.freetool.id

    access_config {
      // Ephemeral external IP for package/image pulls and direct troubleshooting.
      // The app itself is served via the HTTPS load balancer.
    }
  }

  service_account {
    email  = google_service_account.vm.email
    scopes = ["cloud-platform"]
  }

  metadata_startup_script = templatefile("${path.module}/templates/startup.sh.tmpl", {
    artifact_registry_location                   = var.artifact_registry_location
    project_id                                   = var.project_id
    artifact_registry_repo                       = var.artifact_registry_repo
    image_name                                   = var.image_name
    initial_image_tag                            = var.initial_image_tag
    iap_jwt_audience                             = var.iap_jwt_audience
    google_directory_enabled                     = var.google_directory_enabled
    google_directory_admin_user_email            = var.google_directory_admin_user_email
    google_directory_scope                       = var.google_directory_scope
    google_directory_org_unit_key_prefix         = var.google_directory_org_unit_key_prefix
    google_directory_include_org_unit_hierarchy  = var.google_directory_include_org_unit_hierarchy
    google_directory_custom_attribute_key_prefix = var.google_directory_custom_attribute_key_prefix
    google_directory_credentials_secret_name     = local.google_directory_credentials_secret_name
    org_admin_email                              = var.org_admin_email
    validate_iap_jwt                             = var.validate_iap_jwt
    data_mount_path                              = var.data_mount_path
  })

  labels = local.labels

  depends_on = [
    google_project_iam_member.vm_artifact_reader,
    google_project_iam_member.vm_logging_writer,
    google_artifact_registry_repository.freetool,
    google_project_service.required,
  ]
}

resource "google_compute_instance_group_manager" "freetool" {
  name               = "${var.name_prefix}-mig"
  zone               = var.zone
  base_instance_name = local.vm_name
  target_size        = var.primary_mig_target_size

  version {
    instance_template = google_compute_instance_template.freetool.id
  }

  named_port {
    name = "http"
    port = 8080
  }

  update_policy {
    type                  = "PROACTIVE"
    minimal_action        = "REPLACE"
    max_surge_fixed       = 0
    max_unavailable_fixed = 1
  }

  auto_healing_policies {
    health_check      = google_compute_health_check.freetool.id
    initial_delay_sec = var.primary_mig_autohealing_initial_delay_sec
  }
}

resource "google_compute_instance" "bluegreen" {
  count = var.bluegreen_enabled ? 1 : 0

  name         = local.bluegreen_vm_name
  machine_type = var.machine_type
  zone         = var.zone
  tags         = ["${var.name_prefix}-freetool"]

  boot_disk {
    initialize_params {
      image = "ubuntu-os-cloud/ubuntu-2204-lts"
      size  = var.boot_disk_size_gb
      type  = "pd-balanced"
    }
  }

  attached_disk {
    source      = local.data_disk_self_link
    device_name = local.data_disk_name
    mode        = "READ_WRITE"
  }

  network_interface {
    subnetwork = google_compute_subnetwork.freetool.id

    access_config {
      // Ephemeral external IP for package/image pulls and direct troubleshooting.
      // The app itself is served via the HTTPS load balancer.
    }
  }

  service_account {
    email  = google_service_account.vm.email
    scopes = ["cloud-platform"]
  }

  metadata_startup_script = templatefile("${path.module}/templates/startup-bluegreen.sh.tmpl", {
    artifact_registry_location                   = var.artifact_registry_location
    project_id                                   = var.project_id
    artifact_registry_repo                       = var.artifact_registry_repo
    image_name                                   = var.image_name
    image_tag                                    = local.bluegreen_effective_image_tag
    iap_jwt_audience                             = var.iap_jwt_audience
    google_directory_enabled                     = var.google_directory_enabled
    google_directory_admin_user_email            = var.google_directory_admin_user_email
    google_directory_scope                       = var.google_directory_scope
    google_directory_org_unit_key_prefix         = var.google_directory_org_unit_key_prefix
    google_directory_include_org_unit_hierarchy  = var.google_directory_include_org_unit_hierarchy
    google_directory_custom_attribute_key_prefix = var.google_directory_custom_attribute_key_prefix
    google_directory_credentials_secret_name     = local.google_directory_credentials_secret_name
    org_admin_email                              = var.org_admin_email
    validate_iap_jwt                             = var.validate_iap_jwt
    data_disk_name                               = local.data_disk_name
    data_mount_path                              = var.data_mount_path
  })

  labels = merge(local.labels, { role = "bluegreen" })

  depends_on = [
    google_project_iam_member.vm_artifact_reader,
    google_project_iam_member.vm_logging_writer,
    google_artifact_registry_repository.freetool,
    google_project_service.required,
  ]
}

resource "google_compute_instance_group" "bluegreen" {
  count = var.bluegreen_enabled ? 1 : 0

  name      = local.bluegreen_ig_name
  zone      = var.zone
  instances = [google_compute_instance.bluegreen[0].self_link]

  named_port {
    name = "http"
    port = 8080
  }
}

resource "google_compute_firewall" "allow_ssh" {
  name    = "${var.name_prefix}-allow-ssh"
  network = google_compute_network.freetool.name

  allow {
    protocol = "tcp"
    ports    = ["22"]
  }

  source_ranges = var.allow_ssh_from
  target_tags   = ["${var.name_prefix}-freetool"]
}

resource "google_compute_firewall" "allow_lb_to_app" {
  name    = "${var.name_prefix}-allow-lb-to-app"
  network = google_compute_network.freetool.name

  allow {
    protocol = "tcp"
    ports    = ["8080"]
  }

  source_ranges = ["35.191.0.0/16", "130.211.0.0/22"]
  target_tags   = ["${var.name_prefix}-freetool"]
}

resource "google_compute_health_check" "freetool" {
  name               = "${var.name_prefix}-hc"
  check_interval_sec = 10
  timeout_sec        = 5

  http_health_check {
    port         = 8080
    request_path = "/swagger/index.html"
  }
}

resource "google_compute_backend_service" "freetool" {
  name                  = "${var.name_prefix}-backend"
  protocol              = "HTTP"
  port_name             = "http"
  timeout_sec           = 30
  health_checks         = [google_compute_health_check.freetool.id]
  load_balancing_scheme = "EXTERNAL_MANAGED"

  backend {
    group           = google_compute_instance_group_manager.freetool.instance_group
    balancing_mode  = "UTILIZATION"
    capacity_scaler = var.primary_backend_capacity
  }

  dynamic "backend" {
    for_each = var.bluegreen_enabled ? [google_compute_instance_group.bluegreen[0].self_link] : []
    content {
      group           = backend.value
      balancing_mode  = "UTILIZATION"
      capacity_scaler = var.bluegreen_backend_capacity
    }
  }

  iap {
    enabled              = true
    oauth2_client_id     = var.oauth2_client_id
    oauth2_client_secret = var.oauth2_client_secret
  }

  depends_on = [google_project_service.required]
}

resource "google_iap_web_backend_service_iam_binding" "freetool_access" {
  project             = var.project_id
  web_backend_service = google_compute_backend_service.freetool.name
  role                = "roles/iap.httpsResourceAccessor"
  members             = local.iap_effective_access_members
}

resource "google_compute_url_map" "freetool" {
  name            = "${local.lb_name}-url-map"
  default_service = google_compute_backend_service.freetool.id

  host_rule {
    hosts        = [var.domain_name]
    path_matcher = "freetool"
  }

  path_matcher {
    name            = "freetool"
    default_service = google_compute_backend_service.freetool.id
  }
}

resource "google_compute_managed_ssl_certificate" "freetool" {
  name = "${local.lb_name}-cert-${replace(var.domain_name, ".", "-")}"

  managed {
    domains = [var.domain_name]
  }

  lifecycle {
    create_before_destroy = true
  }
}

resource "google_compute_target_https_proxy" "freetool" {
  name             = "${local.lb_name}-https-proxy"
  url_map          = google_compute_url_map.freetool.id
  ssl_certificates = [google_compute_managed_ssl_certificate.freetool.id]
}

resource "google_compute_global_address" "freetool" {
  name = "${local.lb_name}-ip"
}

resource "google_compute_global_forwarding_rule" "https" {
  name                  = "${local.lb_name}-https"
  ip_protocol           = "TCP"
  load_balancing_scheme = "EXTERNAL_MANAGED"
  port_range            = "443"
  target                = google_compute_target_https_proxy.freetool.id
  ip_address            = google_compute_global_address.freetool.id
}

resource "google_compute_url_map" "http_redirect" {
  name = "${local.lb_name}-http-redirect"

  default_url_redirect {
    https_redirect         = true
    redirect_response_code = "MOVED_PERMANENTLY_DEFAULT"
    strip_query            = false
  }
}

resource "google_compute_target_http_proxy" "redirect" {
  name    = "${local.lb_name}-http-proxy"
  url_map = google_compute_url_map.http_redirect.id
}

resource "google_compute_global_forwarding_rule" "http" {
  name                  = "${local.lb_name}-http"
  ip_protocol           = "TCP"
  load_balancing_scheme = "EXTERNAL_MANAGED"
  port_range            = "80"
  target                = google_compute_target_http_proxy.redirect.id
  ip_address            = google_compute_global_address.freetool.id
}

resource "google_dns_record_set" "freetool" {
  count = var.dns_managed_zone == "" ? 0 : 1

  name         = "${var.domain_name}."
  type         = "A"
  ttl          = 300
  managed_zone = var.dns_managed_zone
  rrdatas      = [google_compute_global_address.freetool.address]
}
