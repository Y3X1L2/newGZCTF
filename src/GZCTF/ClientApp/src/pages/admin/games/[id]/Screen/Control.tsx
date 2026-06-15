import { Button, Group, Select, SimpleGrid, Stack, Text, Title } from '@mantine/core'
import { useClipboard } from '@mantine/hooks'
import { showNotification } from '@mantine/notifications'
import { mdiCheck, mdiContentCopy, mdiOpenInNew, mdiPresentationPlay, mdiSwapHorizontal } from '@mdi/js'
import { Icon } from '@mdi/react'
import { FC, useMemo } from 'react'
import { useNavigate, useParams } from 'react-router'
import { WithGameEditTab } from '@Components/admin/WithGameEditTab'
import { BrandMark } from '@Components/yinyu/BrandMark'
import { useAdminGame } from '@Hooks/useGame'
import { usePageTitle } from '@Hooks/usePageTitle'
import { OnceSWRConfig } from '@Hooks/useConfig'
import api from '@Api'
import classes from '@Styles/AdminGameScreen.module.css'

interface DisplayModeCardProps {
  eyebrow: string
  title: string
  description: string
  path: string
  actionLabel: string
  onOpen: () => void
  onCopy: () => void
}

const DisplayModeCard: FC<DisplayModeCardProps> = ({
  eyebrow,
  title,
  description,
  path,
  actionLabel,
  onOpen,
  onCopy,
}) => (
  <section className={classes.displayCard}>
    <div className={classes.displayCardMain}>
      <Text className={classes.modeLabel}>{eyebrow}</Text>
      <Title order={3} className={classes.modeTitle}>
        {title}
      </Title>
      <Text className={classes.modeDescription}>{description}</Text>
      <div className={classes.pathBox}>{path}</div>
    </div>

    <Group className={classes.actionGroup}>
      <Button leftSection={<Icon path={mdiOpenInNew} size={0.9} />} onClick={onOpen}>
        {actionLabel}
      </Button>
      <Button variant="light" leftSection={<Icon path={mdiContentCopy} size={0.9} />} onClick={onCopy}>
        复制链接
      </Button>
    </Group>
  </section>
)

const ScreenControl: FC = () => {
  const { id } = useParams()
  const numId = parseInt(id ?? '-1', 10)
  const clipboard = useClipboard()
  const navigate = useNavigate()
  const { game } = useAdminGame(numId)
  const { data: games } = api.edit.useEditGetGames({ count: 100, skip: 0 }, OnceSWRConfig)
  const displayPath = `/admin/games/${numId}/screen`
  const demoPath = `/admin/games/${numId}/screen/demo`

  usePageTitle('赛事态势大屏')

  const gameOptions = useMemo(
    () =>
      (games?.data ?? []).map((item) => ({
        value: String(item.id),
        label: item.title ?? `赛事 #${item.id}`,
      })),
    [games?.data]
  )

  const copyLink = (path: string, label: string) => {
    clipboard.copy(`${window.location.origin}${path}`)
    showNotification({
      color: 'green',
      message: `${label}链接已复制到剪贴板`,
      icon: <Icon path={mdiCheck} size={1} />,
    })
  }

  const openDisplay = (path: string) => {
    window.open(path, '_blank', 'noopener,noreferrer')
  }

  return (
    <WithGameEditTab
      head={
        <Group justify="space-between" w="100%" align="center" className={classes.pageHead}>
          <div>
            <Title order={3}>赛事态势大屏</Title>
            <Text size="sm">面向现场终端的正式比赛大屏与演示大屏。</Text>
          </div>
          <Select
            className={classes.gameSelect}
            data={gameOptions}
            leftSection={<Icon path={mdiSwapHorizontal} size={0.85} />}
            placeholder="切换比赛"
            value={Number.isFinite(numId) ? String(numId) : null}
            searchable
            onChange={(value) => {
              if (!value) return
              navigate(`/admin/games/${value}/screen/control`)
            }}
          />
        </Group>
      }
    >
      <Stack gap="lg" className={classes.controlRoot}>
        <section className={classes.hero}>
          <div className={classes.heroVisual}>
            <BrandMark className={classes.heroMark} />
            <div className={classes.heroSignal} />
          </div>

          <div className={classes.heroCopy}>
            <Text className={classes.heroLabel}>DISPLAY CONTROL</Text>
            <Title order={2} className={classes.heroTitle}>
              {game?.title ?? '安全综合演练大屏'}
            </Title>
            <Text className={classes.heroDescription}>
              正式入口用于现场主屏展示当前比赛数据；演示入口用于投屏、录制和设备联调，不写入任何赛事记录。
            </Text>
          </div>

          <div className={classes.heroMeta}>
            <div className={classes.heroMetaItem}>
              <span>当前比赛</span>
              <strong>{game?.title ?? `赛事 #${numId}`}</strong>
            </div>
            <div className={classes.heroMetaItem}>
              <span>正式入口</span>
              <strong>实时赛事数据</strong>
            </div>
            <div className={classes.heroMetaItem}>
              <span>使用建议</span>
              <strong>独立窗口全屏</strong>
            </div>
          </div>
        </section>

        <SimpleGrid cols={{ base: 1, lg: 2 }} spacing="lg">
          <DisplayModeCard
            eyebrow="OFFICIAL SCREEN"
            title="正式比赛入口"
            description="读取当前赛事计分榜、解题提交和时间状态，用于现场主屏展示。"
            path={displayPath}
            actionLabel="打开正式大屏"
            onOpen={() => openDisplay(displayPath)}
            onCopy={() => copyLink(displayPath, '正式大屏')}
          />
          <DisplayModeCard
            eyebrow="DEMO SCREEN"
            title="演示大屏入口"
            description="使用动态演练数据，便于现场调试屏幕、投影和录制效果。"
            path={demoPath}
            actionLabel="打开演示大屏"
            onOpen={() => openDisplay(demoPath)}
            onCopy={() => copyLink(demoPath, '演示大屏')}
          />
        </SimpleGrid>

        <section className={classes.noteCard}>
          <Icon path={mdiPresentationPlay} size={1.15} />
          <Text>
            建议在独立浏览器窗口打开后进入系统全屏。正式入口支持切换比赛；演示入口不写入任何赛事数据，仅用于展示验证。
          </Text>
        </section>
      </Stack>
    </WithGameEditTab>
  )
}

export default ScreenControl
