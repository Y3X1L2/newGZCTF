import { Badge, Button, Group, SimpleGrid, Stack, Text, Title } from '@mantine/core'
import { useDisclosure, useInputState } from '@mantine/hooks'
import { showNotification } from '@mantine/notifications'
import { mdiArrowLeft, mdiCheck, mdiClose, mdiDocker, mdiFlagVariantOutline } from '@mdi/js'
import { Icon } from '@mdi/react'
import React, { FC, useEffect, useMemo, useState } from 'react'
import { useTranslation } from 'react-i18next'
import { Link, useParams } from 'react-router'
import { ChallengeCard } from '@Components/ChallengeCard'
import { ChallengeModal } from '@Components/ChallengeModal'
import { WithNavBar } from '@Components/WithNavbar'
import { YinyuPanel } from '@Components/yinyu/YinyuUI'
import { useConfig } from '@Hooks/useConfig'
import { encryptApiData } from '@Utils/Crypto'
import { showErrorMsg, useChallengeCategoryLabelMap } from '@Utils/Shared'
import { AnswerResult, ChallengeCategory } from '@Api'
import {
  TrainingCtfChallengeDetailModel,
  TrainingModuleChallengeModel,
  TrainingModuleModel,
  trainingApi,
} from '@Utils/TrainingApi'

