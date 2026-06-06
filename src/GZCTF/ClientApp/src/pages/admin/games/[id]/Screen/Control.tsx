import { Badge, Button, Group, SimpleGrid, Stack, Text, Title } from '@mantine/core'
import { useClipboard } from '@mantine/hooks'
import { showNotification } from '@mantine/notifications'
import { mdiCheck, mdiContentCopy, mdiOpenInNew } from '@mdi/js'
import { Icon } from '@mdi/react'
import { FC } from 'react'
import { useParams } from 'react-router'
import { WithGameEditTab } from '@Components/admin/WithGameEditTab'
import { getScreenDisplayPath, SCREEN_MODE_META } from '@Components/screen/useScreenData'
import { useAdminGame } from '@Hooks/useGame'
import { usePageTitle } from '@Hooks/usePageTitle'
import classes from '@Styles/AdminGameScreen.module.css'

const ScreenControl: FC = () => {
  const { id } = useParams()
  const numId = parseInt(id ?? '-1', 10)
  const clipboard = useClipboard()
  const { game } = useAdminGame(numId)

  usePageTitle('赛事大屏')

  const copyLink = (path: string, title: string) => {
    clipboard.copy(`${window.location.origin}${path}`)
    showNotification({
      color: 'cyan',
      message: `${title}链接已复制到剪贴板`,
      icon: <Icon path={mdiCheck} size={1} />,
    })
  }

  const openDisplay = (path: string) => {
    window.open(path, '_blank', 'noopener,noreferrer')
  }

  return (
    <WithGameEditTab
      head={
        <Group justify="space-between" w="100%" align="center">
          <div>
            <Title order={3}>赛事大屏</Title>
            <Text c="dimmed" size="sm">
              管理展示入口，主屏与副屏使用统一布局规则，显示页默认隐藏控制区
            </Text>
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
          <div>
            <Text className={classes.heroLabel}>DISPLAY CONTROL</Text>
            <Title order={2} className={classes.heroTitle}>
              {game?.title ?? '攻防实时指挥大屏'}
            </Title>
            <Text className={classes.heroDescription}>
              推荐在独立浏览器窗口中打开后进入系统全屏。展示页已移除后台容器和滚动区域，主副屏使用同一套自适应尺寸规则。
            </Text>
          </div>
          <div className={classes.heroMeta}>
            <div className={classes.heroMetaItem}>
              <span>适配重点</span>
              <strong>16:9 全屏</strong>
            </div>
            <div className={classes.heroMetaItem}>
              <span>控制区</span>
              <strong>默认隐藏</strong>
            </div>
            <div className={classes.heroMetaItem}>
              <span>滚动策略</span>
              <strong>禁止滚动</strong>
            </div>
          </div>
        </section>

        <SimpleGrid cols={{ base: 1, xl: 3 }} spacing="lg">
          {SCREEN_MODE_META.map((item) => {
            const path = getScreenDisplayPath(numId, item.mode)

            return (
              <article key={item.mode} className={classes.modeCard}>
                <div className={classes.modeHead}>
                  <div>
                    <Text className={classes.modeLabel}>{item.mode.toUpperCase()}</Text>
                    <Title order={4} className={classes.modeTitle}>
                      {item.title}
                    </Title>
                  </div>
                  <Badge variant="light" color={item.mode === 'main' ? 'cyan' : 'blue'}>
                    {item.subtitle}
                  </Badge>
                </div>

                <Text className={classes.modeDescription}>{item.description}</Text>

                <div className={classes.pathBox}>{path}</div>

                <Group grow>
                  <Button leftSection={<Icon path={mdiOpenInNew} size={0.9} />} onClick={() => openDisplay(path)}>
                    打开显示页
                  </Button>
                  <Button
                    variant="light"
                    leftSection={<Icon path={mdiContentCopy} size={0.9} />}
                    onClick={() => copyLink(path, item.title)}
                  >
                    复制链接
                  </Button>
                </Group>
              </article>
            )
          })}
        </SimpleGrid>
      </Stack>
    </WithGameEditTab>
  )
}

export default ScreenControl
