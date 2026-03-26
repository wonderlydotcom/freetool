import { useQuery } from "@tanstack/react-query";
import { getSpaceById, getSpaces, getUsers } from "@/api/api";
import { fetchAllPages } from "@/lib/pagination";
import type { Space, SpaceUser, SpaceWithDetails } from "@/types/space";

interface SpaceListResponse {
  items?: Space[];
  total?: number;
}

interface SpacesQueryResult {
  spaces: SpaceWithDetails[];
  total: number;
}

/**
 * Hook to fetch all spaces the current user has access to
 *
 * @returns Space list data with enriched user details, loading state, and error state
 */
export function useSpaces() {
  const { data, isLoading, error, refetch } = useQuery<SpacesQueryResult>({
    queryKey: ["spaces"],
    queryFn: async () => {
      const result = await getSpaces();

      if (result.error || !result.data) {
        throw new Error(result.error?.message || "Failed to fetch spaces");
      }

      const responseData = result.data as SpaceListResponse | Space[];
      const spaceItems: Space[] = Array.isArray(responseData)
        ? responseData
        : responseData.items || [];

      // Fetch all users to enrich space data
      const usersMap = new Map<string, SpaceUser>();

      const users = await fetchAllPages((skip, take) => getUsers(skip, take));

      for (const user of users) {
        usersMap.set(user.id, {
          id: user.id,
          name: user.name,
          email: user.email,
          profilePicUrl: user.profilePicUrl,
        });
      }

      // Enrich spaces with user details
      const spaces: SpaceWithDetails[] = spaceItems.map((space) => {
        const moderator = usersMap.get(space.moderatorUserId) || {
          id: space.moderatorUserId,
          name: "Unknown User",
          email: "",
        };

        const members: SpaceUser[] = space.memberIds
          .map((memberId) => usersMap.get(memberId))
          .filter((user): user is SpaceUser => user !== undefined);

        return {
          ...space,
          moderator,
          members,
        };
      });

      const total = Array.isArray(responseData)
        ? spaces.length
        : (responseData.total ?? spaces.length);

      return {
        spaces,
        total,
      };
    },
    staleTime: 5 * 60 * 1000, // 5 minutes
  });

  return {
    spaces: data?.spaces || [],
    total: data?.total || 0,
    isLoading,
    error,
    refetch,
  };
}

/**
 * Hook to fetch a single space by ID
 *
 * @param spaceId - The ID of the space to fetch
 * @returns Space data with enriched user details, loading state, and error state
 */
export function useSpace(spaceId: string | undefined) {
  const { data, isLoading, error, refetch } = useQuery({
    queryKey: ["space", spaceId],
    queryFn: async () => {
      if (!spaceId) {
        return null;
      }

      const result = await getSpaceById(spaceId);

      if (result.error || !result.data) {
        throw new Error(result.error?.message || "Failed to fetch space");
      }

      const space = result.data as Space;

      // Fetch users for enrichment
      const usersMap = new Map<string, SpaceUser>();

      const users = await fetchAllPages((skip, take) => getUsers(skip, take));

      for (const user of users) {
        usersMap.set(user.id, {
          id: user.id,
          name: user.name,
          email: user.email,
          profilePicUrl: user.profilePicUrl,
        });
      }

      const moderator = usersMap.get(space.moderatorUserId) || {
        id: space.moderatorUserId,
        name: "Unknown User",
        email: "",
      };

      const members: SpaceUser[] = space.memberIds
        .map((memberId) => usersMap.get(memberId))
        .filter((user): user is SpaceUser => user !== undefined);

      return {
        ...space,
        moderator,
        members,
      } as SpaceWithDetails;
    },
    enabled: !!spaceId,
    staleTime: 5 * 60 * 1000, // 5 minutes
  });

  return {
    space: data || null,
    isLoading,
    error,
    refetch,
  };
}
