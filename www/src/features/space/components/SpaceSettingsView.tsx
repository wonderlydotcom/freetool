import { useQueryClient } from "@tanstack/react-query";
import {
  ChevronLeft,
  Crown,
  GlobeLock,
  Save,
  User as UserIcon,
} from "lucide-react";
import { useEffect, useState } from "react";
import { useNavigate, useParams } from "react-router-dom";
import {
  addSpaceMember,
  changeSpaceModerator,
  deleteSpace,
  getSpaceById,
  getUsers,
  inviteUser,
  removeSpaceMember,
  updateSpaceName,
} from "@/api/api";
import { PaginationControls } from "@/components/PaginationControls";
import { Badge } from "@/components/ui/badge";
import { Button } from "@/components/ui/button";
import { Card, CardContent, CardHeader, CardTitle } from "@/components/ui/card";
import { Checkbox } from "@/components/ui/checkbox";
import { Input } from "@/components/ui/input";
import { Label } from "@/components/ui/label";
import {
  Select,
  SelectContent,
  SelectItem,
  SelectTrigger,
  SelectValue,
} from "@/components/ui/select";
import { Separator } from "@/components/ui/separator";
import { Skeleton } from "@/components/ui/skeleton";
import {
  Table,
  TableBody,
  TableCell,
  TableHead,
  TableHeader,
  TableRow,
} from "@/components/ui/table";
import {
  Tooltip,
  TooltipContent,
  TooltipProvider,
  TooltipTrigger,
} from "@/components/ui/tooltip";
import { usePagination } from "@/hooks/usePagination";
import {
  useCurrentUser,
  useIsOrgAdmin,
  useIsSpaceModerator,
} from "@/hooks/usePermissions";
import { useSpaceDefaultMemberPermissions } from "@/hooks/useSpaceDefaultMemberPermissions";
import { useSpaceMembersPermissions } from "@/hooks/useSpaceMembersPermissions";
import { fetchAllPages } from "@/lib/pagination";
import { compareUsersByName } from "@/lib/utils";
import type { Permission, SpacePermissions } from "@/types/permissions";

interface SpaceSettingsViewProps {
  onBackClick?: () => void;
}

interface User {
  id: string;
  name: string;
  email: string;
  profilePicUrl?: string;
  invitedAt?: string;
  isOrgAdmin?: boolean;
}

interface Space {
  id: string;
  name: string;
  moderatorUserId: string;
  memberIds: string[];
}

const isInvitedPlaceholder = (user: User): boolean =>
  !!user.invitedAt && (!user.name || user.name === "");

/**
 * Permission groups for organizing the table columns
 */
const PERMISSION_GROUPS = [
  {
    label: "Resources",
    permissions: [
      { key: "create_resource" as Permission, label: "Create" },
      { key: "edit_resource" as Permission, label: "Edit" },
      { key: "delete_resource" as Permission, label: "Delete" },
    ],
  },
  {
    label: "Apps",
    permissions: [
      { key: "create_app" as Permission, label: "Create" },
      { key: "edit_app" as Permission, label: "Edit" },
      { key: "delete_app" as Permission, label: "Delete" },
      { key: "run_app" as Permission, label: "Run" },
    ],
  },
  {
    label: "Folders",
    permissions: [
      { key: "create_folder" as Permission, label: "Create" },
      { key: "edit_folder" as Permission, label: "Edit" },
      { key: "delete_folder" as Permission, label: "Delete" },
    ],
  },
  {
    label: "Dashboards",
    permissions: [
      { key: "create_dashboard" as Permission, label: "Create" },
      { key: "edit_dashboard" as Permission, label: "Edit" },
      { key: "delete_dashboard" as Permission, label: "Delete" },
      { key: "run_dashboard" as Permission, label: "Run" },
    ],
  },
];

