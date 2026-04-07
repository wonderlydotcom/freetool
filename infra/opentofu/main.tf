locals {
  secret_provider_class_value = try(trimspace(var.platform_contract.secret_provider_class), "")
  secret_provider_class_name  = local.secret_provider_class_value != "" ? local.secret_provider_class_value : null

  image_ref = format(
    "%s-docker.pkg.dev/%s/%s/%s:%s",
    var.artifact_registry_location,
    var.project_id,
    var.platform_contract.artifact_registry_repo,
    var.image_name,
    var.image_tag,
  )

  managed_labels = {
    "app.kubernetes.io/name"       = var.workload_name
    "app.kubernetes.io/managed-by" = "opentofu"
  }

  resource_labels = merge(var.extra_resource_labels, local.managed_labels)
  pod_labels      = merge(var.extra_pod_labels, local.managed_labels, var.platform_contract.required_pod_labels)
  config_map_name = "${var.workload_name}-config"

  mandatory_app_config = merge(
    {
      ConnectionStrings__DefaultConnection = format("Data Source=%s/freetool.db", var.data_mount_path)
      OpenFGA__ApiUrl                      = var.openfga_api_url
      Auth__IAP__JwtAudience               = var.platform_contract.iap_jwt_audience
    },
    local.secret_provider_class_name == null || trimspace(var.google_directory_credentials_relative_path) == "" ? {} : {
      Auth__GoogleDirectory__CredentialsFile = "${var.runtime_secrets_mount_path}/${trimspace(var.google_directory_credentials_relative_path)}"
    }
  )

  effective_app_config = merge(var.app_config, local.mandatory_app_config)

  pod_annotations = merge(
    var.pod_annotations,
    {
      "internal-tools.wonderly.io/platform-contract-sha" = sha256(jsonencode({
        health_check_path           = var.platform_contract.health_check_path
        iap_jwt_audience            = var.platform_contract.iap_jwt_audience
        runtime_contract_config_map = var.platform_contract.runtime_contract_config_map
        secret_provider_class       = local.secret_provider_class_name
      }))
      "internal-tools.wonderly.io/runtime-config-sha" = sha256(jsonencode(local.effective_app_config))
      "internal-tools.wonderly.io/openfga-sha" = sha256(jsonencode({
        image           = var.openfga_image
        data_mount_path = var.openfga_data_mount_path
        data_subpath    = var.openfga_pvc_subpath
      }))
    }
  )
}

resource "kubernetes_config_map_v1" "app_config" {
  metadata {
    name      = local.config_map_name
    namespace = var.platform_contract.namespace
    labels    = local.resource_labels
  }

  data = local.effective_app_config
}

resource "kubernetes_stateful_set_v1" "app" {
  wait_for_rollout = false

  metadata {
    name      = var.workload_name
    namespace = var.platform_contract.namespace
    labels    = local.resource_labels
  }

  spec {
    replicas     = 1
    service_name = var.platform_contract.service_name

    selector {
      match_labels = var.platform_contract.required_pod_labels
    }

    update_strategy {
      type = "RollingUpdate"
    }

    template {
      metadata {
        labels      = local.pod_labels
        annotations = local.pod_annotations
      }

      spec {
        service_account_name = var.platform_contract.runtime_service_account

        init_container {
          name    = "prepare-data-dirs"
          image   = "busybox:1.36"
          command = ["/bin/sh", "-c"]
          args = [
            "mkdir -p /mnt/pvc/${var.sqlite_pvc_subpath} /mnt/pvc/${var.openfga_pvc_subpath} && chmod -R a+rwX /mnt/pvc/${var.sqlite_pvc_subpath} /mnt/pvc/${var.openfga_pvc_subpath}"
          ]

          volume_mount {
            name       = "data"
            mount_path = "/mnt/pvc"
          }
        }

        init_container {
          name  = "openfga-migrate"
          image = var.openfga_image
          args  = ["migrate"]

          env {
            name  = "OPENFGA_DATASTORE_ENGINE"
            value = "sqlite"
          }

          env {
            name  = "OPENFGA_DATASTORE_URI"
            value = "file:${var.openfga_data_mount_path}/openfga.db"
          }

          volume_mount {
            name       = "data"
            mount_path = var.openfga_data_mount_path
            sub_path   = var.openfga_pvc_subpath
          }
        }

        container {
          name  = "api"
          image = local.image_ref

          port {
            name           = "http"
            container_port = 8080
          }

          env_from {
            config_map_ref {
              name = var.platform_contract.runtime_contract_config_map
            }
          }

          env_from {
            config_map_ref {
              name = kubernetes_config_map_v1.app_config.metadata[0].name
            }
          }

          readiness_probe {
            http_get {
              path = var.platform_contract.health_check_path
              port = 8080
            }

            initial_delay_seconds = 5
            period_seconds        = 10
            timeout_seconds       = 5
            failure_threshold     = 3
          }

          liveness_probe {
            http_get {
              path = var.platform_contract.health_check_path
              port = 8080
            }

            initial_delay_seconds = 15
            period_seconds        = 20
            timeout_seconds       = 5
            failure_threshold     = 3
          }

          volume_mount {
            name       = "data"
            mount_path = var.data_mount_path
            sub_path   = var.sqlite_pvc_subpath
          }

          dynamic "volume_mount" {
            for_each = local.secret_provider_class_name == null ? [] : [local.secret_provider_class_name]

            content {
              name       = "runtime-secrets"
              mount_path = var.runtime_secrets_mount_path
              read_only  = true
            }
          }
        }

        container {
          name  = "openfga"
          image = var.openfga_image
          args  = ["run"]

          port {
            name           = "openfga-http"
            container_port = 8090
          }

          env {
            name  = "OPENFGA_DATASTORE_ENGINE"
            value = "sqlite"
          }

          env {
            name  = "OPENFGA_DATASTORE_URI"
            value = "file:${var.openfga_data_mount_path}/openfga.db"
          }

          env {
            name  = "OPENFGA_LOG_FORMAT"
            value = "json"
          }

          env {
            name  = "OPENFGA_HTTP_ADDR"
            value = "0.0.0.0:8090"
          }

          env {
            name  = "OPENFGA_GRPC_ADDR"
            value = "0.0.0.0:8091"
          }

          volume_mount {
            name       = "data"
            mount_path = var.openfga_data_mount_path
            sub_path   = var.openfga_pvc_subpath
          }
        }

        volume {
          name = "data"

          persistent_volume_claim {
            claim_name = var.platform_contract.pvc_name
          }
        }

        dynamic "volume" {
          for_each = local.secret_provider_class_name == null ? [] : [local.secret_provider_class_name]

          content {
            name = "runtime-secrets"

            csi {
              driver = "secrets-store-gke.csi.k8s.io"

              read_only = true

              volume_attributes = {
                secretProviderClass = volume.value
              }
            }
          }
        }
      }
    }
  }

  depends_on = [kubernetes_config_map_v1.app_config]
}
