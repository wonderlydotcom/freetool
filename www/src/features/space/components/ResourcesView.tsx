import { useQueryClient } from "@tanstack/react-query";
import { ChevronLeft, Edit, Plus, Trash2 } from "lucide-react";
import { useCallback, useEffect, useState } from "react";
import {
  createResource,
  deleteResource,
  getApps,
  getResources,
} from "@/api/api";
import httpLogo from "@/assets/http.svg";
import postgresLogo from "@/assets/postgres.png";
import { PaginationControls } from "@/components/PaginationControls";
import { PermissionButton } from "@/components/PermissionButton";
import { PermissionGate } from "@/components/PermissionGate";
import { Button } from "@/components/ui/button";
import { Card, CardContent, CardHeader, CardTitle } from "@/components/ui/card";
import {
  Dialog,
  DialogContent,
  DialogFooter,
  DialogHeader,
  DialogTitle,
} from "@/components/ui/dialog";
import { Separator } from "@/components/ui/separator";
import {
  Tooltip,
  TooltipContent,
  TooltipProvider,
  TooltipTrigger,
} from "@/components/ui/tooltip";
import { usePagination } from "@/hooks/usePagination";
import { useHasPermission } from "@/hooks/usePermissions";
import { DEFAULT_PAGE_SIZE } from "@/lib/pagination";
import { useResourceForm } from "../hooks/useResourceForm";
import type { DatabaseConfig, KeyValuePair, ResourceKind } from "../types";
import {
  parseAuthFromHeaders,
  removeAuthFromHeaders,
} from "../utils/authUtils";
import ResourceForm, { type ResourceFormData } from "./ResourceForm";

const filterEmptyPairs = (pairs: KeyValuePair[]) =>
  pairs.filter((pair) => pair.key.trim() && pair.value.trim());

interface Resource {
  id: string;
  name: string;
  description: string;
  resourceKind: ResourceKind;
  baseUrl: string;
  urlParameters: KeyValuePair[];
  headers: KeyValuePair[];
  body: KeyValuePair[];
  databaseConfig: DatabaseConfig;
}

const emptyDatabaseConfig: DatabaseConfig = {
  databaseName: "",
  host: "",
  port: "",
  engine: "postgres",
  authScheme: "username_password",
  username: "",
  password: "",
  useSsl: false,
  enableSshTunnel: false,
  connectionOptions: [],
};

const initialFormData: ResourceFormData = {
  name: "",
  description: "",
  resourceKind: "http",
  baseUrl: "",
  urlParameters: [],
  headers: [],
  body: [],
  authConfig: { type: "none" },
  databaseConfig: emptyDatabaseConfig,
};

const getResourceLogo = (resourceKind: ResourceKind) =>
  resourceKind === "http" ? httpLogo : postgresLogo;

const getResourceLogoAlt = (resourceKind: ResourceKind) =>
  resourceKind === "http" ? "HTTP" : "PostgreSQL";

interface ResourcesViewProps {
  spaceId: string;
  spaceName?: string;
  onBackClick?: () => void;
}

