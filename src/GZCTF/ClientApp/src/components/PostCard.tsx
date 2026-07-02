import { ActionIcon, Avatar, Group, Text, Title } from '@mantine/core'
import { mdiPencilOutline, mdiPinOffOutline, mdiPinOutline } from '@mdi/js'
import { Icon } from '@mdi/react'
import dayjs from 'dayjs'
import { FC, memo, useState } from 'react'
import { useTranslation } from 'react-i18next'
import { Link, useNavigate } from 'react-router'
import { Markdown } from '@Components/MarkdownRenderer'
import { RequireRole } from '@Components/WithRole'
import { BrandMark } from '@Components/yinyu/BrandMark'
import { YinyuHexField, YinyuStatusPill } from '@Components/yinyu/YinyuUI'
import { useLanguage } from '@Utils/I18n'
import { useConfig } from '@Hooks/useConfig'
import { useUserRole } from '@Hooks/useUser'
import { PostInfoModel, Role } from '@Api'

export interface PostCardProps {
  post: PostInfoModel
  onTogglePinned?: (post: PostInfoModel, setDisabled: (value: boolean) => void) => void
}

export const PostCard: FC<PostCardProps> = memo(({ post, onTogglePinned }) => {
  const { role } = useUserRole()
  const { config } = useConfig()
  const { t } = useTranslation()
  const navigate = useNavigate()
  const [disabled, setDisabled] = useState(false)
  const { locale } = useLanguage()

  return (
    <article className="post-preview panel-card">
      <YinyuHexField cells={42} />
      <div className="quote-mark">
        <BrandMark className="post-brand-mark" src={config.logoUrl} />
      </div>
      <div>
        <Group justify="space-between" align="flex-start" wrap="nowrap">
          <Title order={3}>
            {post.isPinned ? (
              <Text span fw={900}>
                {t('post.content.pinned')}{' '}
              </Text>
            ) : null}
            {post.title}
          </Title>
          {RequireRole(Role.Admin, role) ? (
            <Group gap={4} wrap="nowrap">
              {onTogglePinned ? (
                <ActionIcon disabled={disabled} onClick={() => onTogglePinned(post, setDisabled)}>
                  {post.isPinned ? <Icon path={mdiPinOffOutline} size={1} /> : <Icon path={mdiPinOutline} size={1} />}
                </ActionIcon>
              ) : null}
              <ActionIcon component={Link} to={`/posts/${post.id}/edit`}>
                <Icon path={mdiPencilOutline} size={1} />
              </ActionIcon>
            </Group>
          ) : null}
        </Group>
        <Markdown source={post.summary} />
        {post.tags ? (
          <Group mt="sm" gap="xs">
            {post.tags.map((tag, idx) => (
              <YinyuStatusPill key={idx} tone="neutral" state="open">
                #{tag}
              </YinyuStatusPill>
            ))}
          </Group>
        ) : null}
      </div>
      <footer>
        <Avatar alt="avatar" src={post.authorAvatar} size="sm" className="avatar-dot">
          {post.authorName?.slice(0, 1) ?? 'A'}
        </Avatar>
        <strong>{post.authorName ?? 'Anonym'}</strong>
        <span>
          {t('post.content.metadata', {
            author: post.authorName ?? 'Anonym',
            date: dayjs(post.time).locale(locale).format('LLL'),
          })}
        </span>
        <button type="button" onClick={() => navigate(`/posts/${post.id}`)}>
          {t('post.content.details')} &gt;&gt;&gt;
        </button>
      </footer>
    </article>
  )
})
