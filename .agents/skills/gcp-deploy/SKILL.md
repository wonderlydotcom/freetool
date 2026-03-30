---
name: gcp-deploy
description: Deploy a repo into the shared internal-tools GKE platform and verify rollout.
mcp_server: internal-tools
mcp_tool: use_workflow
mcp_workflow: gcp-deploy
mcp_repo: freetool
mcp_kind: shared-stub
---

# Gcp Deploy

Call `internal-tools.use_workflow` with:

- `workflow_name="gcp-deploy"`
- `repo_name="freetool"`

If the task is not an obvious fit for this stub, call `internal-tools.recommend_workflows` first and then use the top shared workflow before editing.

Then follow the returned:

- files to inspect
- workflow steps
- validation commands
- related workflows

If the change also touches adjacent concerns, follow the related workflows returned by `internal-tools.use_workflow`.
