export const DEFAULT_PAGE_SIZE = 50;

export interface PaginatedResponse<T> {
  items?: T[] | null;
  totalCount?: number;
  skip?: number;
  take?: number;
}

export async function fetchAllPages<T>(
  fetcher: (
    skip: number,
    take: number
  ) => Promise<{ data?: PaginatedResponse<T> | null; error?: unknown }>
): Promise<T[]> {
  const allItems: T[] = [];
  let skip = 0;
  let hasMore = true;

  while (hasMore) {
    const response = await fetcher(skip, DEFAULT_PAGE_SIZE);

    if (response.error || !response.data?.items) {
      break;
    }

    const pageItems = response.data.items;
    if (pageItems.length === 0) {
      break;
    }

    allItems.push(...pageItems);

    const totalCount = response.data.totalCount;
    const pageSize = response.data.take ?? pageItems.length;
    skip += pageItems.length;
    hasMore =
      totalCount !== undefined
        ? skip < totalCount
        : pageItems.length === pageSize;
  }

  return allItems;
}
