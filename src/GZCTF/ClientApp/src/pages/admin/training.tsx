import {
  Badge,
  Button,
  Checkbox,
  Group,
  MultiSelect,
  NumberInput,
  Progress,
  ScrollArea,
  Select,
  SimpleGrid,
  Stack,
  Text,
  TextInput,
  Textarea,
  Title,
} from '@mantine/core'
import { showNotification } from '@mantine/notifications'
import { mdiCheck, mdiPlus, mdiPublish, mdiSchoolOutline } from '@mdi/js'
import { Icon } from '@mdi/react'
import React, { FC, useEffect, useMemo, useState } from 'react'
import { useTranslation } from 'react-i18next'
import { Role } from '@Api'
import { AdminPage } from '@Components/admin/AdminPage'
import { YinyuPanel } from '@Components/yinyu/YinyuUI'
import { useUser } from '@Hooks/useUser'
import { showErrorMsg } from '@Utils/Shared'
import {
  StudentGroupBriefModel,
  StudentGroupDetailModel,
  TrainingDirectionModel,
  TrainingModuleEditModel,
  TrainingModuleModel,
  TheoryTrainingPlanEditModel,
  TrainingGroupStatsModel,
  TrainingType,
  TrainingVisibilityType,
  trainingAdminApi,
} from '@Utils/TrainingApi'

const defaultRule = {
  requireArticleRead: true,
  requireAllRequiredChallenges: true,
  requiredChallengeCount: 0,
  theoryPassRate: 80,
}

interface ImageTemplateOption {
  id: number
  name: string
  imageType?: string | number
  status?: string | number
  registryUrl?: string | null
}

const emptyModule = (direction?: TrainingDirectionModel): TrainingModuleEditModel => ({
  directionId: direction?.id ?? 0,
  parentId: null,
  type: direction?.type ?? TrainingType.Ctf,
  title: '',
  slug: '',
  summary: '',
  articleContent: '',
  articleContentType: 'Markdown',
  environmentTemplateId: null,
  completionRule: defaultRule,
  order: 0,
})

const getDescendantModuleIds = (modules: TrainingModuleModel[], moduleId: number | null) => {
  if (!moduleId) return new Set<number>()

  const childrenByParent = new Map<number, number[]>()
  for (const module of modules) {
    if (!module.parentId) continue
    childrenByParent.set(module.parentId, [...(childrenByParent.get(module.parentId) ?? []), module.id])
  }

  const descendants = new Set<number>()
  const stack = [...(childrenByParent.get(moduleId) ?? [])]
  while (stack.length > 0) {
    const current = stack.pop()
    if (!current || descendants.has(current)) continue
    descendants.add(current)
    stack.push(...(childrenByParent.get(current) ?? []))
  }

  return descendants
}

