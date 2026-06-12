import { Badge, Button, Group, Stack, Text, Title } from '@mantine/core'
import { useClipboard } from '@mantine/hooks'
import { showNotification } from '@mantine/notifications'
import { mdiCheck, mdiContentCopy, mdiOpenInNew } from '@mdi/js'
import { Icon } from '@mdi/react'
import { FC } from 'react'
import { useParams } from 'react-router'
import { WithGameEditTab } from '@Components/admin/WithGameEditTab'
import { BrandMark } from '@Components/yinyu/BrandMark'
import { useAdminGame } from '@Hooks/useGame'
import { usePageTitle } from '@Hooks/usePageTitle'
import classes from '@Styles/AdminGameScreen.module.css'

const ScreenControl: FC = () => {
  const { id } = useParams()
  const numId = parseInt(id ?? '-1', 10)
  const clipboard = useClipboard()
  const { game } = useAdminGame(numId)
  const displayPath = `/admin/games/${numId}/screen`

  usePageTitle('赛事大屏')

  const copyLink = () => {
    clipboard.copy(`${window.location.origin}${displayPath}`)
    showNotification({
      color: 'green',
      message: '主显示屏链接已复制到剪贴板',
      icon: <Icon path={mdiCheck} size={1} />,
    })
  }

  const openDisplay = () => {
    window.open(displayPath, '_blank', 'noopener,noreferrer')
  }

  return (
    <WithGameEditTab
      head={
        <Group justify="space-between" w="100%" align="center" className={classes.pageHead}>
          <div>
            <Title order={3}>赛事大屏</Title>
            <Text size="sm">打开独立展示窗口，实时呈现排行、分数城市与解题动态。</Text>
          </div>
          {game?.isTest && (
            <Badge color="orange" variant="filled">
              演示模式
            </Badge>
          )}
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
              主显示屏采用 16:9 全屏展示逻辑，聚合比赛排行、3D 分数城市和实时解题日志。建议在独立浏览器窗口中打开后进入系统全屏。
            </Text>
          </div>

          <div className={classes.heroMeta}>
            <div className={classes.heroMetaItem}>
              <span>展示模式</span>
              <strong>单主屏</strong>
            </div>
            <div className={classes.heroMetaItem}>
              <span>视觉风格</span>
              <strong>银色金属态势</strong>
            </div>
            <div className={classes.heroMetaItem}>
              <span>数据来源</span>
              <strong>实时记分榜</strong>
            </div>
          </div>
        </section>

        <section className={classes.displayCard}>
          <div className={classes.displayCardMain}>
            <Text className={classes.modeLabel}>MAIN SCREEN</Text>
            <Title order={3} className={classes.modeTitle}>
              主显示屏
            </Title>
            <Text className={classes.modeDescription}>
              一个链接完成大屏展示，左侧排行、中央 3D 分数城市、右侧实时解题日志会随比赛数据自动更新。
            </Text>
            <div className={classes.pathBox}>{displayPath}</div>
          </div>

          <Group className={classes.actionGroup}>
            <Button leftSection={<Icon path={mdiOpenInNew} size={0.9} />} onClick={openDisplay}>
              打开主显示屏
            </Button>
            <Button variant="light" leftSection={<Icon path={mdiContentCopy} size={0.9} />} onClick={copyLink}>
              复制链接
            </Button>
          </Group>
        </section>
      </Stack>
    </WithGameEditTab>
  )
}

export default ScreenControl