const TrainingCtfChallenges: FC = () => {
  const { moduleId } = useParams()
  const id = Number(moduleId)
  const [module, setModule] = useState<TrainingModuleModel | null>(null)
  const [challenges, setChallenges] = useState<TrainingModuleChallengeModel[]>([])
  const [active, setActive] = useState<TrainingModuleChallengeModel | null>(null)
  const [detail, setDetail] = useState<TrainingCtfChallengeDetailModel | null>(null)
  const [opened, { open, close }] = useDisclosure(false)
  const [flag, setFlag] = useInputState('')
  const [activeFlagId, setActiveFlagId] = useState<number | null>(null)
  const [disabled, setDisabled] = useState(false)
  const [activeInstanceIds, setActiveInstanceIds] = useState<Set<number>>(new Set())
  const { config } = useConfig()
  const { t } = useTranslation()
  const categoryLabelMap = useChallengeCategoryLabelMap()

  const load = async () => {
    if (!id) return
    try {
      const [moduleRes, challengesRes] = await Promise.all([trainingApi.getModule(id), trainingApi.ctfChallenges(id)])
      setModule(moduleRes.data)
      setChallenges(challengesRes.data)
    } catch (e) {
      showErrorMsg(e, t)
    }
  }

  const loadDetail = async (challenge: TrainingModuleChallengeModel) => {
    setActive(challenge)
    try {
      const res = await trainingApi.ctfChallenge(id, challenge.exerciseChallengeId)
      setDetail(res.data)
      setActiveFlagId(res.data.flags?.[0]?.id ?? null)
      if (res.data.context?.instanceEntry) {
        setActiveInstanceIds((current) => new Set(current).add(challenge.exerciseChallengeId))
      }
      open()
    } catch (e) {
      showErrorMsg(e, t)
    }
  }

  useEffect(() => {
    void load()
  }, [id])

  const solved = detail?.solved ?? false
  const cateData = useMemo(
    () =>
      categoryLabelMap.get(active?.category ?? detail?.category ?? ChallengeCategory.Misc) ??
      categoryLabelMap.get(ChallengeCategory.Misc)!,
    [active?.category, detail?.category, categoryLabelMap]
  )

  const onCreate = async () => {
    if (!active || disabled) return
    setDisabled(true)
    setActiveInstanceIds((current) => new Set(current).add(active.exerciseChallengeId))
    try {
      const res = await trainingApi.createCtfContainer(id, active.exerciseChallengeId)
      setDetail((current) =>
        current
          ? {
              ...current,
              context: {
                ...current.context,
                closeTime: res.data.expectStopAt,
                instanceEntry: res.data.entry,
              },
            }
          : current
      )
      showNotification({ color: 'teal', message: '培训容器已启动', icon: <Icon path={mdiCheck} size={1} /> })
    } catch (e) {
      setActiveInstanceIds((current) => {
        const next = new Set(current)
        next.delete(active.exerciseChallengeId)
        return next
      })
      showErrorMsg(e, t)
    } finally {
      setDisabled(false)
    }
  }

  const onDestroy = async () => {
    if (!active || disabled) return
    setDisabled(true)
    try {
      await trainingApi.destroyCtfContainer(id, active.exerciseChallengeId)
      setDetail((current) =>
        current
          ? {
              ...current,
              context: {
                ...current.context,
                closeTime: null,
                instanceEntry: null,
              },
            }
          : current
      )
      setActiveInstanceIds((current) => {
        const next = new Set(current)
        next.delete(active.exerciseChallengeId)
        return next
      })
      showNotification({ color: 'teal', message: '培训容器已销毁', icon: <Icon path={mdiCheck} size={1} /> })
    } catch (e) {
      showErrorMsg(e, t)
    } finally {
      setDisabled(false)
    }
  }

  const onSubmit = async () => {
    if (!active || !flag.trim()) {
      showNotification({ color: 'red', message: '请输入 Flag', icon: <Icon path={mdiClose} size={1} /> })
      return
    }

    setDisabled(true)
    try {
      const res = await trainingApi.submitCtfFlag(id, active.exerciseChallengeId, {
        flag: await encryptApiData(t, flag, config.apiPublicKey),
        ...(detail?.flags && detail.flags.length > 1 && activeFlagId ? { flagId: activeFlagId } : {}),
      })

      setFlag('')
      if (res.data.status === AnswerResult.Accepted) {
        showNotification({ color: 'teal', title: 'Accepted', message: '培训题目已解出', icon: <Icon path={mdiCheck} size={1} /> })
        close()
      } else {
        showNotification({ color: 'red', title: 'Wrong Answer', message: 'Flag 不正确', icon: <Icon path={mdiClose} size={1} /> })
      }
      await load()
      if (active) {
        const fresh = await trainingApi.ctfChallenge(id, active.exerciseChallengeId)
        setDetail(fresh.data)
      }
    } catch (e) {
      showErrorMsg(e, t)
    } finally {
      setDisabled(false)
    }
  }

  const challengeForModal = detail
    ? {
        ...detail,
        score: 0,
        deadline: undefined,
      }
    : undefined

  return (
    <WithNavBar width="var(--container)">
      <Stack gap="md">
        <Group justify="space-between">
          <Button component={Link} to="/training" variant="subtle" leftSection={<Icon path={mdiArrowLeft} size={0.86} />}>
            返回培训
          </Button>
          <Badge className="yy-gradient-status">训练题目集</Badge>
        </Group>
        <YinyuPanel className="panel-card" p="md">
          <Group justify="space-between" align="start">
            <Stack gap={4}>
              <Title order={2}>{module?.title ?? '训练题目集'}</Title>
              {module?.summary ? <Text c="dimmed">{module.summary}</Text> : null}
            </Stack>
            <Title order={3}>
              {module?.challengeSolvedCount ?? 0}/{module?.challengeTotalCount ?? challenges.length}
            </Title>
          </Group>
        </YinyuPanel>
        <SimpleGrid cols={{ base: 1, sm: 2, lg: 3 }} spacing="md">
          {challenges.map((challenge) => (
            <ChallengeCard
              key={challenge.exerciseChallengeId}
              challenge={{
                id: challenge.exerciseChallengeId,
                title: challenge.displayTitle || challenge.title,
                category: challenge.category,
                score: 0,
                solved: 0,
                bloods: [],
                disableBloodBonus: true,
              }}
              solved={false}
              onClick={() => void loadDetail(challenge)}
              instanceActive={activeInstanceIds.has(challenge.exerciseChallengeId)}
            />
          ))}
          {module && challenges.length === 0 ? (
            <YinyuPanel className="panel-card" p="md">
              <Stack gap="sm">
                <Group>
                  <Icon path={mdiFlagVariantOutline} size={1} />
                  <Text fw={800}>暂无训练题目</Text>
                </Group>
                <Text c="dimmed">老师还没有为该模块配置练手题目。</Text>
              </Stack>
            </YinyuPanel>
          ) : null}
        </SimpleGrid>
      </Stack>
      <ChallengeModal
        opened={opened}
        onClose={() => {
          setFlag('')
          close()
        }}
        gameTitle="培训模块"
        cateData={cateData}
        challenge={challengeForModal as any}
        solved={solved}
        flag={flag}
        setFlag={setFlag}
        onCreate={onCreate}
        onDestroy={onDestroy}
        onExtend={() => undefined}
        onSubmitFlag={onSubmit}
        disabled={disabled}
        gameEnded={false}
        practiceMode
        activeFlagId={activeFlagId}
        setActiveFlagId={setActiveFlagId}
      />
    </WithNavBar>
  )
}

export default TrainingCtfChallenges
