import {
  Avatar,
  Button,
  Group,
  Stack,
  Text,
  Title,
} from '@mantine/core'
import { mdiPencilOutline } from '@mdi/js'
import { Icon } from '@mdi/react'
import dayjs from 'dayjs'
import { FC, useEffect } from 'react'
import { useTranslation } from 'react-i18next'
import { Link, useNavigate, useParams } from 'react-router'
import { Markdown } from '@Components/MarkdownRenderer'
import { WithNavBar } from '@Components/WithNavbar'
import { RequireRole } from '@Components/WithRole'
import { BrandMark } from '@Components/yinyu/BrandMark'
import { YinyuHexField, YinyuStatusPill } from '@Components/yinyu/YinyuUI'
import { useLanguage } from '@Utils/I18n'
import { useConfig } from '@Hooks/useConfig'
import { usePageTitle } from '@Hooks/usePageTitle'
import { useUserRole } from '@Hooks/useUser'
import api, { Role } from '@Api'

const Post: FC = () => {
  const { postId } = useParams()
  const navigate = useNavigate()

  const { t } = useTranslation()

  useEffect(() => {
    if (postId?.length !== 8) {
      navigate('/404')
      return
    }
  }, [postId, navigate])

  const { data: post } = api.info.useInfoGetPost(
    postId ?? '',
    {
      refreshInterval: 0,
      revalidateOnFocus: false,
    },
    postId?.length === 8
  )

  const { role } = useUserRole()
  const { locale } = useLanguage()
  const { config } = useConfig()

  usePageTitle(post?.title ?? '通知')

  return (
    <WithNavBar width="var(--container)" isLoading={!post} minWidth={0}>
      <section className="yy-page-frame yy-post-detail-page">
        <header className="panel-card yy-post-hero">
          <YinyuHexField cells={54} />
          <BrandMark className="yy-post-hero-mark" src={config.logoUrl} />
          <Stack gap="sm" className="yy-post-hero-copy">
            <span className="yy-section-kicker">PLATFORM NOTICE</span>
            <Title order={1}>{post?.title}</Title>
            <Group gap="sm" className="yy-post-meta">
              <Avatar alt="avatar" src={post?.authorAvatar} size="md" className="avatar-dot">
                {post?.authorName?.slice(0, 1) ?? 'A'}
              </Avatar>
              <Text fw={800}>{post?.authorName ?? 'Anonym'}</Text>
              <Text>{dayjs(post?.time).locale(locale).format('LLL')}</Text>
            </Group>
          </Stack>
          {RequireRole(Role.Admin, role) && (
            <Button
              component={Link}
              variant="filled"
              size="md"
              leftSection={<Icon path={mdiPencilOutline} size={1} />}
              to={`/posts/${postId}/edit`}
              className="yy-post-edit-button"
            >
              {t('post.button.edit')}
            </Button>
          )}
        </header>
        <article className="panel-card yy-post-panel yy-post-detail-content">
          <YinyuHexField cells={48} />
          <Markdown source={post?.content ?? ''} />
          {post?.tags && post.tags.length > 0 && (
            <Group justify="right" mt="lg">
              {post.tags.map((tag, idx) => (
                <YinyuStatusPill key={idx} tone="neutral" state="open">
                  #{tag}
                </YinyuStatusPill>
              ))}
            </Group>
          )}
          <Group gap={5} my="lg" justify="right">
            <Avatar alt="avatar" src={post?.authorAvatar} size="sm">
              {post?.authorName?.slice(0, 1) ?? 'A'}
            </Avatar>
            <Text fw="bold">
              {t('post.content.metadata', {
                author: post?.authorName ?? 'Anonym',
                date: dayjs(post?.time).locale(locale).format('LLL'),
              })}
            </Text>
          </Group>
        </article>
      </section>
    </WithNavBar>
  )
}

export default Post
