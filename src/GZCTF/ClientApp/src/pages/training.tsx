import { Badge, Button, Group, Progress, ScrollArea, SimpleGrid, Stack, Text, Title } from '@mantine/core'
import { mdiArrowRight, mdiBookOpenPageVariantOutline, mdiFlagVariantOutline } from '@mdi/js'
import { Icon } from '@mdi/react'
import React, { FC, useEffect, useMemo, useState } from 'react'
import { useTranslation } from 'react-i18next'
import { Link } from 'react-router'
import { WithNavBar } from '@Components/WithNavbar'
import { YinyuPanel } from '@Components/yinyu/YinyuUI'
import { showErrorMsg } from '@Utils/Shared'
import {
  TrainingDirectionModel,
  TrainingModuleModel,
  TrainingModuleProgressStatus,
  TrainingOverviewModel,
  TrainingType,
  trainingApi,
} from '@Utils/TrainingApi'

const statusLabel = (status?: TrainingModuleProgressStatus | null) => {
  switch (status) {
    case TrainingModuleProgressStatus.Completed:
      return '已完成'
    case TrainingModuleProgressStatus.Practicing:
      return '练习中'
    case TrainingModuleProgressStatus.Reading:
      return '阅读中'
    default:
      return '未开始'
  }
}

const completionHint = (module: TrainingModuleModel, progress: number) => {
  if (module.progressStatus === TrainingModuleProgressStatus.Completed) return '模块已完成，可以继续复盘知识点或进入题目集练习。'
  if (module.completionRule.requireArticleRead && module.progressStatus === TrainingModuleProgressStatus.NotStarted) {
    return '先阅读知识点文章并标记已读，再进入题目集练手。'
  }
  if (module.type === TrainingType.Ctf && module.challengeTotalCount > module.challengeSolvedCount) {
    return `还需要完成 ${module.challengeTotalCount - module.challengeSolvedCount} 道练手题。`
  }
  if (module.type === TrainingType.Theory && progress < 100) return `完成测验并达到 ${module.completionRule.theoryPassRate}% 通过率后会标记完成。`
  return '继续完成本模块配置的学习任务。'
}