const TrainingAdminPage: FC = () => {
  const [groups, setGroups] = useState<StudentGroupBriefModel[]>([])
  const [directions, setDirections] = useState<TrainingDirectionModel[]>([])
  const [modules, setModules] = useState<TrainingModuleModel[]>([])
  const [imageTemplates, setImageTemplates] = useState<ImageTemplateOption[]>([])
  const [activeDirectionId, setActiveDirectionId] = useState<number | null>(null)
  const [activeModuleId, setActiveModuleId] = useState<number | null>(null)
  const [moduleDraft, setModuleDraft] = useState<TrainingModuleEditModel>(emptyModule())
  const [groupDraft, setGroupDraft] = useState({ name: '', description: '' })
  const [directionDraft, setDirectionDraft] = useState({
    type: TrainingType.Ctf,
    key: 'web',
    title: 'Web',
    description: 'Web 安全基础学习路径',
    icon: 'web',
    color: '#6beeb1',
    order: 0,
    isEnabled: true,
  })
  const [activeStatsGroupId, setActiveStatsGroupId] = useState<string | null>(null)
  const [visibilityGroupIds, setVisibilityGroupIds] = useState<string[]>([])
  const [visibilityAllStudents, setVisibilityAllStudents] = useState(false)
  const [activeGroup, setActiveGroup] = useState<StudentGroupDetailModel | null>(null)
  const [challengeDraft, setChallengeDraft] = useState({ exerciseChallengeId: 0, order: 0, isRequired: true, displayTitle: '' })
  const [gameChallengeId, setGameChallengeId] = useState<number | ''>('')
  const [theoryPlan, setTheoryPlan] = useState<TheoryTrainingPlanEditModel>({
    title: '',
    description: '',
    mode: 'Random',
    questionCount: 30,
    bankName: '',
    questionTypes: [],
    passRate: 80,
    allowRetake: true,
    showCorrectAnswerAfterSubmit: true,
    isPublished: false,
    questions: [],
  })
  const [manualQuestionIds, setManualQuestionIds] = useState('')
  const [groupStats, setGroupStats] = useState<TrainingGroupStatsModel | null>(null)
  const [loading, setLoading] = useState(true)
  const { t } = useTranslation()
  const { user } = useUser()

  const activeDirection = directions.find((d) => d.id === activeDirectionId)
  const activeModule = modules.find((m) => m.id === activeModuleId)
  const visibleModules = useMemo(
    () => modules.filter((module) => !activeDirectionId || module.directionId === activeDirectionId),
    [modules, activeDirectionId]
  )
  const invalidParentModuleIds = useMemo(
    () => getDescendantModuleIds(modules, activeModuleId),
    [modules, activeModuleId]
  )
  const parentModuleOptions = useMemo(
    () =>
      modules
        .filter(
          (module) =>
            module.directionId === moduleDraft.directionId &&
            module.type === moduleDraft.type &&
            module.id !== activeModuleId &&
            !invalidParentModuleIds.has(module.id)
        )
        .map((module) => ({
          value: module.id.toString(),
          label: `${module.parentId ? '└ ' : ''}${module.title}`,
        })),
    [modules, moduleDraft.directionId, moduleDraft.type, activeModuleId, invalidParentModuleIds]
  )
  const canPublishAllStudents = user?.role === Role.Admin || user?.role === Role.SuperAdmin
  const dockerTemplateOptions = useMemo(
    () =>
      imageTemplates
        .filter((template) => {
          const imageType = String(template.imageType ?? '').toLowerCase()
          const status = String(template.status ?? '').toLowerCase()
          return (imageType === '0' || imageType === 'docker') && (status === '0' || status === 'ready')
        })
        .map((template) => ({ value: template.id.toString(), label: template.name })),
    [imageTemplates]
  )

  const load = async () => {
    setLoading(true)
    try {
      const [groupRes, directionRes, moduleRes, templateRes] = await Promise.all([
        trainingAdminApi.groups(),
        trainingAdminApi.directions(),
        trainingAdminApi.modules(),
        fetch('/api/v1/image-templates?imageType=0&pageSize=200').then((res) => res.json()).catch(() => ({ items: [] })),
      ])
      setGroups(groupRes.data)
      setDirections(directionRes.data)
      setModules(moduleRes.data)
      setImageTemplates((templateRes?.items ?? templateRes?.data ?? []) as ImageTemplateOption[])
      setActiveDirectionId((current) => current ?? directionRes.data[0]?.id ?? null)
      if (activeStatsGroupId) {
        const detail = await trainingAdminApi.group(Number(activeStatsGroupId))
        setActiveGroup(detail.data)
      }
    } catch (e) {
      showErrorMsg(e, t)
    } finally {
      setLoading(false)
    }
  }

  useEffect(() => {
    void load()
  }, [])

  useEffect(() => {
    if (activeModule) {
      setModuleDraft({
        directionId: activeModule.directionId,
        parentId: activeModule.parentId,
        type: activeModule.type,
        title: activeModule.title,
        slug: activeModule.slug,
        summary: activeModule.summary,
        articleContent: activeModule.articleContent,
        articleContentType: activeModule.articleContentType,
        environmentTemplateId: activeModule.environmentTemplateId,
        completionRule: activeModule.completionRule,
        order: activeModule.order,
      })
      setVisibilityGroupIds(
        activeModule.visibilities
          .filter((item) => item.visibilityType === TrainingVisibilityType.GroupOnly && item.groupId)
          .map((item) => item.groupId!.toString())
      )
      setVisibilityAllStudents(activeModule.visibilities.some((item) => item.visibilityType === TrainingVisibilityType.AllStudents))
      setChallengeDraft({ exerciseChallengeId: 0, order: activeModule.challenges.length + 1, isRequired: true, displayTitle: '' })
    } else {
      setModuleDraft(emptyModule(activeDirection))
      setVisibilityGroupIds([])
      setVisibilityAllStudents(false)
      setChallengeDraft({ exerciseChallengeId: 0, order: 0, isRequired: true, displayTitle: '' })
    }
  }, [activeModule, activeDirection])

  useEffect(() => {
    const loadTheoryPlan = async () => {
      if (!activeModule || activeModule.type !== TrainingType.Theory) return
      try {
        const res = await trainingAdminApi.theoryPlan(activeModule.id)
        setTheoryPlan({
          title: res.data.title,
          description: res.data.description,
          mode: res.data.mode,
          questionCount: res.data.questionCount,
          bankName: res.data.bankName,
          questionTypes: res.data.questionTypes,
          passRate: res.data.passRate,
          allowRetake: res.data.allowRetake,
          showCorrectAnswerAfterSubmit: res.data.showCorrectAnswerAfterSubmit,
          isPublished: res.data.isPublished,
          questions: res.data.questions,
        })
        setManualQuestionIds(res.data.questions.map((question) => question.sourceQuestionId).join(', '))
      } catch (e) {
        showErrorMsg(e, t)
      }
    }

    void loadTheoryPlan()
  }, [activeModuleId])

  const createGroup = async () => {
    if (!groupDraft.name.trim()) return
    try {
      await trainingAdminApi.createGroup(groupDraft)
      setGroupDraft({ name: '', description: '' })
      await load()
    } catch (e) {
      showErrorMsg(e, t)
    }
  }

  const createDirection = async () => {
    try {
      const res = await trainingAdminApi.createDirection(directionDraft)
      setActiveDirectionId(res.data.id)
      await load()
    } catch (e) {
      showErrorMsg(e, t)
    }
  }

  const saveModule = async () => {
    if (!moduleDraft.title.trim() || !moduleDraft.directionId) return
    const normalizedParentId =
      moduleDraft.parentId && moduleDraft.parentId !== activeModuleId && !invalidParentModuleIds.has(moduleDraft.parentId)
        ? moduleDraft.parentId
        : null
    const payload = {
      ...moduleDraft,
      parentId: normalizedParentId,
    }

    try {
      let id = activeModuleId
      if (id) {
        await trainingAdminApi.updateModule(id, payload)
      } else {
        const res = await trainingAdminApi.createModule(payload)
        id = res.data.id
        setActiveModuleId(id)
      }

      if (id) {
        await trainingAdminApi.setVisibility(id, [
          ...(visibilityAllStudents
            ? [{ visibilityType: TrainingVisibilityType.AllStudents }]
            : []),
          ...[...new Set(visibilityGroupIds)]
            .map((groupId) => Number(groupId))
            .filter((groupId) => Number.isInteger(groupId) && groupId > 0)
            .map((groupId) => ({ visibilityType: TrainingVisibilityType.GroupOnly, groupId })),
        ])
      }

      await load()
      showNotification({ message: '培训模块已保存', color: 'teal', icon: <Icon path={mdiCheck} size={1} /> })
    } catch (e) {
      showErrorMsg(e, t)
    }
  }

  const publishModule = async () => {
    if (!activeModuleId) return
    try {
      await trainingAdminApi.publish(activeModuleId)
      await load()
      showNotification({ message: '培训模块已发布', color: 'teal', icon: <Icon path={mdiPublish} size={1} /> })
    } catch (e) {
      showErrorMsg(e, t)
    }
  }

  const addChallenge = async () => {
    if (!activeModuleId || challengeDraft.exerciseChallengeId <= 0) return
    try {
      await trainingAdminApi.addModuleChallenge(activeModuleId, challengeDraft)
      await load()
      showNotification({ message: '训练题目已加入模块', color: 'teal', icon: <Icon path={mdiCheck} size={1} /> })
    } catch (e) {
      showErrorMsg(e, t)
    }
  }

  const copyGameChallenge = async () => {
    if (!activeModuleId || !gameChallengeId) return
    try {
      await trainingAdminApi.copyGameChallenge(activeModuleId, Number(gameChallengeId))
      setGameChallengeId('')
      await load()
      showNotification({ message: '正式题目已复制为培训题目', color: 'teal', icon: <Icon path={mdiCheck} size={1} /> })
    } catch (e) {
      showErrorMsg(e, t)
    }
  }

  const saveTheoryPlan = async () => {
    if (!activeModuleId) return
    const questions =
      theoryPlan.mode === 'Manual'
        ? manualQuestionIds
            .split(/[\s,，]+/)
            .map((item) => Number(item.trim()))
            .filter((item) => Number.isInteger(item) && item > 0)
            .map((sourceQuestionId, index) => ({ sourceQuestionId, score: 1, order: index + 1 }))
        : theoryPlan.questions
    try {
      await trainingAdminApi.saveTheoryPlan(activeModuleId, { ...theoryPlan, questions })
      await load()
      showNotification({ message: '理论培训计划已保存', color: 'teal', icon: <Icon path={mdiCheck} size={1} /> })
    } catch (e) {
      showErrorMsg(e, t)
    }
  }

  const loadGroupStats = async (groupId: string | null) => {
    if (!groupId) return
    try {
      const [statsRes, groupRes] = await Promise.all([
        trainingAdminApi.groupStats(Number(groupId)),
        trainingAdminApi.group(Number(groupId)),
      ])
      setGroupStats(statsRes.data)
      setActiveGroup(groupRes.data)
    } catch (e) {
      showErrorMsg(e, t)
    }
  }

  const removeMember = async (studentId: string) => {
    if (!activeStatsGroupId) return
    try {
      await trainingAdminApi.removeGroupMember(Number(activeStatsGroupId), studentId)
      await load()
      await loadGroupStats(activeStatsGroupId)
      showNotification({ message: '学生已移出分组', color: 'teal', icon: <Icon path={mdiCheck} size={1} /> })
    } catch (e) {
      showErrorMsg(e, t)
    }
  }

  return (
    <AdminPage isLoading={loading}>
      <SimpleGrid cols={{ base: 1, lg: 3 }} spacing="md">
        <YinyuPanel className="panel-card" p="md">
          <Stack gap="md">
            <Group justify="space-between">
              <Title order={3}>学生分组</Title>
              <Badge className="yy-gradient-status">{groups.length} 组</Badge>
            </Group>
            <TextInput
              label="新建分组"
              placeholder="例如 2026 春季 Web 班"
              value={groupDraft.name}
              onChange={(event) => setGroupDraft({ ...groupDraft, name: event.currentTarget.value })}
            />
            <Textarea
              label="分组说明"
              minRows={2}
              value={groupDraft.description}
              onChange={(event) => setGroupDraft({ ...groupDraft, description: event.currentTarget.value })}
            />
            <Button leftSection={<Icon path={mdiPlus} size={0.86} />} onClick={createGroup}>
              创建分组
            </Button>
            <ScrollArea h="42vh" scrollbarSize={4}>
              <Stack gap="xs">
                {groups.map((group) => (
                  <button
                    key={group.id}
                    type="button"
                    className={`panel-card yy-training-list-item ${activeStatsGroupId === group.id.toString() ? 'is-active' : ''}`}
                    onClick={() => {
                      setActiveStatsGroupId(group.id.toString())
                      void loadGroupStats(group.id.toString())
                    }}
                  >
                    <strong>{group.name}</strong>
                    <span>{group.memberCount} 名学生</span>
                  </button>
                ))}
              </Stack>
            </ScrollArea>
            {groupStats && (
              <YinyuPanel p="sm" className="panel-card">
                <Stack gap="xs">
                  <Group justify="space-between">
                    <Text fw={900}>{groupStats.groupName}</Text>
                    <Badge className="yy-gradient-status">{groupStats.averageCompletionRate}%</Badge>
                  </Group>
                  <Progress value={groupStats.averageCompletionRate} radius="xl" color="teal" />
                  <Text size="sm" c="dimmed">
                    学生 {groupStats.studentCount} 人 / 已发布模块 {groupStats.totalModules} 个
                  </Text>
                  <ScrollArea h="18vh" scrollbarSize={4}>
                    <Stack gap={6}>
                      {groupStats.students.map((student) => {
                        const percent =
                          student.totalModules > 0
                            ? Math.round((student.completedModules / student.totalModules) * 100)
                            : 0
                        return (
                          <YinyuPanel key={student.userId} p="xs" className="panel-card">
                            <Stack gap={4}>
                              <Group justify="space-between" wrap="nowrap">
                                <Text fw={800} size="sm" lineClamp={1}>
                                  {student.realName || student.userName}
                                </Text>
                                <Badge size="xs" className="yy-gradient-status">
                                  {student.completedModules}/{student.totalModules}
                                </Badge>
                              </Group>
                              <Progress value={percent} size="xs" radius="xl" color="teal" />
                              <Text size="xs" c="dimmed">
                                CTF {student.ctfSolvedChallenges}/{student.ctfTotalChallenges} / 理论{' '}
                                {student.theoryCompletedModules}/{student.theoryTotalModules}
                              </Text>
                            </Stack>
                          </YinyuPanel>
                        )
                      })}
                    </Stack>
                  </ScrollArea>
                  {activeGroup && (
                    <ScrollArea h="16vh" scrollbarSize={4}>
                      <Stack gap={6}>
                        {activeGroup.members.map((member) => (
                          <Group key={member.studentId} justify="space-between" className="panel-card" p="xs">
                            <Stack gap={0}>
                              <Text size="sm" fw={800}>{member.realName || member.userName}</Text>
                              <Text size="xs" c="dimmed">{member.stdNumber || member.userName}</Text>
                            </Stack>
                            <Button size="xs" variant="subtle" color="red" onClick={() => removeMember(member.studentId)}>
                              移出
                            </Button>
                          </Group>
                        ))}
                      </Stack>
                    </ScrollArea>
                  )}
                </Stack>
              </YinyuPanel>
            )}
          </Stack>
        </YinyuPanel>

        <YinyuPanel className="panel-card" p="md">
          <Stack gap="md">
            <Group justify="space-between">
              <Title order={3}>培训大纲</Title>
              <Button size="xs" leftSection={<Icon path={mdiSchoolOutline} size={0.8} />} onClick={createDirection}>
                新建方向
              </Button>
            </Group>
            <SimpleGrid cols={2}>
              <Select
                label="方向类型"
                data={[
                  { value: TrainingType.Ctf, label: 'CTF 培训' },
                  { value: TrainingType.Theory, label: '理论培训' },
                ]}
                value={directionDraft.type}
                onChange={(value) => setDirectionDraft({ ...directionDraft, type: value as TrainingType })}
              />
              <TextInput
                label="方向标识"
                value={directionDraft.key}
                onChange={(event) => setDirectionDraft({ ...directionDraft, key: event.currentTarget.value })}
              />
            </SimpleGrid>
            <TextInput
              label="方向名称"
              value={directionDraft.title}
              onChange={(event) => setDirectionDraft({ ...directionDraft, title: event.currentTarget.value })}
            />
            <ScrollArea h="58vh" scrollbarSize={4}>
              <Stack gap="xs">
                {directions.map((direction) => (
                  <button
                    key={direction.id}
                    type="button"
                    className={`panel-card yy-training-list-item ${activeDirectionId === direction.id ? 'is-active' : ''}`}
                    onClick={() => {
                      setActiveDirectionId(direction.id)
                      setActiveModuleId(null)
                    }}
                  >
                    <strong>{direction.title}</strong>
                    <span>{direction.type === TrainingType.Ctf ? 'CTF 培训' : '理论培训'}</span>
                  </button>
                ))}
              </Stack>
            </ScrollArea>
          </Stack>
        </YinyuPanel>

        <YinyuPanel className="panel-card" p="md">
          <Stack gap="md">
            <Group justify="space-between">
              <Title order={3}>模块编辑</Title>
              <Button size="xs" variant="light" onClick={() => setActiveModuleId(null)}>
                新建模块
              </Button>
            </Group>
            <Select
              label="选择模块"
              searchable
              data={visibleModules.map((module) => ({
                value: module.id.toString(),
                label: `${module.title}${module.isPublished ? ' / 已发布' : ' / 草稿'}`,
              }))}
              value={activeModuleId?.toString() ?? null}
              onChange={(value) => setActiveModuleId(value ? Number(value) : null)}
            />
            <TextInput
              label="模块标题"
              value={moduleDraft.title}
              onChange={(event) => setModuleDraft({ ...moduleDraft, title: event.currentTarget.value })}
            />
            <Select
              label="父级大纲模块"
              description="用于组织多级学习路径；留空表示方向下的一级模块。"
              clearable
              searchable
              data={parentModuleOptions}
              value={moduleDraft.parentId?.toString() ?? null}
              onChange={(value) => setModuleDraft({ ...moduleDraft, parentId: value ? Number(value) : null })}
            />
            <Textarea
              label="模块摘要"
              minRows={2}
              value={moduleDraft.summary}
              onChange={(event) => setModuleDraft({ ...moduleDraft, summary: event.currentTarget.value })}
            />
            <Textarea
              label="知识点文章（Markdown）"
              minRows={8}
              value={moduleDraft.articleContent}
              onChange={(event) => setModuleDraft({ ...moduleDraft, articleContent: event.currentTarget.value })}
            />
            <Select
              label="默认环境模板"
              description="可选。模块练手题未单独配置环境时，可参考该模板组织教学环境。"
              clearable
              searchable
              data={dockerTemplateOptions}
              value={moduleDraft.environmentTemplateId?.toString() ?? null}
              onChange={(value) => setModuleDraft({ ...moduleDraft, environmentTemplateId: value ? Number(value) : null })}
            />
            <SimpleGrid cols={2}>
              <MultiSelect
                label="可见分组"
                description="可同时发布给多个学生分组。老师只能选择自己管理的分组。"
                data={groups.map((group) => ({ value: group.id.toString(), label: group.name }))}
                value={visibilityGroupIds}
                onChange={setVisibilityGroupIds}
                searchable
              />
              <NumberInput
                label="理论通过率"
                min={0}
                max={100}
                value={moduleDraft.completionRule.theoryPassRate}
                onChange={(value) =>
                  setModuleDraft({
                    ...moduleDraft,
                    completionRule: { ...moduleDraft.completionRule, theoryPassRate: Number(value) },
                  })
                }
              />
            </SimpleGrid>
            {canPublishAllStudents && (
              <Checkbox
                label="对全部学生可见"
                description="仅管理员和超级管理员可用；勾选后仍可额外选择分组用于统计聚合。"
                checked={visibilityAllStudents}
                onChange={(event) => setVisibilityAllStudents(event.currentTarget.checked)}
              />
            )}
            <Group grow>
              <Button onClick={saveModule}>保存模块</Button>
              <Button disabled={!activeModuleId} variant="light" onClick={publishModule}>
                发布
              </Button>
            </Group>
            {activeModuleId && moduleDraft.type === TrainingType.Ctf && (
              <YinyuPanel p="sm" className="panel-card">
                <Stack gap="xs">
                  <Text fw={900}>CTF 练手题目</Text>
                  <Text size="xs" c="dimmed">
                    可以复用已有培训题，也可以从正式比赛题目复制一份独立培训题，避免训练提交污染正式比赛榜单。
                  </Text>
                  <SimpleGrid cols={2}>
                    <NumberInput
                      label="正式比赛题目 ID"
                      min={1}
                      value={gameChallengeId}
                      onChange={(value) => setGameChallengeId(typeof value === 'number' ? value : '')}
                    />
                    <Button mt="xl" variant="light" disabled={!gameChallengeId} onClick={copyGameChallenge}>
                      复制正式题目
                    </Button>
                  </SimpleGrid>
                  <SimpleGrid cols={2}>
                    <NumberInput
                      label="培训题目 ID"
                      min={1}
                      value={challengeDraft.exerciseChallengeId}
                      onChange={(value) => setChallengeDraft({ ...challengeDraft, exerciseChallengeId: Number(value) })}
                    />
                    <NumberInput
                      label="排序"
                      min={0}
                      value={challengeDraft.order}
                      onChange={(value) => setChallengeDraft({ ...challengeDraft, order: Number(value) })}
                    />
                  </SimpleGrid>
                  <TextInput
                    label="显示标题（可选）"
                    value={challengeDraft.displayTitle}
                    onChange={(event) => setChallengeDraft({ ...challengeDraft, displayTitle: event.currentTarget.value })}
                  />
                  <Checkbox
                    label="必做题"
                    checked={challengeDraft.isRequired}
                    onChange={(event) => setChallengeDraft({ ...challengeDraft, isRequired: event.currentTarget.checked })}
                  />
                  <Button onClick={addChallenge}>加入题目</Button>
                  <Stack gap={4}>
                    {activeModule?.challenges.map((challenge) => (
                      <Text key={challenge.exerciseChallengeId} size="xs" c="dimmed">
                        #{challenge.exerciseChallengeId} {challenge.displayTitle || challenge.title}
                      </Text>
                    ))}
                  </Stack>
                </Stack>
              </YinyuPanel>
            )}
            {activeModuleId && moduleDraft.type === TrainingType.Theory && (
              <YinyuPanel p="sm" className="panel-card">
                <Stack gap="xs">
                  <Text fw={900}>理论培训测验</Text>
                  <TextInput
                    label="测验标题"
                    value={theoryPlan.title}
                    onChange={(event) => setTheoryPlan({ ...theoryPlan, title: event.currentTarget.value })}
                  />
                  <SimpleGrid cols={2}>
                    <Select
                      label="组卷方式"
                      data={[
                        { value: 'Random', label: '随机抽题' },
                        { value: 'Manual', label: '手动组卷' },
                      ]}
                      value={theoryPlan.mode}
                      onChange={(value) => setTheoryPlan({ ...theoryPlan, mode: (value as 'Random' | 'Manual') ?? 'Random' })}
                    />
                    <NumberInput
                      label="题量"
                      min={1}
                      max={500}
                      value={theoryPlan.questionCount}
                      onChange={(value) => setTheoryPlan({ ...theoryPlan, questionCount: Number(value) })}
                    />
                  </SimpleGrid>
                  {theoryPlan.mode === 'Manual' && (
                    <Textarea
                      label="手动组卷题目 ID"
                      description="用逗号、空格或换行分隔题库题目 ID，保存时会按输入顺序组卷。"
                      minRows={3}
                      value={manualQuestionIds}
                      onChange={(event) => setManualQuestionIds(event.currentTarget.value)}
                    />
                  )}
                  <SimpleGrid cols={2}>
                    <TextInput
                      label="题库名称（可选）"
                      value={theoryPlan.bankName ?? ''}
                      onChange={(event) => setTheoryPlan({ ...theoryPlan, bankName: event.currentTarget.value })}
                    />
                    <NumberInput
                      label="通过率"
                      min={0}
                      max={100}
                      value={theoryPlan.passRate}
                      onChange={(value) => setTheoryPlan({ ...theoryPlan, passRate: Number(value) })}
                    />
                  </SimpleGrid>
                  <Group>
                    <Checkbox
                      label="允许重考"
                      checked={theoryPlan.allowRetake}
                      onChange={(event) => setTheoryPlan({ ...theoryPlan, allowRetake: event.currentTarget.checked })}
                    />
                    <Checkbox
                      label="提交后显示答案"
                      checked={theoryPlan.showCorrectAnswerAfterSubmit}
                      onChange={(event) =>
                        setTheoryPlan({ ...theoryPlan, showCorrectAnswerAfterSubmit: event.currentTarget.checked })
                      }
                    />
                    <Checkbox
                      label="发布测验"
                      checked={theoryPlan.isPublished}
                      onChange={(event) => setTheoryPlan({ ...theoryPlan, isPublished: event.currentTarget.checked })}
                    />
                  </Group>
                  <Button onClick={saveTheoryPlan}>保存理论计划</Button>
                </Stack>
              </YinyuPanel>
            )}
          </Stack>
        </YinyuPanel>
      </SimpleGrid>
    </AdminPage>
  )
}

export default TrainingAdminPage
