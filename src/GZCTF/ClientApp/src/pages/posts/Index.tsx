import { Button, Group, Pagination, Stack } from '@mantine/core'
import { mdiPlus } from '@mdi/js'
import { Icon } from '@mdi/react'
import { FC, useState } from 'react'
import { useTranslation } from 'react-i18next'
import { Link } from 'react-router'
import { Empty } from '@Components/Empty'
import { PostCard } from '@Components/PostCard'
import { WithNavBar } from '@Components/WithNavbar'
import { RequireRole } from '@Components/WithRole'
import { YinyuRouteLoader, YinyuSectionHead, YinyuStatusPill } from '@Components/yinyu/YinyuUI'
import { showErrorMsg } from '@Utils/Shared'
import { OnceSWRConfig } from '@Hooks/useConfig'
import { usePageTitle } from '@Hooks/usePageTitle'
import { useUserRole } from '@Hooks/useUser'
import api, { PostInfoModel, Role } from '@Api'
import misc from '@Styles/Misc.module.css'

const ITEMS_PER_PAGE = 10

const Posts: FC = () => {
  const { data: posts, mutate } = api.info.useInfoGetPosts(OnceSWRConfig)
  const [activePage, setPage] = useState(1)
  const { role } = useUserRole()
  const { t } = useTranslation()

  usePageTitle(t('post.title.index'))

  const onTogglePinned = async (post: PostInfoModel, setDisabled: (value: boolean) => void) => {
    setDisabled(true)

    try {
      const res = await api.edit.editUpdatePost(post.id, {
        isPinned: !post.isPinned,
      })
      if (post.isPinned) {
        mutate([
          ...(posts?.filter((p) => p.id !== post.id && p.isPinned) ?? []),
          { ...res.data },
          ...(posts?.filter((p) => p.id !== post.id && !p.isPinned) ?? []),
        ])
      } else {
        mutate([
          { ...res.data },
          ...(posts?.filter((p) => p.id !== post.id && p.isPinned) ?? []),
          ...(posts?.filter((p) => p.id !== post.id && !p.isPinned) ?? []),
        ])
      }
      api.info.mutateInfoGetLatestPosts()
    } catch (e) {
      showErrorMsg(e, t)
    } finally {
      setDisabled(false)
    }
  }

  return (
    <WithNavBar minWidth={0} width="var(--container)">
      <section className="yy-page-frame view-stack yy-archive-page">
        <YinyuSectionHead eyebrow="NOTICE CENTER" title={t('post.title.index')}>
          <YinyuStatusPill tone="neutral" state="open">
            {posts?.length ?? 0} 条通知
          </YinyuStatusPill>
        </YinyuSectionHead>
        <Stack gap="md">
          {!posts ? (
            <article className="state-card panel-card yy-list-loading">
              <YinyuRouteLoader title={t('post.title.index')} description="通知列表加载中" />
            </article>
          ) : posts.length > 0 ? (
            posts
              .slice((activePage - 1) * ITEMS_PER_PAGE, activePage * ITEMS_PER_PAGE)
              .map((post) => <PostCard key={post.id} post={post} onTogglePinned={onTogglePinned} />)
          ) : (
            <article className="state-card panel-card">
              <Empty description="暂无通知" />
            </article>
          )}
        </Stack>
        {(posts?.length ?? 0) > 0 && (
          <Pagination.Root
            total={Math.ceil((posts?.length ?? 0) / ITEMS_PER_PAGE)}
            siblings={3}
            value={activePage}
            onChange={setPage}
            mb="xl"
          >
            <Group gap={5} justify="flex-end">
              <Pagination.First />
              <Pagination.Previous />
              <Pagination.Items />
              <Pagination.Next />
              <Pagination.Last />
            </Group>
          </Pagination.Root>
        )}
      </section>
      {RequireRole(Role.Admin, role) && (
        <Button
          component={Link}
          className={misc.fixedButton}
          __vars={{
            '--fixed-right': 'calc(0.1 * (100vw - 70px - 2rem) + 1rem)',
            '--fixed-bottom': '6rem',
          }}
          variant="filled"
          size="md"
          leftSection={<Icon path={mdiPlus} size={1} />}
          to="/posts/new/edit"
        >
          {t('post.button.new')}
        </Button>
      )}
    </WithNavBar>
  )
}

export default Posts