export default function ResourcesView({
  spaceId,
  spaceName,
  onBackClick,
}: ResourcesViewProps) {
  const queryClient = useQueryClient();
  const [editingResource, setEditingResource] = useState<Resource | null>(null);
  const [resources, setResources] = useState<Resource[]>([]);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);
  const [creating, setCreating] = useState(false);
  const [createError, setCreateError] = useState<string | null>(null);
  const [showCreateForm, setShowCreateForm] = useState(false);
  const [apps, setApps] = useState<
    Array<{ id: string; name: string; resourceId?: string }>
  >([]);
  const [deletingResourceId, setDeletingResourceId] = useState<string | null>(
    null
  );

  // Pagination
  const { currentPage, pageSize, skip, totalPages, goToPage, setTotalCount } =
    usePagination();

  // Permission checks
  const canEditResource = useHasPermission(spaceId, "edit_resource");

  // Create form state
  const { formData: createFormData, setFormData: setCreateFormData } =
    useResourceForm(initialFormData);

  // Edit form state
  const {
    formData: editFormData,
    saveState,
    hasUnsavedChanges,
    saveConfig,
    discardChanges,
    setFormData: setEditFormData,
    resetFormData,
  } = useResourceForm(initialFormData, editingResource?.id, (updatedData) => {
    // Update the resources list when edit form data changes
    setResources((prev) =>
      prev.map((r) =>
        r.id === editingResource?.id ? { ...r, ...updatedData } : r
      )
    );
    // Update the editing resource
    setEditingResource((prev) => (prev ? { ...prev, ...updatedData } : null));
  });

  const fetchApps = useCallback(async () => {
    try {
      const allApps: Array<{ id: string; name: string; resourceId?: string }> =
        [];
      let appsSkip = 0;
      let hasMore = true;
      const pageSize = DEFAULT_PAGE_SIZE;

      while (hasMore) {
        const response = await getApps(appsSkip, pageSize);
        const items = response.data?.items ?? [];
        if (items.length === 0) {
          break;
        }

        allApps.push(
          ...items.map((item) => ({
            id: item.id ?? "",
            name: item.name,
            resourceId: item.resourceId,
          }))
        );

        const totalCount = response.data?.totalCount;
        const take = response.data?.take ?? items.length;
        appsSkip += items.length;
        hasMore =
          totalCount !== undefined
            ? appsSkip < totalCount
            : items.length === take;
      }

      setApps(allApps);
    } catch (_err) {
      // Silently handle fetch errors - apps list is for validation purposes only
    }
  }, []);

  const fetchResources = useCallback(async () => {
    try {
      setLoading(true);
      setError(null);
      const response = await getResources(spaceId, skip, pageSize);
      if (response.data?.items) {
        const mappedItems = response.data?.items.map((item) => {
          const resourceKind =
            item.resourceKind?.toLowerCase() === "sql" ? "sql" : "http";

          return {
            id: item.id ?? "",
            name: item.name,
            description: item.description,
            resourceKind,
            baseUrl: item.baseUrl ?? "",
            urlParameters: item.urlParameters || [],
            headers: item.headers || [],
            body: item.body || [],
            databaseConfig: {
              databaseName: item.databaseName ?? "",
              host: item.databaseHost ?? "",
              port: item.databasePort ? String(item.databasePort) : "",
              engine:
                item.databaseEngine?.toLowerCase() === "postgres"
                  ? "postgres"
                  : "postgres",
              authScheme:
                item.databaseAuthScheme?.toLowerCase() === "username_password"
                  ? "username_password"
                  : "username_password",
              username: item.databaseUsername ?? "",
              password: "",
              useSsl: item.useSsl ?? false,
              enableSshTunnel: item.enableSshTunnel ?? false,
              connectionOptions: item.connectionOptions || [],
            },
          } as Resource;
        });
        setResources(mappedItems);
        if (response.data.totalCount !== undefined) {
          setTotalCount(response.data.totalCount);
        }
      }
      // Also fetch apps to check resource usage
      await fetchApps();
    } catch (_err) {
      setError("Failed to load resources");
    } finally {
      setLoading(false);
    }
  }, [fetchApps, spaceId, skip, pageSize, setTotalCount]);

  const getAppsUsingResource = (resourceId: string) => {
    return apps.filter((app) => app.resourceId === resourceId);
  };

  const renderUsageSummary = (resourceId: string) => {
    const usedBy = getAppsUsingResource(resourceId);
    if (usedBy.length === 0) {
      return (
        <p className="text-xs text-muted-foreground">
          Not used by any apps yet.
        </p>
      );
    }

    const maxToShow = 3;
    const shown = usedBy.slice(0, maxToShow);
    const remaining = usedBy.length - shown.length;

    return (
      <div className="space-y-2">
        <p className="text-xs text-muted-foreground">
          Used in {usedBy.length} app{usedBy.length === 1 ? "" : "s"}:
        </p>
        <div className="flex flex-wrap gap-2">
          {shown.map((app) => (
            <span key={app.id} className="text-xs bg-muted px-2 py-1 rounded">
              {app.name}
            </span>
          ))}
          {remaining > 0 && (
            <span className="text-xs text-muted-foreground">
              +{remaining} more
            </span>
          )}
        </div>
      </div>
    );
  };

  const canDeleteResource = (resourceId: string) => {
    return getAppsUsingResource(resourceId).length === 0;
  };

  const handleDeleteResource = async (resourceId: string) => {
    if (!canDeleteResource(resourceId)) {
      return;
    }

    try {
      setDeletingResourceId(resourceId);
      await deleteResource(resourceId);
      await fetchResources();
      queryClient.invalidateQueries({ queryKey: ["resources", spaceId] });
    } catch (_err) {
      setError("Failed to delete resource. Please try again.");
    } finally {
      setDeletingResourceId(null);
    }
  };

  useEffect(() => {
    fetchResources();
  }, [fetchResources]);

  const handleCreateResource = async () => {
    const nameValue = createFormData.name.trim();
    const descriptionValue = createFormData.description.trim();
    const isHttp = createFormData.resourceKind === "http";

    if (!(nameValue && descriptionValue)) {
      setCreateError("Name and description are required");
      return;
    }

    if (isHttp && !createFormData.baseUrl.trim()) {
      setCreateError("Base URL is required for HTTP resources");
      return;
    }

    if (!isHttp) {
      const { databaseConfig } = createFormData;
      const parsedPort = Number.parseInt(databaseConfig.port, 10);
      if (
        !(
          databaseConfig.databaseName.trim() &&
          databaseConfig.host.trim() &&
          databaseConfig.port.trim() &&
          databaseConfig.username.trim() &&
          databaseConfig.password.trim()
        )
      ) {
        setCreateError(
          "Database name, host, port, username, and password are required"
        );
        return;
      }

      if (Number.isNaN(parsedPort)) {
        setCreateError("Database port must be a number");
        return;
      }
    }

    try {
      setCreating(true);
      setCreateError(null);

      let headersWithAuth = createFormData.headers;
      if (isHttp) {
        // Inject auth into headers before sending
        const { injectAuthIntoHeaders } = await import("../utils/authUtils");
        headersWithAuth = injectAuthIntoHeaders(
          createFormData.headers,
          createFormData.authConfig
        );
      }

      const parsedPort = Number.parseInt(
        createFormData.databaseConfig.port,
        10
      );

      if (isHttp) {
        await createResource({
          spaceId,
          name: nameValue,
          description: descriptionValue,
          resourceKind: "http",
          baseUrl: createFormData.baseUrl.trim(),
          urlParameters: filterEmptyPairs(createFormData.urlParameters),
          headers: filterEmptyPairs(headersWithAuth),
          body: filterEmptyPairs(createFormData.body),
        });
      } else {
        await createResource({
          spaceId,
          name: nameValue,
          description: descriptionValue,
          resourceKind: "sql",
          databaseName: createFormData.databaseConfig.databaseName.trim(),
          databaseHost: createFormData.databaseConfig.host.trim(),
          databasePort: parsedPort,
          databaseEngine: createFormData.databaseConfig.engine,
          databaseAuthScheme: createFormData.databaseConfig.authScheme,
          databaseUsername: createFormData.databaseConfig.username.trim(),
          databasePassword: createFormData.databaseConfig.password,
          useSsl: createFormData.databaseConfig.useSsl,
          enableSshTunnel: createFormData.databaseConfig.enableSshTunnel,
          connectionOptions: filterEmptyPairs(
            createFormData.databaseConfig.connectionOptions
          ),
        });
      }

      setCreateFormData(initialFormData);
      setShowCreateForm(false);
      await fetchResources();
      queryClient.invalidateQueries({ queryKey: ["resources", spaceId] });
    } catch (_err) {
      setCreateError("Failed to create resource. Please try again.");
    } finally {
      setCreating(false);
    }
  };

  const handleCancelCreate = () => {
    setShowCreateForm(false);
    setCreateError(null);
    setCreateFormData(initialFormData);
  };

  const handleEditResource = (resource: Resource) => {
    setEditingResource(resource);

    // Parse auth from headers and remove Authorization header from display
    const authConfig =
      resource.resourceKind === "http"
        ? parseAuthFromHeaders(resource.headers || [])
        : { type: "none" };
    const displayHeaders =
      resource.resourceKind === "http"
        ? removeAuthFromHeaders(resource.headers || [])
        : [];

    // Use resetFormData to set both form state and saved baseline
    // This ensures hasUnsavedChanges starts as false
    resetFormData({
      name: resource.name,
      description: resource.description,
      resourceKind: resource.resourceKind,
      baseUrl: resource.baseUrl,
      urlParameters: resource.urlParameters || [],
      headers: displayHeaders,
      body: resource.body || [],
      authConfig,
      databaseConfig:
        resource.resourceKind === "sql"
          ? resource.databaseConfig
          : emptyDatabaseConfig,
    });
  };

  const handleCloseEdit = () => {
    setEditingResource(null);
    setEditFormData(initialFormData);
  };

  const handleSaveChanges = async () => {
    await saveConfig();
  };

  const handleDiscardChanges = () => {
    discardChanges();
  };

  const createDisabled =
    creating ||
    !createFormData.name.trim() ||
    !createFormData.description.trim() ||
    (createFormData.resourceKind === "http"
      ? !createFormData.baseUrl.trim()
      : !(
          createFormData.databaseConfig.databaseName.trim() &&
          createFormData.databaseConfig.host.trim() &&
          createFormData.databaseConfig.port.trim() &&
          createFormData.databaseConfig.username.trim() &&
          createFormData.databaseConfig.password.trim()
        ));

  return (
    <section className="p-6 space-y-4 overflow-y-auto flex-1">
      <header className="flex items-center justify-between">
        <div className="flex items-center gap-2">
          {onBackClick && (
            <Button variant="ghost" size="sm" onClick={onBackClick}>
              <ChevronLeft className="h-4 w-4 mr-1" />
              {spaceName || "Space"}
            </Button>
          )}
          <h2 className="text-2xl font-semibold">Resources</h2>
        </div>
        <div className="flex gap-2">
          <PermissionButton
            spaceId={spaceId}
            permission="create_resource"
            onClick={() => setShowCreateForm(!showCreateForm)}
            variant={showCreateForm ? "secondary" : "default"}
          >
            {!showCreateForm && <Plus className="w-4 h-4 mr-2" />}
            {showCreateForm ? "Cancel" : "Create Resource"}
          </PermissionButton>
          <Button onClick={fetchResources} variant="outline">
            Refresh
          </Button>
        </div>
      </header>
      <Separator />

      {showCreateForm && (
        <Card>
          <CardHeader>
            <CardTitle>Create New Resource</CardTitle>
          </CardHeader>
          <CardContent className="space-y-4">
            {createError && (
              <div className="text-red-500 text-sm bg-red-50 p-3 rounded">
                {createError}
              </div>
            )}

            <ResourceForm
              data={createFormData}
              onChange={(newData) => {
                setCreateFormData(newData);
              }}
              mode="create"
              disabled={creating}
            />

            <div className="flex gap-2 pt-2">
              <Button onClick={handleCreateResource} disabled={createDisabled}>
                {creating ? "Creating..." : "Create Resource"}
              </Button>
              <Button
                onClick={handleCancelCreate}
                variant="outline"
                disabled={creating}
              >
                Cancel
              </Button>
            </div>
          </CardContent>
        </Card>
      )}

      {loading && (
        <Card>
          <CardContent className="py-10 text-center text-muted-foreground">
            Loading resources...
          </CardContent>
        </Card>
      )}

      {error && (
        <Card>
          <CardContent className="py-10 text-center text-red-500">
            {error}
          </CardContent>
        </Card>
      )}

      {!(loading || error) && resources.length === 0 && (
        <Card>
          <CardContent className="py-10 text-center text-muted-foreground">
            No resources found.
          </CardContent>
        </Card>
      )}

      {!(loading || error) && resources.length > 0 && (
        <>
          <div className="grid gap-4 grid-cols-1 sm:grid-cols-[repeat(auto-fit,minmax(340px,1fr))]">
            {resources.map((resource) => (
              <Card
                key={resource.id}
                className="transition-transform hover:scale-[1.01]"
              >
                <CardHeader className="flex flex-col gap-2">
                  <CardTitle className="flex items-center gap-3">
                    <div className="flex h-9 w-9 items-center justify-center rounded-md bg-gray-50 p-1">
                      <img
                        src={getResourceLogo(resource.resourceKind)}
                        alt={getResourceLogoAlt(resource.resourceKind)}
                        className="h-full w-full object-contain"
                      />
                    </div>
                    <span>{resource.name}</span>
                  </CardTitle>
                  <p className="text-sm text-muted-foreground">
                    {resource.description}
                  </p>
                  <div className="flex items-center gap-2">
                    <PermissionGate
                      spaceId={spaceId}
                      permission="edit_resource"
                      showTooltip={true}
                    >
                      <Button
                        variant="ghost"
                        size="icon"
                        className="h-8 w-8"
                        onClick={() => handleEditResource(resource)}
                      >
                        <Edit className="h-4 w-4" />
                      </Button>
                    </PermissionGate>
                    <PermissionGate
                      spaceId={spaceId}
                      permission="delete_resource"
                      showTooltip={true}
                    >
                      <TooltipProvider>
                        <Tooltip>
                          <TooltipTrigger asChild>
                            <span>
                              <Button
                                variant="ghost"
                                size="icon"
                                className="h-8 w-8"
                                onClick={() =>
                                  handleDeleteResource(resource.id)
                                }
                                disabled={
                                  !canDeleteResource(resource.id) ||
                                  deletingResourceId === resource.id
                                }
                              >
                                <Trash2
                                  className={`h-4 w-4 text-red-500 ${
                                    !canDeleteResource(resource.id) ||
                                    deletingResourceId === resource.id
                                      ? "opacity-50"
                                      : ""
                                  }`}
                                />
                              </Button>
                            </span>
                          </TooltipTrigger>
                          <TooltipContent>
                            {canDeleteResource(resource.id) ? (
                              <p>Delete resource</p>
                            ) : (
                              <p>
                                There are still apps that use this resource.
                                Please delete those first.
                              </p>
                            )}
                          </TooltipContent>
                        </Tooltip>
                      </TooltipProvider>
                    </PermissionGate>
                  </div>
                </CardHeader>
                <CardContent>
                  <p className="text-xs font-mono bg-gray-50 p-2 rounded">
                    {resource.resourceKind === "http"
                      ? resource.baseUrl
                      : `${resource.databaseConfig.host}${
                          resource.databaseConfig.port
                            ? `:${resource.databaseConfig.port}`
                            : ""
                        }/${resource.databaseConfig.databaseName}`}
                  </p>
                  <div className="mt-3">{renderUsageSummary(resource.id)}</div>
                </CardContent>
              </Card>
            ))}
          </div>
          <PaginationControls
            currentPage={currentPage}
            totalPages={totalPages}
            onPageChange={goToPage}
          />
        </>
      )}

      <Dialog open={!!editingResource} onOpenChange={() => handleCloseEdit()}>
        <DialogContent className="max-w-3xl max-h-[80vh] overflow-hidden flex flex-col">
          <DialogHeader>
            <DialogTitle>
              {canEditResource ? "Edit Resource" : "View Resource"}
            </DialogTitle>
            {!canEditResource && (
              <p className="text-sm font-normal text-muted-foreground mt-1">
                You don't have permission to edit this resource. Contact your
                team admin for access.
              </p>
            )}
          </DialogHeader>
          {editingResource && (
            <div className="space-y-4 overflow-y-auto flex-1 pr-2">
              <ResourceForm
                data={editFormData}
                onChange={setEditFormData}
                mode="edit"
                disabled={!canEditResource || saveState.saving}
              />
              {saveState.errorMessage && (
                <div className="text-red-500 text-sm bg-red-50 p-3 rounded">
                  {saveState.errorMessage}
                </div>
              )}
            </div>
          )}
          {canEditResource && (
            <DialogFooter className="mt-4">
              <Button
                variant="outline"
                onClick={handleDiscardChanges}
                disabled={!hasUnsavedChanges || saveState.saving}
              >
                Discard Changes
              </Button>
              <Button
                onClick={handleSaveChanges}
                disabled={!hasUnsavedChanges || saveState.saving}
              >
                {saveState.saving ? (
                  <>
                    <div className="animate-spin rounded-full h-4 w-4 border-b-2 border-white mr-2" />
                    Saving...
                  </>
                ) : (
                  "Save Changes"
                )}
              </Button>
            </DialogFooter>
          )}
        </DialogContent>
      </Dialog>
    </section>
  );
}
