---
name: openapi
description: Regenerate and review checked-in API contract artifacts.
mcp_server: internal-tools
mcp_tool: use_workflow
mcp_workflow: openapi
mcp_repo: freetool
mcp_kind: shared-stub
---

# Openapi

Call `internal-tools.use_workflow` with:

- `workflow_name="openapi"`
- `repo_name="freetool"`

If the task is not an obvious fit for this stub, call `internal-tools.recommend_workflows` first and then use the top shared workflow before editing.

Then follow the returned:

- files to inspect
- workflow steps
- validation commands
- related workflows

If the change also touches adjacent concerns, follow the related workflows returned by `internal-tools.use_workflow`.