const Training: FC = () => {
  const [catalog, setCatalog] = useState<TrainingDirectionModel[]>([])
  const [overview, setOverview] = useState<TrainingOverviewModel | null>(null)
  const [activeDirectionId, setActiveDirectionId] = useState<number | null>(null)
  const [activeModuleId, setActiveModuleId] = useState<number | null>(null)
  const { t } = useTranslation()

  const activeDirection = catalog.find((direction) => direction.id === activeDirectionId) ?? catalog[0]
  const activeModule =
    activeDirection?.modules.find((module) => module.id === activeModuleId) ?? activeDirection?.modules[0] ?? null

  const ctfDirections = useMemo(() => catalog.filter((d) => d.type === TrainingType.Ctf), [catalog])
  const theoryDirections = useMemo(() => catalog.filter((d) => d.type === TrainingType.Theory), [catalog])
  const modulesByParent = useMemo(() => {
    const map = new Map<number | null, TrainingModuleModel[]>()
    for (const module of activeDirection?.modules ?? []) {
      const key = module.parentId ?? null
      map.set(key, [...(map.get(key) ?? []), module])
    }
    for (const list of map.values()) {
      list.sort((a, b) => a.order - b.order || a.id - b.id)
    }
    return map
  }, [activeDirection])
  const activeModuleChildren = activeModule ? (modulesByParent.get(activeModule.id) ?? []) : []

  const load = async () => {
    try {
      const [catalogRes, overviewRes] = await Promise.all([trainingApi.catalog(), trainingApi.overview()])
      setCatalog(catalogRes.data)
      setOverview(overviewRes.data)
      setActiveDirectionId((current) => current ?? catalogRes.data[0]?.id ?? null)
    } catch (e) {
      showErrorMsg(e, t)
    }
  }

  useEffect(() => {
    void load()
  }, [])

  useEffect(() => {
    if (activeDirection && !activeModuleId) {
      setActiveModuleId(activeDirection.modules[0]?.id ?? null)
    }
  }, [activeDirectionId, catalog.length])

  const renderDirectionGroup = (title: string, directions: TrainingDirectionModel[], icon: string) => (
    <Stack gap="xs">
      <Group gap="xs">
        <Icon path={icon} size={0.9} />
        <Text fw={900}>{title}</Text>
      </Group>
      {directions.map((direction) => (
        <button
          key={direction.id}
          type="button"
          className={`panel-card yy-training-list-item ${activeDirection?.id === direction.id ? 'is-active' : ''}`}
          onClick={() => {
            setActiveDirectionId(direction.id)
            setActiveModuleId(direction.modules[0]?.id ?? null)
          }}
        >
          <strong>{direction.title}</strong>
          <span>{direction.modules.length} 个模块</span>
        </button>
      ))}
      {directions.length === 0 ? (
        <YinyuPanel p="sm" className="panel-card">
          <Text size="sm" c="dimmed">暂无已发布内容。</Text>
        </YinyuPanel>
      ) : null}
    </Stack>
  )

  const moduleProgress = activeModule?.challengeTotalCount
    ? Math.round((activeModule.challengeSolvedCount / activeModule.challengeTotalCount) * 100)
    : activeModule?.progressStatus === TrainingModuleProgressStatus.Completed
      ? 100
      : 0

  return (
    <WithNavBar width="var(--container)">
      <SimpleGrid cols={{ base: 1, lg: 3 }} spacing="md">
        <YinyuPanel className="panel-card" p="md">
          <Stack gap="md">
            <Title order={2}>培训模块</Title>
            <Text size="sm" c="dimmed">
              按方向和知识模块推进学习，完成文章阅读后进入同款题目界面练手。
            </Text>
            <SimpleGrid cols={2}>
              <YinyuPanel p="sm" className="panel-card">
                <Text size="xs" c="dimmed">
                  总完成度
                </Text>
                <Title order={3}>{overview?.completedModules ?? 0}/{overview?.totalModules ?? 0}</Title>
              </YinyuPanel>
              <YinyuPanel p="sm" className="panel-card">
                <Text size="xs" c="dimmed">
                  CTF 题目
                </Text>
                <Title order={3}>{overview?.ctfSolvedChallenges ?? 0}/{overview?.ctfTotalChallenges ?? 0}</Title>
              </YinyuPanel>
            </SimpleGrid>
            <Progress value={overview?.completionRate ?? 0} radius="xl" color="teal" />
            <ScrollArea h="58vh" scrollbarSize={4}>
              <Stack gap="md">
                {renderDirectionGroup('CTF 培训', ctfDirections, mdiFlagVariantOutline)}
                {renderDirectionGroup('理论培训', theoryDirections, mdiBookOpenPageVariantOutline)}
              </Stack>
            </ScrollArea>
          </Stack>
        </YinyuPanel>

        <YinyuPanel className="panel-card" p="md">
          <Stack gap="sm">
            <Group justify="space-between">
              <Title order={3}>{activeDirection?.title ?? '暂无方向'}</Title>
              <Badge className="yy-gradient-status">{activeDirection?.type === TrainingType.Ctf ? 'CTF' : '理论'}</Badge>
            </Group>
            <ScrollArea h="72vh" scrollbarSize={4}>
              <Stack gap="xs">
                {(modulesByParent.get(null) ?? []).map((module) => {
                  const children = modulesByParent.get(module.id) ?? []
                  return (
                    <Stack key={module.id} gap={6}>
                      <button
                        type="button"
                        className={`panel-card yy-training-list-item ${activeModule?.id === module.id ? 'is-active' : ''}`}
                        onClick={() => setActiveModuleId(module.id)}
                      >
                        <strong>
                          {module.title}
                          {module.progressStatus === TrainingModuleProgressStatus.Completed ? '（已完成）' : ''}
                        </strong>
                        <span>{children.length > 0 ? `${children.length} 个子模块` : statusLabel(module.progressStatus)}</span>
                      </button>
                      {children.map((child) => (
                        <button
                          key={child.id}
                          type="button"
                          className={`panel-card yy-training-list-item yy-training-child-item ${activeModule?.id === child.id ? 'is-active' : ''}`}
                          onClick={() => setActiveModuleId(child.id)}
                        >
                          <strong>
                            {child.title}
                            {child.progressStatus === TrainingModuleProgressStatus.Completed ? '（已完成）' : ''}
                          </strong>
                          <span>{statusLabel(child.progressStatus)}</span>
                        </button>
                      ))}
                    </Stack>
                  )
                })}
              </Stack>
            </ScrollArea>
          </Stack>
        </YinyuPanel>

        <YinyuPanel className="panel-card" p="md">
          {activeModule ? (
            <Stack gap="md">
              <Group justify="space-between">
                <Title order={3}>{activeModule.title}</Title>
                <Badge className="yy-gradient-status">{statusLabel(activeModule.progressStatus)}</Badge>
              </Group>
              <Text size="sm" c="dimmed">
                {activeModule.summary || '老师还没有填写摘要。'}
              </Text>
              {activeModuleChildren.length > 0 && (
                <YinyuPanel p="sm" className="panel-card">
                  <Stack gap="xs">
                    <Text fw={900}>继续学习</Text>
                    {activeModuleChildren.map((child) => (
                      <button
                        key={child.id}
                        type="button"
                        className="panel-card yy-training-list-item yy-training-child-item"
                        onClick={() => setActiveModuleId(child.id)}
                      >
                        <strong>{child.title}</strong>
                        <span>{statusLabel(child.progressStatus)}</span>
                      </button>
                    ))}
                  </Stack>
                </YinyuPanel>
              )}
              <YinyuPanel p="sm" className="panel-card">
                <Group justify="space-between">
                  <Text fw={800}>模块进度</Text>
                  <Text fw={900}>{activeModule.challengeSolvedCount}/{activeModule.challengeTotalCount}</Text>
                </Group>
                <Progress mt="xs" value={moduleProgress} radius="xl" color="teal" />
                <Text mt="xs" size="xs" c="dimmed">
                  {completionHint(activeModule, moduleProgress)}
                </Text>
              </YinyuPanel>
              <ScrollArea h="38vh" scrollbarSize={4} className="panel-card">
                <Text p="md" style={{ whiteSpace: 'pre-wrap' }}>
                  {activeModule.articleContent || '暂无知识点文章。'}
                </Text>
              </ScrollArea>
              <Group grow>
                <Button onClick={() => activeModule && trainingApi.markRead(activeModule.id).then(load).catch((e) => showErrorMsg(e, t))}>
                  标记已读
                </Button>
                <Button
                  component={Link}
                  to={
                    activeModule.type === TrainingType.Ctf
                      ? `/training/ctf/modules/${activeModule.id}/challenges`
                      : `/training/theory/modules/${activeModule.id}/session`
                  }
                  rightSection={<Icon path={mdiArrowRight} size={0.88} />}
                >
                  {activeModule.type === TrainingType.Ctf ? '练练手' : '开始测验'}
                </Button>
              </Group>
            </Stack>
          ) : (
            <Stack gap="xs">
              <Title order={3}>暂无可见培训</Title>
              <Text c="dimmed">老师发布给你所在分组的培训内容会显示在这里。</Text>
            </Stack>
          )}
        </YinyuPanel>
      </SimpleGrid>
    </WithNavBar>
  )
}

export default Training
