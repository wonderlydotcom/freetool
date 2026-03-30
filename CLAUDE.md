# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Project Overview

Freetool is an open-source internal tools platform (Retool alternative) built with F# and ASP.NET Core. The backend uses **Onion Architecture** with functional design patterns, while the frontend (in `www/`) is a React/TypeScript SPA using Vite, React Router, and shadcn/ui components.

## Shared MCP Skills

Shared internal-tools skills are served by the deployed `internal-tools-mcp` server.

- Codex reads [`.codex/config.toml`](./.codex/config.toml).
- Claude Code reads [`.mcp.json`](./.mcp.json) and [`.claude/settings.json`](./.claude/settings.json).
- No bearer token or local secret bootstrap is required before starting either client.
- Shared internal-tools workflows are now surfaced locally as thin `.agents/skills/*/SKILL.md` stubs that delegate to `internal-tools.use_workflow`.
- If the right shared workflow is not obvious, call `internal-tools.recommend_workflows` first, then call `internal-tools.use_workflow` for the top match before editing.
- Before editing EF Core mappings, repositories, or `DbContext` code, load `entity-framework-fsharp` first.
- Before editing schema or migration code, load `db-migrations` first.
- Consult the matching shared stub before infra, deploy, secret, OpenAPI, or review work when the task clearly maps to one of those workflows.
- After loading a primary workflow, also consult related shared stubs such as `domain-driven-design`, `event-sourcing-audit`, and `otel-tracing` when they exist in this repo and the task touches business rules, audit/events, or new request paths.
- Keep `freetool-controller-authoring`, `freetool-iap-auth-architecture`, and `freetool-openfga-hexagonal-architecture` as the full repo-local override skills.
- Use those repo-local skills instead of shared `new-controller`, `iap-auth`, and `openfga` in this repo.

## Essential Commands

### Backend (F#/.NET)
```bash
# Restore dependencies
dotnet restore Freetool.sln

# Build all projects
dotnet build Freetool.sln -c Release

# Run API locally (serves from www/ for static files)
dotnet run --project src/Freetool.Api/Freetool.Api.fsproj

# Run all tests
dotnet test Freetool.sln

# Run tests for specific project
dotnet test src/Freetool.Domain/test/Freetool.Domain.Tests.fsproj

# Run specific test by name filter
dotnet test --filter "FullyQualifiedName~NameOfTest"

# Format code
dotnet format Freetool.sln
```

### Frontend (React/TypeScript)
```bash
cd www

# Install dependencies
npm install

# Development server
npm run dev

# Build for production
npm run build

# Build for development (with source maps)
npm run build:dev

# Lint
npm run lint

# Format
npm run format

# Run tests
npm test

# Run tests in watch mode
npm run test:watch

# Run tests with UI
npm run test:ui
```

### Docker
```bash
# Start all services (API, OpenFGA, Aspire Dashboard)
docker-compose up --build

# Start specific service
docker-compose up --build freetool-api

# View logs
docker-compose logs -f freetool-api
```

**Service URLs:**
- API: http://localhost:5001
- Swagger UI: http://localhost:5001/swagger
- OTEL/Aspire Dashboard: http://localhost:18888
- OpenFGA: http://localhost:8090
- OpenFGA Playground: http://localhost:3030

## Architecture Overview

### Layered Onion Architecture

The F# backend follows strict dependency inversion - all dependencies point inward toward the domain:

