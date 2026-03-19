terraform {
  backend "gcs" {
    bucket = "wonderly-idp-sso-freetool-state"
    prefix = "infra/opentofu"
  }
}
