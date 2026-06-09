import { Badge, Center, Group, SimpleGrid, Stack, Text, Title } from '@mantine/core'
import { mdiChartTimelineVariant, mdiServerNetwork, mdiShieldCheckOutline, mdiTrophyOutline } from '@mdi/js'
import { Icon } from '@mdi/react'
import { FC } from 'react'
import { useTranslation } from 'react-i18next'
import { WithNavBar } from '@Components/WithNavbar'
import { MainIcon } from '@Components/icon/MainIcon'
import { PLATFORM_BRAND, PLATFORM_DESCRIPTION, PLATFORM_SLOGAN } from '@Utils/Brand'
import { useIsMobile } from '@Utils/ThemeOverride'
import { usePageTitle } from '@Hooks/usePageTitle'
import classes from '@Styles/About.module.css'

const features = [
  {
    icon: mdiTrophyOutline,
    title: '赛事管理',
    description: '覆盖报名、分组、题目、公告、排行榜与赛后数据归档。',
  },
  {
    icon: mdiShieldCheckOutline,
    title: '攻防演练',
    description: '支持 AWDP 流程、服务实例、攻击提交、补丁验证与轮次计分。',
  },
  {
    icon: mdiServerNetwork,
    title: '分布式靶场',
    description: '统一管理节点、镜像模板、虚拟机实例与远程调度状态。',
  },
  {
    icon: mdiChartTimelineVariant,
    title: '实时观测',
    description: '面向管理员与选手提供清晰的状态、日志、指标和比赛态势。',
  },
]

const About: FC = () => {
  const { t } = useTranslation()
  const isMobile = useIsMobile()

  usePageTitle(t('common.title.about'))

  return (
    <WithNavBar minWidth={0}>
      <Stack justify="center" align="center" gap="xl" className={classes.container} data-mobile={isMobile || undefined}>
        <Center>
          <Stack align="center" gap="xs">
            <MainIcon size={isMobile ? '4rem' : '5rem'} className={classes.mainIcon} />
            <Title order={1} fw={800} ta="center" className={classes.mainTitle}>
              {PLATFORM_BRAND}
            </Title>
            <Text size={isMobile ? 'md' : 'xl'} fw={500} ta="center" c="dimmed" className={classes.slogan}>
              {PLATFORM_SLOGAN}
            </Text>
            <Badge size="lg" variant="light" mt="sm">
              CTF / AWDP / Theory / Fleet
            </Badge>
          </Stack>
        </Center>

        <Text size="lg" c="dimmed" ta="center" maw={760} className={classes.description}>
          {PLATFORM_DESCRIPTION}
        </Text>

        <SimpleGrid cols={{ base: 1, sm: 2 }} spacing="md" maw={900} w="100%">
          {features.map((feature) => (
            <Stack key={feature.title} gap="xs" className={classes.featureItem}>
              <Group gap="sm" wrap="nowrap">
                <Icon path={feature.icon} size={1.1} />
                <Title order={3} fw={700}>
                  {feature.title}
                </Title>
              </Group>
              <Text c="dimmed" size="sm">
                {feature.description}
              </Text>
            </Stack>
          ))}
        </SimpleGrid>
      </Stack>
    </WithNavBar>
  )
}

export default About