export default function SpaceSettingsView({
  onBackClick,
}: SpaceSettingsViewProps) {
  const { spaceId } = useParams<{ spaceId: string }>();
  const navigate = useNavigate();
  const queryClient = useQueryClient();

  // Permission checks
  const isOrgAdmin = useIsOrgAdmin();
  const isSpaceModerator = useIsSpaceModerator(spaceId || "");
  const { currentUser } = useCurrentUser();

  // All users for member selection
  const [allUsers, setAllUsers] = useState<User[]>([]);
  const [usersLoading, setUsersLoading] = useState(true);

  // Space data
  const [space, setSpace] = useState<Space | null>(null);
  const [spaceLoading, setSpaceLoading] = useState(true);
  const [spaceError, setSpaceError] = useState<string | null>(null);
  const isCurrentUserPersistedModerator =
    !!(currentUser?.id && space?.moderatorUserId) &&
    currentUser.id === space.moderatorUserId;
  const canEdit =
    isOrgAdmin || isSpaceModerator || isCurrentUserPersistedModerator;

  // General settings form state
  const [editSpaceName, setEditSpaceName] = useState("");
  const [editModeratorId, setEditModeratorId] = useState("");
  const [editMemberIds, setEditMemberIds] = useState<string[]>([]);
  const [isSavingSettings, setIsSavingSettings] = useState(false);
  const [hasSettingsChanges, setHasSettingsChanges] = useState(false);

  // Invite by email
  const [inviteEmail, setInviteEmail] = useState("");
  const [isInviting, setIsInviting] = useState(false);

  // Permissions state
  const [pendingPermissionChanges, setPendingPermissionChanges] = useState<
    Record<string, SpacePermissions>
  >({});
  const [pendingDefaultPermissions, setPendingDefaultPermissions] =
    useState<SpacePermissions | null>(null);
  const [isSavingPermissions, setIsSavingPermissions] = useState(false);
  const [isDeletingSpace, setIsDeletingSpace] = useState(false);

  // Pagination for members permissions
  const {
    currentPage,
    pageSize,
    skip,
    totalPages,
    totalCount: membersTotalCount,
    goToPage,
    setTotalCount,
  } = usePagination();

  // Fetch members and their permissions
  const {
    members,
    spaceName: permissionsSpaceName,
    totalCount,
    isLoading: permissionsLoading,
    error: permissionsError,
    updatePermissions,
    refetch: refetchPermissions,
  } = useSpaceMembersPermissions(spaceId || "", { skip, take: pageSize });

  const {
    permissions: defaultMemberPermissions,
    isLoading: defaultPermissionsLoading,
    error: defaultPermissionsError,
    updatePermissions: updateDefaultMemberPermissions,
    refetch: refetchDefaultPermissions,
    isUpdating: isUpdatingDefaultPermissions,
  } = useSpaceDefaultMemberPermissions(spaceId || "");

  // Update pagination total count when data changes
  useEffect(() => {
    setTotalCount(totalCount);
  }, [totalCount, setTotalCount]);

  // Fetch all users
  useEffect(() => {
    const fetchUsers = async () => {
      try {
        setUsersLoading(true);
        const users = await fetchAllPages((currentSkip, currentTake) =>
          getUsers(currentSkip, currentTake)
        );
        const userData: User[] = users.map((user) => ({
          id: user.id,
          name: user.name,
          email: user.email,
          profilePicUrl: user.profilePicUrl,
          invitedAt: user.invitedAt,
          isOrgAdmin: user.isOrgAdmin,
        }));
        setAllUsers(userData);
      } finally {
        setUsersLoading(false);
      }
    };

    fetchUsers();
  }, []);

  // Fetch space data
  useEffect(() => {
    const fetchSpace = async () => {
      if (!spaceId) {
        return;
      }

      try {
        setSpaceLoading(true);
        setSpaceError(null);
        const response = await getSpaceById(spaceId);
        if (response.data) {
          const spaceData: Space = {
            id: response.data.id,
            name: response.data.name,
            moderatorUserId: response.data.moderatorUserId,
            memberIds: response.data.memberIds || [],
          };
          setSpace(spaceData);
          setEditSpaceName(spaceData.name);
          setEditModeratorId(spaceData.moderatorUserId);
          setEditMemberIds(spaceData.memberIds);
        }
      } catch (_error) {
        setSpaceError("Failed to load space");
      } finally {
        setSpaceLoading(false);
      }
    };

    fetchSpace();
  }, [spaceId]);

  // Track settings changes
  useEffect(() => {
    if (!space) {
      setHasSettingsChanges(false);
      return;
    }

    const nameChanged = editSpaceName !== space.name;
    const moderatorChanged = editModeratorId !== space.moderatorUserId;
    const membersChanged =
      JSON.stringify([...editMemberIds].sort()) !==
      JSON.stringify([...space.memberIds].sort());

    setHasSettingsChanges(nameChanged || moderatorChanged || membersChanged);
  }, [editSpaceName, editModeratorId, editMemberIds, space]);

  const handleMemberSelection = (userId: string, checked: boolean) => {
    if (userId === editModeratorId) {
      return;
    }
    if (checked) {
      setEditMemberIds([...editMemberIds, userId]);
    } else {
      setEditMemberIds(editMemberIds.filter((id) => id !== userId));
    }
  };

  const handleModeratorChange = (newModeratorId: string) => {
    const oldModeratorId = editModeratorId;
    setEditModeratorId(newModeratorId);
    // Add old moderator to members, remove new moderator from members
    setEditMemberIds((prev) => {
      const withOldModerator =
        oldModeratorId && !prev.includes(oldModeratorId)
          ? [...prev, oldModeratorId]
          : prev;
      return withOldModerator.filter((id) => id !== newModeratorId);
    });
  };

  const handleInviteUser = async () => {
    if (!(inviteEmail.trim() && spaceId)) {
      return;
    }

    try {
      setIsInviting(true);
      const response = await inviteUser({ email: inviteEmail.trim() });

      if (response.data) {
        const newUser: User = {
          id: response.data.id,
          name: response.data.name,
          email: response.data.email,
          profilePicUrl: response.data.profilePicUrl,
          invitedAt: response.data.invitedAt,
        };

        // Add to space immediately (persist to backend)
        await addSpaceMember({ spaceId, userId: newUser.id });

        // Update local state
        setAllUsers((prev) => [...prev, newUser]);
        setEditMemberIds((prev) => [...prev, newUser.id]);

        // Also update the space object to reflect the new member
        setSpace((prev) =>
          prev
            ? {
                ...prev,
                memberIds: [...prev.memberIds, newUser.id],
              }
            : null
        );

        setInviteEmail("");

        // Invalidate queries to refresh permissions table
        queryClient.invalidateQueries({ queryKey: ["spaces"] });
        refetchPermissions();
      }
    } finally {
      setIsInviting(false);
    }
  };

  const handleSaveSettings = async () => {
    if (!(space && spaceId)) {
      return;
    }

    try {
      setIsSavingSettings(true);

      // Update space name if changed
      if (editSpaceName !== space.name) {
        await updateSpaceName({ id: spaceId, name: editSpaceName.trim() });
      }

      // Change moderator if changed (org admin only)
      if (
        isOrgAdmin &&
        editModeratorId &&
        editModeratorId !== space.moderatorUserId
      ) {
        await changeSpaceModerator({
          spaceId,
          newModeratorUserId: editModeratorId,
        });
      }

      // Update members
      const membersToAdd = editMemberIds.filter(
        (id) => !space.memberIds.includes(id) && id !== editModeratorId
      );
      const membersToRemove = space.memberIds.filter(
        (id) => !editMemberIds.includes(id)
      );

      for (const userId of membersToAdd) {
        await addSpaceMember({ spaceId, userId });
      }

      for (const userId of membersToRemove) {
        await removeSpaceMember({ spaceId, userId });
      }

      // Refresh space data
      const response = await getSpaceById(spaceId);
      if (response.data) {
        const updatedSpace: Space = {
          id: response.data.id,
          name: response.data.name,
          moderatorUserId: response.data.moderatorUserId,
          memberIds: response.data.memberIds || [],
        };
        setSpace(updatedSpace);
        setEditSpaceName(updatedSpace.name);
        setEditModeratorId(updatedSpace.moderatorUserId);
        setEditMemberIds(updatedSpace.memberIds);
      }

      // Invalidate queries and refetch permissions
      queryClient.invalidateQueries({ queryKey: ["spaces"] });
      refetchPermissions();
    } finally {
      setIsSavingSettings(false);
    }
  };

  // Permission handling
  const getEffectivePermissions = (
    userId: string,
    originalPermissions: SpacePermissions
  ): SpacePermissions => {
    return pendingPermissionChanges[userId] || originalPermissions;
  };

  const handlePermissionChange = (
    userId: string,
    permissionKey: Permission,
    currentPermissions: SpacePermissions
  ) => {
    const effectivePermissions = getEffectivePermissions(
      userId,
      currentPermissions
    );
    const newValue = !effectivePermissions[permissionKey];

    setPendingPermissionChanges((prev) => ({
      ...prev,
      [userId]: {
        ...effectivePermissions,
        [permissionKey]: newValue,
      },
    }));
  };

  const handleSavePermissions = async () => {
    if (Object.keys(pendingPermissionChanges).length === 0) {
      return;
    }

    setIsSavingPermissions(true);
    try {
      for (const [userId, permissions] of Object.entries(
        pendingPermissionChanges
      )) {
        await updatePermissions(userId, permissions);
      }
      setPendingPermissionChanges({});
    } finally {
      setIsSavingPermissions(false);
    }
  };

  const getEffectiveDefaultPermissions = (): SpacePermissions | null => {
    return pendingDefaultPermissions || defaultMemberPermissions;
  };

  const handleDefaultPermissionChange = (permissionKey: Permission) => {
    const effective = getEffectiveDefaultPermissions();
    if (!effective) {
      return;
    }

    setPendingDefaultPermissions({
      ...effective,
      [permissionKey]: !effective[permissionKey],
    });
  };

  const handleSaveDefaultPermissions = async () => {
    const effective = getEffectiveDefaultPermissions();
    if (!(effective && pendingDefaultPermissions)) {
      return;
    }

    await updateDefaultMemberPermissions(effective);
    setPendingDefaultPermissions(null);
    refetchDefaultPermissions();
    refetchPermissions();
  };

  const handleDeleteSpace = async () => {
    if (!(isOrgAdmin && spaceId && space)) {
      return;
    }

    const confirmed = window.confirm(
      `Delete space "${space.name}"? This will also remove related members and OU mappings.`
    );

    if (!confirmed) {
      return;
    }

    try {
      setIsDeletingSpace(true);
      await deleteSpace(spaceId);
      await queryClient.invalidateQueries({ queryKey: ["spaces"] });
      navigate("/spaces-list");
    } finally {
      setIsDeletingSpace(false);
    }
  };

  const hasPermissionChanges = Object.keys(pendingPermissionChanges).length > 0;
  const hasDefaultPermissionChanges = !!pendingDefaultPermissions;
  const isLoading =
    spaceLoading ||
    usersLoading ||
    permissionsLoading ||
    defaultPermissionsLoading;

  if (isLoading) {
    return (
      <section className="p-6 space-y-6 overflow-y-auto flex-1">
        <header className="flex items-center gap-2">
          {onBackClick && (
            <Button variant="ghost" size="sm" onClick={onBackClick}>
              <ChevronLeft className="h-4 w-4 mr-1" />
              Back
            </Button>
          )}
          <Skeleton className="h-8 w-48" />
        </header>
        <Separator />
        <Card>
          <CardHeader>
            <Skeleton className="h-6 w-32" />
          </CardHeader>
          <CardContent className="space-y-4">
            <Skeleton className="h-10 w-full" />
            <Skeleton className="h-10 w-full" />
            <Skeleton className="h-32 w-full" />
          </CardContent>
        </Card>
        <Card>
          <CardHeader>
            <Skeleton className="h-6 w-32" />
          </CardHeader>
          <CardContent>
            <Skeleton className="h-64 w-full" />
          </CardContent>
        </Card>
      </section>
    );
  }

  if (spaceError || permissionsError || defaultPermissionsError) {
    return (
      <section className="p-6 space-y-6 overflow-y-auto flex-1">
        <header className="flex items-center gap-2">
          {onBackClick && (
            <Button variant="ghost" size="sm" onClick={onBackClick}>
              <ChevronLeft className="h-4 w-4 mr-1" />
              Back
            </Button>
          )}
          <h2 className="text-2xl font-semibold">Settings</h2>
        </header>
        <Separator />
        <Card>
          <CardContent className="py-10 text-center text-destructive">
            {spaceError ||
              permissionsError?.message ||
              defaultPermissionsError?.message ||
              "Failed to load data"}
          </CardContent>
        </Card>
      </section>
    );
  }

  const spaceName = space?.name || permissionsSpaceName || "Space";

  return (
    <section className="p-6 space-y-6 overflow-y-auto flex-1">
      <header className="flex items-center justify-between">
        <div className="flex items-center gap-2">
          {onBackClick && (
            <Button variant="ghost" size="sm" onClick={onBackClick}>
              <ChevronLeft className="h-4 w-4 mr-1" />
              Back
            </Button>
          )}
          <h2 className="text-2xl font-semibold">{spaceName} Settings</h2>
        </div>
      </header>
      <Separator />

      {/* General Settings Section */}
      <Card>
        <CardHeader className="flex flex-row items-center justify-between">
          <CardTitle className="text-lg">General Settings</CardTitle>
          {canEdit && hasSettingsChanges && (
            <Button
              onClick={handleSaveSettings}
              disabled={isSavingSettings}
              size="sm"
            >
              <Save className="h-4 w-4 mr-2" />
              {isSavingSettings ? "Saving..." : "Save Settings"}
            </Button>
          )}
        </CardHeader>
        <CardContent className="space-y-6">
          {/* Space Name */}
          <div className="space-y-2">
            <Label htmlFor="space-name">Space Name</Label>
            <Input
              id="space-name"
              value={editSpaceName}
              onChange={(e) => setEditSpaceName(e.target.value)}
              disabled={!canEdit}
            />
          </div>

          {/* Moderator Selection (org admin only) */}
          {isOrgAdmin ? (
            <div className="space-y-2">
              <Label htmlFor="moderator-select">Moderator</Label>
              <Select
                value={editModeratorId}
                onValueChange={handleModeratorChange}
              >
                <SelectTrigger id="moderator-select">
                  <SelectValue placeholder="Select a moderator" />
                </SelectTrigger>
                <SelectContent>
                  {allUsers
                    .filter(
                      (user) =>
                        editMemberIds.includes(user.id) ||
                        user.id === editModeratorId
                    )
                    .map((user) => (
                      <SelectItem key={user.id} value={user.id}>
                        <div className="flex items-center gap-2">
                          <Crown className="h-3 w-3 text-amber-500" />
                          {isInvitedPlaceholder(user) ? user.email : user.name}
                        </div>
                      </SelectItem>
                    ))}
                </SelectContent>
              </Select>
              <p className="text-xs text-muted-foreground">
                The moderator has full control over the space
              </p>
            </div>
          ) : (
            <div className="space-y-2">
              <Label>Moderator</Label>
              <div className="flex items-center gap-2 p-2 border rounded-md bg-muted/50">
                <Crown className="h-4 w-4 text-amber-500" />
                <span>
                  {allUsers.find((u) => u.id === editModeratorId)?.name ||
                    "Unknown"}
                </span>
              </div>
              <p className="text-xs text-muted-foreground">
                Only org admins can change the moderator
              </p>
            </div>
          )}

          {/* Members */}
          <div className="space-y-3">
            <Label>Members</Label>
            <div className="space-y-2 max-h-48 overflow-y-auto border rounded-md p-3">
              {allUsers
                .filter((user) => user.id !== editModeratorId)
                .sort(compareUsersByName)
                .map((user) => (
                  <div key={user.id} className="flex items-center space-x-3">
                    <Checkbox
                      id={`member-${user.id}`}
                      checked={editMemberIds.includes(user.id)}
                      onCheckedChange={(checked) =>
                        handleMemberSelection(user.id, checked as boolean)
                      }
                      disabled={!canEdit}
                    />
                    <div className="flex items-center space-x-2 flex-1">
                      {user.profilePicUrl ? (
                        <img
                          src={user.profilePicUrl}
                          alt={`${user.name}'s profile`}
                          className="w-6 h-6 rounded-full object-cover"
                        />
                      ) : (
                        <div className="w-6 h-6 rounded-full bg-muted flex items-center justify-center">
                          <UserIcon
                            size={12}
                            className="text-muted-foreground"
                          />
                        </div>
                      )}
                      <Label
                        htmlFor={`member-${user.id}`}
                        className="text-sm font-medium leading-none cursor-pointer"
                      >
                        {isInvitedPlaceholder(user) ? (
                          <span className="flex items-center gap-2">
                            <span className="text-muted-foreground">
                              {user.email}
                            </span>
                            <Badge variant="secondary" className="text-xs">
                              Pending
                            </Badge>
                          </span>
                        ) : (
                          user.name
                        )}
                      </Label>
                    </div>
                  </div>
                ))}
            </div>
            <p className="text-xs text-muted-foreground">
              {editMemberIds.length} member
              {editMemberIds.length !== 1 ? "s" : ""} selected
            </p>
          </div>

          {/* Invite by Email */}
          {canEdit && (
            <div className="space-y-2 pt-4 border-t">
              <Label>Invite by Email</Label>
              <div className="flex gap-2">
                <Input
                  type="email"
                  placeholder="Enter email address"
                  value={inviteEmail}
                  onChange={(e) => setInviteEmail(e.target.value)}
                  onKeyDown={(e) => {
                    if (e.key === "Enter") {
                      e.preventDefault();
                      handleInviteUser();
                    }
                  }}
                />
                <Button
                  type="button"
                  variant="outline"
                  onClick={handleInviteUser}
                  disabled={!inviteEmail.trim() || isInviting}
                >
                  {isInviting ? "Inviting..." : "Invite"}
                </Button>
              </div>
              <p className="text-xs text-muted-foreground">
                Invited users will be added once they log in
              </p>
            </div>
          )}
        </CardContent>
      </Card>

      {/* Default Permissions Section */}
      <Card>
        <CardHeader className="flex flex-row items-center justify-between">
          <div>
            <CardTitle className="text-lg">
              Default Member Permissions
            </CardTitle>
            <p className="text-sm text-muted-foreground mt-1">
              Controls baseline access for non-moderator members, including
              users auto-added via OU mapping.
            </p>
          </div>
          {canEdit && hasDefaultPermissionChanges && (
            <Button
              onClick={handleSaveDefaultPermissions}
              disabled={isUpdatingDefaultPermissions}
              size="sm"
            >
              <Save className="h-4 w-4 mr-2" />
              {isUpdatingDefaultPermissions ? "Saving..." : "Save Defaults"}
            </Button>
          )}
        </CardHeader>
        <CardContent>
          {getEffectiveDefaultPermissions() ? (
            <div className="overflow-x-auto">
              <Table>
                <TableHeader>
                  <TableRow className="border-b-0">
                    {PERMISSION_GROUPS.map((group) => (
                      <TableHead
                        key={group.label}
                        colSpan={group.permissions.length}
                        className="text-center border-l first:border-l-0"
                      >
                        {group.label}
                      </TableHead>
                    ))}
                  </TableRow>
                  <TableRow>
                    {PERMISSION_GROUPS.map((group) =>
                      group.permissions.map((perm, idx) => (
                        <TableHead
                          key={perm.key}
                          className={`text-center text-xs ${
                            idx === 0 ? "border-l first:border-l-0" : ""
                          }`}
                        >
                          {perm.label}
                        </TableHead>
                      ))
                    )}
                  </TableRow>
                </TableHeader>
                <TableBody>
                  <TableRow>
                    {PERMISSION_GROUPS.map((group) =>
                      group.permissions.map((perm, idx) => (
                        <TableCell
                          key={perm.key}
                          className={`text-center ${idx === 0 ? "border-l first:border-l-0" : ""}`}
                        >
                          <div className="flex justify-center">
                            <Checkbox
                              checked={
                                getEffectiveDefaultPermissions()?.[perm.key] ??
                                false
                              }
                              disabled={!canEdit}
                              onCheckedChange={() =>
                                handleDefaultPermissionChange(perm.key)
                              }
                              aria-label={`${perm.label} default ${group.label.toLowerCase()} permission`}
                            />
                          </div>
                        </TableCell>
                      ))
                    )}
                  </TableRow>
                </TableBody>
              </Table>
            </div>
          ) : (
            <div className="py-6 text-sm text-muted-foreground">
              Default member permissions are unavailable for this space.
            </div>
          )}
          {!canEdit && (
            <p className="text-sm text-muted-foreground mt-4">
              Only moderators and organization administrators can edit default
              permissions.
            </p>
          )}
        </CardContent>
      </Card>

      {/* Permissions Section */}
      <Card>
        <CardHeader className="flex flex-row items-center justify-between">
          <div>
            <CardTitle className="text-lg flex items-center gap-2">
              Member Permissions
              {membersTotalCount > 0 && (
                <Badge variant="secondary" className="ml-2">
                  {membersTotalCount}
                </Badge>
              )}
            </CardTitle>
            {!canEdit && (
              <p className="text-sm text-muted-foreground mt-1">
                You can view permissions but only moderators and administrators
                can edit them.
              </p>
            )}
          </div>
          {canEdit && hasPermissionChanges && (
            <Button
              onClick={handleSavePermissions}
              disabled={isSavingPermissions}
              size="sm"
            >
              <Save className="h-4 w-4 mr-2" />
              {isSavingPermissions ? "Saving..." : "Save Permissions"}
            </Button>
          )}
        </CardHeader>
        <CardContent>
          {membersTotalCount === 0 ? (
            <div className="py-10 text-center text-muted-foreground">
              No members found in this space.
            </div>
          ) : (
            <>
              <div className="overflow-x-auto">
                <Table>
                  <TableHeader>
                    {/* Group headers row */}
                    <TableRow className="border-b-0">
                      <TableHead
                        className="sticky top-0 left-0 bg-background z-40 w-48"
                        rowSpan={2}
                      >
                        Member
                      </TableHead>
                      {PERMISSION_GROUPS.map((group) => (
                        <TableHead
                          key={group.label}
                          colSpan={group.permissions.length}
                          className="sticky top-0 z-30 bg-background text-center border-l"
                        >
                          {group.label}
                        </TableHead>
                      ))}
                    </TableRow>
                    {/* Permission sub-headers row */}
                    <TableRow>
                      {PERMISSION_GROUPS.map((group) =>
                        group.permissions.map((perm, idx) => (
                          <TableHead
                            key={perm.key}
                            className={`sticky top-10 z-20 bg-background text-center text-xs ${
                              idx === 0 ? "border-l" : ""
                            }`}
                          >
                            {perm.label}
                          </TableHead>
                        ))
                      )}
                    </TableRow>
                  </TableHeader>
                  <TableBody>
                    {[...members].sort(compareUsersByName).map((member) => {
                      const effectivePermissions = getEffectivePermissions(
                        member.userId,
                        member.permissions
                      );
                      const hasChanges =
                        !!pendingPermissionChanges[member.userId];
                      const isReadOnlyRow =
                        member.isModerator || member.isOrgAdmin;
                      const rowClassName = [
                        hasChanges ? "bg-yellow-50" : "",
                        isReadOnlyRow
                          ? "bg-muted/40 text-muted-foreground"
                          : "",
                      ]
                        .filter(Boolean)
                        .join(" ");
                      const stickyCellClassName = [
                        "sticky left-0 z-10 font-medium",
                        isReadOnlyRow ? "bg-muted/40" : "bg-background",
                      ]
                        .filter(Boolean)
                        .join(" ");

                      return (
                        <TableRow key={member.userId} className={rowClassName}>
                          {/* Member name cell - sticky */}
                          <TableCell className={stickyCellClassName}>
                            <div className="flex items-center gap-2">
                              {member.profilePicUrl ? (
                                <img
                                  src={member.profilePicUrl}
                                  alt={member.userName}
                                  className="w-8 h-8 rounded-full object-cover"
                                />
                              ) : (
                                <div className="w-8 h-8 rounded-full bg-muted flex items-center justify-center">
                                  <UserIcon
                                    size={16}
                                    className="text-muted-foreground"
                                  />
                                </div>
                              )}
                              <div className="flex flex-col">
                                <span className="flex items-center gap-1">
                                  {member.userName}
                                  {member.isModerator && (
                                    <TooltipProvider>
                                      <Tooltip>
                                        <TooltipTrigger>
                                          <Crown className="h-4 w-4 text-amber-500" />
                                        </TooltipTrigger>
                                        <TooltipContent>
                                          <p>Space Moderator</p>
                                        </TooltipContent>
                                      </Tooltip>
                                    </TooltipProvider>
                                  )}
                                  {member.isOrgAdmin && (
                                    <TooltipProvider>
                                      <Tooltip>
                                        <TooltipTrigger>
                                          <GlobeLock className="h-4 w-4 text-blue-500" />
                                        </TooltipTrigger>
                                        <TooltipContent>
                                          <p>Organization Admin</p>
                                        </TooltipContent>
                                      </Tooltip>
                                    </TooltipProvider>
                                  )}
                                </span>
                                <span className="text-xs text-muted-foreground">
                                  {member.userEmail}
                                </span>
                              </div>
                              {hasChanges && (
                                <Badge
                                  variant="outline"
                                  className="ml-2 text-xs"
                                >
                                  Unsaved
                                </Badge>
                              )}
                            </div>
                          </TableCell>

                          {/* Permission checkboxes */}
                          {PERMISSION_GROUPS.map((group) =>
                            group.permissions.map((perm, idx) => {
                              const isChecked = isReadOnlyRow
                                ? true
                                : effectivePermissions[perm.key];
                              const isDisabled = !canEdit || isReadOnlyRow;

                              return (
                                <TableCell
                                  key={perm.key}
                                  className={`text-center ${
                                    idx === 0 ? "border-l" : ""
                                  }`}
                                >
                                  <div className="flex justify-center">
                                    <Checkbox
                                      checked={isChecked}
                                      disabled={isDisabled}
                                      onCheckedChange={() =>
                                        handlePermissionChange(
                                          member.userId,
                                          perm.key,
                                          member.permissions
                                        )
                                      }
                                      aria-label={`${perm.label} ${group.label.toLowerCase()} permission for ${member.userName}`}
                                    />
                                  </div>
                                </TableCell>
                              );
                            })
                          )}
                        </TableRow>
                      );
                    })}
                  </TableBody>
                </Table>
              </div>

              {/* Legend */}
              <div className="mt-4 pt-4 border-t flex flex-wrap gap-4 text-sm text-muted-foreground">
                <div className="flex items-center gap-2">
                  <Crown className="h-4 w-4 text-amber-500" />
                  <span>Moderator (all permissions, cannot be changed)</span>
                </div>
                <div className="flex items-center gap-2">
                  <GlobeLock className="h-4 w-4 text-blue-500" />
                  <span>Organization Admin</span>
                </div>
                {hasPermissionChanges && (
                  <div className="flex items-center gap-2">
                    <Badge variant="outline" className="text-xs">
                      Unsaved
                    </Badge>
                    <span>Has pending changes</span>
                  </div>
                )}
              </div>

              {/* Pagination */}
              <PaginationControls
                currentPage={currentPage}
                totalPages={totalPages}
                onPageChange={goToPage}
              />
            </>
          )}
        </CardContent>
      </Card>

      {/* Danger Zone */}
      {isOrgAdmin && (
        <Card className="border-destructive/40">
          <CardHeader>
            <CardTitle className="text-lg text-destructive">
              Danger Zone
            </CardTitle>
          </CardHeader>
          <CardContent className="space-y-3">
            <p className="text-sm text-muted-foreground">
              Deleting a space is permanent and removes related access mappings.
            </p>
            <Button
              variant="destructive"
              onClick={handleDeleteSpace}
              disabled={isDeletingSpace}
            >
              {isDeletingSpace ? "Deleting Space..." : "Delete Space"}
            </Button>
          </CardContent>
        </Card>
      )}
    </section>
  );
}
