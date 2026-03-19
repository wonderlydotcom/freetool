terraform {
  required_version = ">= 1.6.0"

  backend "gcs" {
    bucket = "iac-state-freetool"
    prefix = "freetool/foundation"
  }

  required_providers {
    google = {
      source  = "hashicorp/google"
      version = "~> 6.23"
    }
  }
}
