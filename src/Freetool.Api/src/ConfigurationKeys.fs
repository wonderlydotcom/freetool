namespace Freetool.Api

/// Centralized configuration keys to avoid magic strings throughout the codebase.
/// Using [<Literal>] enables compile-time string substitution and IDE autocomplete.
module ConfigurationKeys =

    /// Connection string key for the default database connection
    [<Literal>]
    let DefaultConnection = "DefaultConnection"

    /// OpenFGA authorization service configuration keys
    module OpenFGA =
        /// The API URL for the OpenFGA service
        [<Literal>]
        let ApiUrl = "OpenFGA:ApiUrl"

        /// The store ID for the OpenFGA authorization store
        [<Literal>]
        let StoreId = "OpenFGA:StoreId"

        /// Email address of the organization admin (for automatic admin setup)
        [<Literal>]
        let OrgAdminEmail = "OpenFGA:OrgAdminEmail"

    /// Google IAP claim mapping keys
    module Auth =
        module IAP =
            [<Literal>]
            let ValidateJwt = "Auth:IAP:ValidateJwt"

            [<Literal>]
            let EmailHeader = "Auth:IAP:EmailHeader"

            [<Literal>]
            let NameHeader = "Auth:IAP:NameHeader"

            [<Literal>]
            let PictureHeader = "Auth:IAP:PictureHeader"

            [<Literal>]
            let GroupsHeader = "Auth:IAP:GroupsHeader"

            [<Literal>]
            let GroupsDelimiter = "Auth:IAP:GroupsDelimiter"

            [<Literal>]
            let JwtAssertionHeader = "Auth:IAP:JwtAssertionHeader"

            [<Literal>]
            let JwtAudience = "Auth:IAP:JwtAudience"

            [<Literal>]
            let JwtIssuer = "Auth:IAP:JwtIssuer"

            [<Literal>]
            let JwtCertsUrl = "Auth:IAP:JwtCertsUrl"

        module GoogleDirectory =
            [<Literal>]
            let Enabled = "Auth:GoogleDirectory:Enabled"

            [<Literal>]
            let AdminUserEmail = "Auth:GoogleDirectory:AdminUserEmail"

            [<Literal>]
            let Scope = "Auth:GoogleDirectory:Scope"

            [<Literal>]
            let OrgUnitKeyPrefix = "Auth:GoogleDirectory:OrgUnitKeyPrefix"

            [<Literal>]
            let IncludeOrgUnitHierarchy = "Auth:GoogleDirectory:IncludeOrgUnitHierarchy"

            [<Literal>]
            let CustomAttributeKeyPrefix = "Auth:GoogleDirectory:CustomAttributeKeyPrefix"

            [<Literal>]
            let CredentialsFile = "Auth:GoogleDirectory:CredentialsFile"

    /// Environment variable keys
    module Environment =
        /// OpenTelemetry Protocol (OTLP) exporter endpoint
        [<Literal>]
        let OtlpEndpoint = "OTEL_EXPORTER_OTLP_ENDPOINT"

        /// Development mode flag (set to "true" to enable dev features)
        [<Literal>]
        let DevMode = "FREETOOL_DEV_MODE"