**1. Domain Layer** (`src/Freetool.Domain/`) - Innermost layer, zero dependencies
- **Entities/**: Aggregates with business rules (User, App, Resource, Folder, Space, Run)
  - Each entity is an F# record with a `State` field (business data) and `UncommittedEvents` list
  - Domain methods are pure functions that validate, update state, and collect events
  - Example: `App.updateName` validates the name, updates the aggregate, and adds an `AppUpdatedEvent`
- **ValueObjects/**: Immutable value types with validation (UserId, Email, AppName, etc.)
- **Events/**: Domain events representing business facts (UserCreatedEvent, AppUpdatedEvent, etc.)
- **Services/**: Pure domain services for complex business logic
- **Types.fs**: Core domain types (`DomainError` discriminated union, etc.)
- **EventSourcingAggregate.fs**: Base aggregate pattern for event collection

**2. Application Layer** (`src/Freetool.Application/`) - Orchestration and use cases
- **Commands/**: Command discriminated unions define all operations
  - Pattern: `type UserCommand = CreateUser of ... | UpdateUser of ... | DeleteUser of ...`
  - Result types: `type UserCommandResult = UserResult of UserDto | UsersResult of PagedResult<UserDto> | ...`
- **Handlers/**: Handler modules implement command pattern matching
  - Pattern: Module-based handlers (e.g., `UserHandler`) with `handleCommand` function
  - Each handler matches command cases and orchestrates domain + repository calls
  - Handlers implement `IGenericCommandHandler<TRepository, TCommand, TResult>` interface
- **DTOs/**: Data transfer objects for API boundary (no business logic)
- **Interfaces/**: Repository and service contracts (e.g., `IUserRepository`, `IAuthorizationService`)
- **Mappers/**: Domain ↔ DTO conversions
- **Services/**: Application services (e.g., `EventEnhancementService` for enriching audit events)

**3. Infrastructure Layer** (`src/Freetool.Infrastructure/`) - External integrations
- **Database/**: Entity Framework Core, repositories, migrations
  - **FreetoolDbContext.fs**: EF Core DbContext
  - **Repositories/**: Repository implementations (UserRepository, AppRepository, etc.)
  - **Migrations/**: DBUp SQL scripts named `DatabaseUpgradeScripts.DBUP.{number}_{description}.sql`
  - Migration pattern: Embedded resources, auto-run on startup
- **Services/**: External service integrations
  - **OpenFgaService.fs**: OpenFGA client for authorization
  - **EventPublisher.fs**: Event publishing infrastructure

**4. API Layer** (`src/Freetool.Api/`) - HTTP endpoints
- **Controllers/**: ASP.NET Core controllers (UserController, AppController, etc.)
  - Controllers call handlers via DI-injected `IGenericCommandHandler` instances
- **Middleware/**: HTTP middleware (AuthzMiddleware for OpenFGA checks)
- **Tracing/**: AutoTracing system for OpenTelemetry instrumentation
- **Program.fs**: Application entry point and DI configuration

### Command/Handler Pattern

This codebase uses a **functional command pattern** instead of OOP use cases:

1. **Define Command DU**: Create discriminated union in `Commands/` (e.g., `AppCommand`)
2. **Define Result DU**: Create result type for all possible outcomes (e.g., `AppCommandResult`)
3. **Implement Handler**: Create handler module in `Handlers/` with `handleCommand` function
   - Use pattern matching on command cases
   - Call domain methods (which create events)
   - Call repository to persist (events saved atomically with data)
4. **Wire in DI**: Register handler in `Program.fs` with AutoTracing decorator
5. **Call from Controller**: Inject `IGenericCommandHandler` and invoke `HandleCommand`

**Example Flow:**
```fsharp
// 1. Command definition
type AppCommand = UpdateAppName of actorUserId: UserId * appId: string * UpdateAppNameDto

// 2. Handler pattern match
match command with
| UpdateAppName(actorUserId, appId, dto) ->
    let! app = repository.GetByIdAsync appId
    // Domain method validates and creates event
    match App.updateName actorUserId dto.Name app with
    | Ok updatedApp ->
        // Repository saves data + events atomically
        repository.UpdateAsync updatedApp
```

### Event Sourcing Pattern

**Transactional Event Sourcing** ensures 1:1 consistency between business operations and audit trail:

1. **Domain Layer**: Entities collect uncommitted events as methods execute
   - Pattern: Each domain method returns updated entity with new events appended
   - Events stored in `UncommittedEvents: IDomainEvent list` field
2. **Application Layer**: Handlers orchestrate domain + repository
3. **Infrastructure Layer**: Repositories save business data + events in **same transaction**
   - Pattern: Begin transaction → Save entity → Save events → Commit
   - `EventRepository.SaveEventAsync` persists to `Events` table
4. **Audit API**: Events queryable via audit endpoints

**Event Schema:**
- `Events` table stores all domain events with metadata (EntityId, EventType, EventData JSON, OccurredAt)
- Events are immutable - never updated or deleted

### Adding Events for New Entities - Audit Log Checklist

When adding a new entity or event type, you **MUST** update these locations to ensure proper audit log display. Missing any step causes "Unknown" entity names or parse errors in audit logs.

**1. Domain Events** (`src/Freetool.Domain/src/Events/NewEntityEvents.fs`)
- Define event types: `NewEntityCreatedEvent`, `NewEntityUpdatedEvent`, `NewEntityDeletedEvent`
- **CRITICAL**: Always include entity `Name` in `CreatedEvent` AND `DeletedEvent` for audit display
- Include `EventId`, `OccurredAt`, `ActorUserId` in all events
- For `UpdatedEvent`, the name is looked up from repository at display time

**2. EventType Registry** (`src/Freetool.Domain/src/Entities/Event.fs`)
- Add to `EventType` discriminated union
- Add to `EventTypeConverter.toString` and `.fromString`
- Add to `EntityType` discriminated union
- Add to `EntityTypeConverter.toString` and `.fromString`

**3. EventRepository** (`src/Freetool.Infrastructure/src/Database/Repositories/EventRepository.fs`)
- Add pattern match case for each event type in `SaveEventAsync`
- Map to correct `EntityType`
- Extract `entityId` correctly

**4. EventEnhancementService** (`src/Freetool.Application/src/Services/EventEnhancementService.fs`)
- Add name extraction logic for each event type in `extractEntityNameFromEventDataAsync`
- **CRITICAL**: For `UpdatedEvent`, use repository lookup to get current entity name
- For `DeletedEvent`, extract name from event data (since entity is already deleted)
- Add summary generation in `generateEventSummary`
- Inject required repository in constructor if not already present

**5. Program.fs DI Registration** (`src/Freetool.Api/src/Program.fs`)
- If EventEnhancementService needs a new repository, add it to the constructor call

**6. Frontend Types** (`www/src/features/space/components/AuditLogView.tsx`)
- Update `EventType` and `EntityType` type unions

**Testing Checklist:**
- [ ] CreatedEvent displays entity name correctly
- [ ] UpdatedEvent displays entity name (not "Unknown")
- [ ] DeletedEvent displays entity name (not "Unknown")
- [ ] No parse errors in audit log for new events

## OpenTelemetry AutoTracing System

The codebase has a **zero-configuration tracing system** that automatically instruments all command handlers:

### How It Works
1. **Naming Convention**: Command names auto-generate span names
   - `CreateApp` → `"app.create"`
   - `UpdateAppName` → `"app.update_app_name"`
2. **Automatic Attributes**: Command parameters and results extracted as OTEL attributes
3. **Security**: Sensitive fields (password, token, secret, key, credential) automatically filtered
4. **Pure Reflection**: Uses F# reflection to maintain architecture separation

### Adding Tracing to New Handlers

In `Program.fs`, wrap your handler with `AutoTracing.createTracingDecorator`:

```fsharp
builder.Services.AddScoped<IGenericCommandHandler<IAppRepository, AppCommand, AppCommandResult>>
    (fun serviceProvider ->
        let handler = serviceProvider.GetRequiredService<AppHandler>()
        let activitySource = serviceProvider.GetRequiredService<ActivitySource>()
        AutoTracing.createTracingDecorator "app" handler activitySource)
```

**Parameters:**
- First arg: Entity name prefix for spans (e.g., `"app"`, `"user"`, `"folder"`)
- Second arg: Handler instance
- Third arg: ActivitySource for OTEL

## OpenFGA Authorization

Fine-grained, relationship-based authorization via OpenFGA (Google Zanzibar):

### Model Hierarchy
- **Organization**: Global admins with all permissions on all spaces
- **Space**: Top-level container with 10 permissions (create/edit/delete for resource/app/folder, plus run_app). Spaces have moderators (full permissions) and members (specific permissions)
- **Permissions**: Relationship tuples like `(user:alice, create_app, space:main)`

### Authorization Checks

Controllers use `AuthzMiddleware` which:
1. Extracts user from Google IAP identity headers
2. Checks permission via `IAuthorizationService.CheckPermissionAsync`
3. Returns 403 if unauthorized

**Manual Check Example:**
```fsharp
let! canCreate = authService.CheckPermissionAsync("user:alice", "create_resource", "space:main")
if not canCreate then
    return Error(PermissionDenied "Insufficient permissions")
```

### Managing Relationships

```fsharp
// Create relationship (grant permission)
authService.CreateRelationshipsAsync([
    { User = "user:bob"; Relation = "create_app"; Object = "space:eng" }
])

// Update relationships atomically (e.g., promote member to moderator)
authService.UpdateRelationshipsAsync({
    TuplesToAdd = [{ User = "user:carol"; Relation = "moderator"; Object = "space:eng" }]
    TuplesToRemove = [{ User = "user:carol"; Relation = "member"; Object = "space:eng" }]
})

// Delete relationship (revoke permission)
authService.DeleteRelationshipsAsync([
    { User = "user:dave"; Relation = "run_app"; Object = "space:eng" }
])
```

## Database Migrations with DBUp

The project uses **DBUp** (not EF migrations) for full SQL control:

### Adding a Migration
1. **Create SQL file** in `src/Freetool.Infrastructure/src/Database/Migrations/`
   - Naming: `DatabaseUpgradeScripts.DBUP.{number}_{description}.sql`
   - Example: `DatabaseUpgradeScripts.DBUP.005_AddColumnToApps.sql`
2. **Add to .fsproj** as embedded resource:
   ```xml
   <EmbeddedResource Include="src/Database/Migrations/DatabaseUpgradeScripts.DBUP.005_AddColumnToApps.sql" />
   ```
3. **Restart app** - DBUp auto-detects and runs new scripts

**Benefits:**
- Version controlled SQL scripts
- Database agnostic (SQLite, Postgres, SQL Server)
- No merge conflicts with migration files
- Explicit rollback scripts when needed

## F# Project File Ordering

F# requires strict file ordering in `.fsproj` - dependencies must appear before dependents:

**Correct Order:**
1. Value objects (e.g., `UserId.fs`, `Email.fs`)
2. Core types (e.g., `Types.fs`)
3. Entities that use value objects (e.g., `User.fs`, `App.fs`)
4. Services that use entities
5. Handlers that use everything

**If you add a new file**, ensure it's ordered correctly or you'll get compilation errors.

## Testing Strategy

Tests colocated in `test/` subdirectories within each project:

### Domain Tests (`src/Freetool.Domain/test/`)
- **Fast, pure, isolated** - no external dependencies
- Test business rules, entity behavior, event generation
- Use xUnit: `[<Fact>]` for single scenarios, `[<Theory>]` for data-driven
- Example: Test that `App.updateName` generates correct `AppUpdatedEvent`

### Application Tests (`src/Freetool.Application/test/`)
- Test handler orchestration, DTO mapping, validation
- Mock repositories using interfaces
- Verify command handling logic

### Infrastructure Tests (`src/Freetool.Infrastructure/test/`)
- Test repositories with in-memory database
- Test OpenFGA integration
- Test event persistence

### API Tests (`src/Freetool.Api/test/`)
- End-to-end integration tests
- Test HTTP endpoints with full middleware pipeline
- Test authorization checks

**Running Specific Tests:**
```bash
# All tests
dotnet test

# Single project
dotnet test src/Freetool.Domain/test/

# By name filter
dotnet test --filter "FullyQualifiedName~UpdateApp"
```

## Frontend Architecture

The `www/` directory contains a React/TypeScript SPA:

### Key Technologies
- **Vite**: Build tool and dev server
- **Vitest**: Fast, Vite-native test framework with React Testing Library
- **React Router**: Client-side routing
- **shadcn/ui**: UI component library (Radix UI + Tailwind)
- **TanStack Query**: Server state management
- **React Hook Form + Zod**: Form handling and validation
- **openapi-fetch**: Type-safe API client generated from OpenAPI spec

### Structure
- `src/components/`: Reusable UI components
- `src/pages/`: Page components (routes)
- `src/lib/`: Utilities, API client, hooks
- `src/hooks/`: Custom React hooks

### API Integration
The frontend uses `openapi-fetch` to generate a type-safe client from `openapi.spec.json` at the repo root. After backend API changes:
1. Regenerate OpenAPI spec (if using Swagger gen)
2. Regenerate TypeScript types: `npm run generate-api-types` (if configured)
3. Frontend automatically gets type checking for API calls

## Frontend Code Quality Standards

The frontend uses **Biome** for linting and formatting - a unified, high-performance tool that replaces ESLint + Prettier. **All linting must pass with zero warnings before committing.**

### Signoff Workflow

Use `./scripts/signoff-pr.sh` before requesting review or signing off a pull request.

**What it does:**
- Detects whether the branch changes backend files under `src/` or frontend files under `www/`
- Runs the relevant verification steps only for the changed area
- Ensures the branch has an upstream
- Creates a pull request with `gh pr create` if one does not already exist
- Signs off the pull request with `gh signoff`

**Checks run by the script:**

**For backend changes**:
- `dotnet tool run fantomas .` - Format the repository
- `dotnet build Freetool.sln -c Release` - Build the solution
- `dotnet test Freetool.sln` - Run backend tests

**For frontend changes**:
- `cd www && npm run check` - Lint, format, and auto-fix with Biome
- `cd www && npm run lint` - Verify zero lint warnings
- `cd www && npm run format` - Normalize formatting

**Requirements:**
- GitHub CLI installed (`gh`)
- GitHub signoff extension installed: `gh extension install basecamp/gh-signoff`

**Note:** Fix all warnings before signing off a PR. The signoff script expects the repository checks to pass.

### Why Biome?

- **Unified Tool**: Single tool replaces ESLint + Prettier (faster dev experience)
- **Rust-based**: 10-100x faster than JavaScript tools
- **80+ Strict Rules**: More comprehensive than previous ESLint setup
- **Zero-config**: Works with sensible defaults
- **React-aware**: Built-in React best practices

### Core Biome Rules

#### 1. No `any` Types (EVER)

The `any` type bypasses TypeScript's type checking and is **strictly forbidden**.

**Bad:**
```typescript
function processData(data: any) {
  return data.map((item: any) => item.value);
}
```

**Good:**
```typescript
interface DataItem {
  value: string;
  id: number;
}

function processData(data: DataItem[]) {
  return data.map((item) => item.value);
}

// If you truly don't know the type, use `unknown` with type guards
function processUnknown(data: unknown) {
  if (Array.isArray(data)) {
    return data.filter((item): item is DataItem =>
      typeof item === 'object' && item !== null && 'value' in item
    );
  }
  return [];
}
```

#### 2. Component Files Export Only Components

React Fast Refresh requires component files to export **only React components**. Extract everything else to separate files.

**⚠️ CODE REVIEW CHECKPOINT**: This pattern is enforced via code review (Biome does not have an equivalent to ESLint's `react-refresh/only-export-components` rule). Pay close attention during PR reviews to ensure component files only export components.

**Bad:**
```typescript
// MyComponent.tsx
export const BUTTON_VARIANTS = { primary: "...", secondary: "..." };

export function useMyHook() {
  return useState(0);
}

export function MyComponent() {
  return <div>Hello</div>;
}
```

**Good:**
```typescript
// MyComponent.variants.ts
export const BUTTON_VARIANTS = { primary: "...", secondary: "..." };

// MyComponent.hooks.ts
export function useMyHook() {
  return useState(0);
}

// MyComponent.tsx
import { BUTTON_VARIANTS } from './MyComponent.variants';
import { useMyHook } from './MyComponent.hooks';

export function MyComponent() {
  return <div>Hello</div>;
}
```

**Naming Convention:**
- `*.variants.ts` - Constants, variant configs, enums
- `*.hooks.ts` - Custom React hooks
- `*.utils.ts` - Utility functions
- `*.types.ts` - Type definitions and interfaces

#### 3. Fix React Hook Dependency Warnings

React Hook dependency warnings are **not suggestions - they are bug reports**. Every warning represents a potential stale closure bug.

**Bad:**
```typescript
const [count, setCount] = useState(0);

useEffect(() => {
  const timer = setInterval(() => {
    console.log(count); // Stale closure! Always logs 0
  }, 1000);
  return () => clearInterval(timer);
}, []); // Missing dependency: count
```

**Good - Option 1: Add the dependency**
```typescript
useEffect(() => {
  const timer = setInterval(() => {
    console.log(count); // Always current
  }, 1000);
  return () => clearInterval(timer);
}, [count]);
```

**Good - Option 2: Use functional updates**
```typescript
useEffect(() => {
  const timer = setInterval(() => {
    setCount(c => c + 1); // No dependency needed
  }, 1000);
  return () => clearInterval(timer);
}, []);
```

**Good - Option 3: Use refs for values that shouldn't trigger re-runs**
```typescript
const countRef = useRef(count);
useEffect(() => { countRef.current = count; });

useEffect(() => {
  const timer = setInterval(() => {
    console.log(countRef.current); // Always current, no re-runs
  }, 1000);
  return () => clearInterval(timer);
}, []);
```

**Never disable the warning with `biome-ignore` unless you have a documented architectural reason.**

#### 4. Use ES6 Imports Only

Never use `require()` - always use ES6 `import` statements.

**Bad:**
```typescript
const React = require('react');
const { useState } = require('react');
```

**Good:**
```typescript
import React, { useState } from 'react';
import type { ReactNode } from 'react';
```

#### 5. No Empty Interfaces

Empty interfaces are forbidden - they provide no type safety.

**Bad:**
```typescript
interface Props {}

function MyComponent(props: Props) {
  return <div />;
}
```

**Good - Option 1: Use proper types**
```typescript
interface Props {
  className?: string;
  children?: ReactNode;
}
```

**Good - Option 2: Use type alias for empty props**
```typescript
type Props = Record<string, never>;
```

**Good - Option 3: Extend existing types**
```typescript
interface ButtonProps extends React.ButtonHTMLAttributes<HTMLButtonElement> {
  variant?: 'primary' | 'secondary';
}
```

### Running Biome

```bash
cd www

# Check for linting issues only
npm run lint

# Format code only
npm run format

# Check and fix both linting and formatting (recommended)
npm run check
```

### Common Biome Violations and Fixes

| Violation | Fix |
|-----------|-----|
| `suspicious/noExplicitAny` | Replace `any` with proper interface/type or `unknown` |
| `correctness/useExhaustiveDependencies` | Add missing dependencies or use functional updates |
| `correctness/noUnusedImports` | Remove unused imports |
| `correctness/noUnusedVariables` | Remove unused variables or prefix with `_` if intentionally unused |
| `suspicious/noConsoleLog` | Remove console.log or use proper logging |
| `suspicious/noDebugger` | Remove debugger statements |
| `style/noVar` | Replace `var` with `const` or `let` |
| `style/useConst` | Use `const` instead of `let` for variables that are never reassigned |

### Why These Rules Matter

1. **No `any`**: TypeScript without types is just JavaScript with extra steps. The `any` type defeats the entire purpose of TypeScript and hides bugs.

2. **Component exports**: Fast Refresh (hot module reloading) breaks when component files export non-components. This slows down development significantly. **This is enforced via code review.**

3. **Hook dependencies**: Missing dependencies cause stale closures - the most common React bug. These are real bugs that will bite you in production.

4. **ES6 imports**: CommonJS `require()` doesn't work with TypeScript's type system and tree-shaking. Always use ES6 modules.

5. **No console.log**: Console statements should be removed before committing. Use proper logging or debugging tools instead.

6. **Prefer const**: Using `const` for variables that don't change makes code more predictable and prevents accidental reassignment bugs.

## Deployment

Freetool is designed to run behind **Google Cloud IAP** at the root path (`/`).

**Authentication:** IAP sets trusted identity headers for authenticated users, and the app uses those headers for JIT provisioning and authorization.

## Configuration

### Environment Variables
- `ASPNETCORE_ENVIRONMENT`: `Development` or `Production`
- `OTEL_EXPORTER_OTLP_ENDPOINT`: OpenTelemetry collector endpoint (default: `http://localhost:4317`)
- `OTEL_SERVICE_NAME`: Service name for traces (default: `freetool-api`)
- `OpenFGA:ApiUrl`: OpenFGA server URL (default: `http://openfga:8090`)
- `OpenFGA:StoreId`: OpenFGA store ID (empty until store created)
- `Auth:IAP:JwtAudience`: **Required in production** for IAP JWT assertion validation
- `Auth:IAP:JwtIssuer`: IAP JWT issuer (default: `https://cloud.google.com/iap`)
- `Auth:IAP:JwtCertsUrl`: Google IAP JWK URL (default: `https://www.gstatic.com/iap/verify/public_key-jwk`)

### appsettings Files
- `appsettings.json`: Base configuration
- `appsettings.Development.json`: Development overrides (SQLite connection string, etc.)
- `appsettings.Production.json`: Production settings (not committed)

**Secret Management:** Use user secrets or environment variables for sensitive config - never commit secrets.

## Common Patterns

### Adding a New Entity

1. **Domain Layer:**
   - Create value object (e.g., `NewEntityId.fs`) in `ValueObjects/`
   - **If creating a new ID type**: Add it to `FSharpSchemaFilter.fs` (see step 6)
   - Create entity (e.g., `NewEntity.fs`) in `Entities/` with business logic
   - Create events (e.g., `NewEntityEvents.fs`) in `Events/`
   - Update `.fsproj` file ordering

2. **Application Layer:**
   - Create DTOs in `DTOs/NewEntityDtos.fs`
   - Create commands in `Commands/NewEntityCommands.fs`
   - Create handler in `Handlers/NewEntityHandler.fs`
   - Create repository interface in `Interfaces/INewEntityRepository.fs`
   - Create mapper in `Mappers/NewEntityMapper.fs`

3. **Infrastructure Layer:**
   - Create EF entity mapping in `FreetoolDbContext.fs` (see "Entity Framework Configuration" below)
   - Create repository in `Database/Repositories/NewEntityRepository.fs`
   - Add migration script
   - Update `.fsproj` file ordering

4. **API Layer:**
   - Create controller in `Controllers/NewEntityController.fs`
   - Register dependencies in `Program.fs` (repository, handler with tracing)
   - Update `.fsproj` file ordering

5. **Test Each Layer:**
   - Domain tests for business rules
   - Application tests for handler logic
   - Infrastructure tests for repository
   - API tests for endpoints

6. **OpenAPI Schema Filter** (if you created a new ID value object):
   - Add to `src/Freetool.Api/src/OpenApi/FSharpSchemaFilter.fs`
   - Find the ID type list (around line 23-29) and add your new ID type
   - Example: `|| context.Type = typeof<NewEntityId>`

7. **Regenerate Frontend Types:**
   - Start backend: `docker compose up -d`
   - Export spec: `curl http://localhost:5001/swagger/v1/swagger.json > openapi.spec.json`
   - Generate types: `cd www && npm run generate-api-types`
   - Verify: `cd www && npx tsc --noEmit`

### Adding a New Command to Existing Entity

1. Add command case to command DU in `Commands/`
2. Add handler case in `Handlers/` (pattern match new command)
3. Create domain method if needed (or reuse existing)
4. Add controller endpoint in `Controllers/`
5. Add tests
6. **Regenerate frontend types** (see step 7 above)

**Note:** AutoTracing automatically picks up new commands - no extra configuration needed!

### Adding a New Property to Existing Entity

When adding a new field/property to an existing entity (e.g., adding `Description` to `App`):

1. **Domain Layer:** Add field to entity record in `Entities/`
2. **Application Layer:** Add field to DTO in `DTOs/`, update mapper in `Mappers/`
3. **Infrastructure Layer:**
   - Add migration script for the new column
   - **CRITICAL:** Add property conversion in `FreetoolDbContext.fs` (see "Entity Framework Configuration" below)
4. **API Layer:** Update controller if needed (new endpoints, changed response shape)
5. **Regenerate frontend types** (see step 7 in "Adding a New Entity")

### Entity Framework Configuration

**Location:** `src/Freetool.Infrastructure/src/Database/FreetoolDbContext.fs`

When adding properties to entities, you MUST configure them in `OnModelCreating` if they use F# types that need conversion:

| F# Type | Required Converter | Example |
|---------|-------------------|---------|
| `string option` | `optionStringConverter` | `entity.Property(fun a -> a.Description).HasConversion(optionStringConverter)` |
| `int option` | `optionIntConverter` | `entity.Property(fun a -> a.Count).HasConversion(optionIntConverter)` |
| Custom value objects | Custom `ValueConverter` | See existing examples in DbContext |
| Lists (e.g., `Input list`) | JSON converter | `entity.Property(fun a -> a.Inputs).HasConversion<string>(inputListConverter)` |
| Discriminated unions | Custom converter | See `EventType`, `EntityType` examples |

**Why this matters:** Without proper converters, Entity Framework silently ignores columns when reading from the database, causing fields to always be `None`/null even when data exists in the DB. The PUT/POST may work (writes succeed) but GET fails to return the data.

## Common Gotchas

### Backend
1. **F# File Ordering**: Compilation errors about "not defined" usually mean wrong file order in `.fsproj`
2. **Event Not Saved**: Ensure repository calls `eventRepository.SaveEventAsync` in transaction
3. **Tracing Missing**: Check that handler is wrapped with `AutoTracing.createTracingDecorator` in DI
4. **Authorization Failing**: Verify OpenFGA store has correct relationships (`CreateRelationshipsAsync`)
5. **Migration Not Running**: Ensure SQL file is marked as `<EmbeddedResource>` in `.fsproj`
5a. **New ID Type Not Serializing as String**: Add the ID type to `FSharpSchemaFilter.fs` - see "Adding a New Entity" step 6
5b. **New Property Missing from GET Response**: If a new field saves correctly (PUT/POST works) but doesn't appear in GET responses, you're missing the Entity Framework converter in `FreetoolDbContext.fs` - see "Entity Framework Configuration" section

### Audit Log Issues
6. **Entity Name Shows "Unknown"**: Update/Delete events need proper name extraction in EventEnhancementService - use repository lookups for updates, include Name in deleted events
7. **Parse Error in Audit Log**: Event schema changed but old events in DB have different structure - wipe DB during development or add fallback deserialization

### Frontend
8. **Frontend Type Errors / `never` Types**: If `response.data` is typed as `never` or API calls show type mismatches, regenerate types: `docker compose up -d && curl http://localhost:5001/swagger/v1/swagger.json > openapi.spec.json && cd www && npm run generate-api-types`
9. **Signoff Script Fails on Frontend Checks**: Run `cd www && npm run check && npm run lint && npm run format` to fix the repo before signing off the PR
10. **Fast Refresh Not Working**: Component files must only export components - see [Frontend Code Quality Standards](#frontend-code-quality-standards)
11. **Stale State in useEffect**: Fix React Hook dependency warnings immediately - they are real bugs
12. **TypeScript Errors with `any`**: Never use `any` type - use proper interfaces or `unknown` with type guards
13. **F# Discriminated Union JSON Format**: When sending F# DUs to the backend, use `{ case: "CaseName" }` format (or `{ case: "CaseName", fields: [...] }` for DUs with parameters). The OpenAPI spec shows the *serialization* format (`{ tag: N, is*: true }`), but deserialization requires the `case` format. See `www/src/lib/inputTypeMapper.ts` for the correct pattern.

## Type Safety Guidelines

### Domain Layer: Strong Typing
The domain layer must maintain strong type safety using F# discriminated unions and value objects:
- **Never use `string` for typed data** in the domain layer - create proper value objects (e.g., `Email`, `DefaultValue`, `InputType`)
- **Validation at boundaries**: Parse and validate strings into typed values at the Application/DTO layer boundary
- **String only in DTOs**: Use `string option` only in DTOs where we interface with user-submitted data
- Example: `DefaultValue` uses typed DU cases (`IntegerDefault of int`, `EmailDefault of Email`) internally, but converts to/from `string` at the DTO boundary via `ToRawString()` and `DefaultValue.Create()`

## Code Style

### Backend (F#)
- **Indentation**: 4 spaces (F# standard)
- **Naming**: PascalCase for types/modules/DU cases, camelCase for functions/values
- **Immutability**: Prefer immutable records, pure functions
- **Pattern Matching**: Exhaustive pattern matching on DUs (compiler enforces)
- **Error Handling**: Use `Result<'T, DomainError>` for domain operations
- **Comments**: Avoid obvious comments; code should be self-documenting
- **Format Before Commit**: Run `dotnet format Freetool.sln`

### Frontend (TypeScript/React)
- **Indentation**: 2 spaces (JavaScript/TypeScript standard)
- **Naming**: PascalCase for components/types/interfaces, camelCase for functions/variables/props
- **Type Safety**: Never use `any` - see [Frontend Code Quality Standards](#frontend-code-quality-standards)
- **Component Organization**: One component per file, extract non-components to separate files
- **Imports**: Always use ES6 `import`, never `require()`
- **React Hooks**: Always fix dependency warnings - they are bugs, not suggestions
- **Format Before Commit**: Run `npm run format` from `www/`
- **Lint Must Pass**: Run `npm run lint` from `www/` - **zero warnings required**

### Frontend Task Completion Checklist

Before declaring any frontend task complete, you MUST:
1. Run `cd www && npm run check` (lint + format with auto-fix)
2. Verify zero warnings with `npm run lint`
3. Run tests with `npm test`

This ensures code quality gates pass before commits.
