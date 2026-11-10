import { Button } from '@mantine/core'

export type CursorPagination = {
  hasNextPage: boolean
  isFetchingNextPage: boolean
  fetchNextPage: () => Promise<unknown>
}

export function LoadMore({ list }: { list: CursorPagination }) {
  return list.hasNextPage ? (
    <div className="load-more">
      <Button variant="subtle" size="xs" loading={list.isFetchingNextPage} onClick={() => void list.fetchNextPage()}>
        加载更多
      </Button>
    </div>
  ) : null
}
