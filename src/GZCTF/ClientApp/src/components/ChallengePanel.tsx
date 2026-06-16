import {
  Button,
  Card,
  Center,
  Divider,
  Group,
  ScrollArea,
  SimpleGrid,
  Skeleton,
  Stack,
  Switch,
  Text,
  Title,
} from '@mantine/core'
import { useLocalStorage } from '@mantine/hooks'
import { mdiFileUploadOutline, mdiFlagOutline, mdiPuzzle } from '@mdi/js'
import { Icon } from '@mdi/react'
import dayjs from 'dayjs'
import { FC, useCallback, useEffect, useMemo, useState } from 'react'
import { useTranslation } from 'react-i18next'
import { useLocation, useParams } from 'react-router'
import { ChallengeCard } from '@Components/ChallengeCard'
import { Empty } from '@Components/Empty'
import { GameChallengeModal } from '@Components/GameChallengeModal'
import { WriteupSubmitModal } from '@Components/WriteupSubmitModal'
import { YinyuHexField } from '@Components/yinyu/YinyuUI'
import { useChallengeCategoryLabelMap } from '@Utils/Shared'
import { useGame, useGameTeamInfo } from '@Hooks/useGame'
import { ChallengeInfo, ChallengeCategory, SubmissionType } from '@Api'
import classes from '@Styles/ChallengePanel.module.css'

const hasInstanceEntry = (chal: ChallengeInfo) =>
  Boolean((chal as ChallengeInfo & { context?: { instanceEntry?: string | null } }).context?.instanceEntry)

export const ChallengePanel: FC = () => {
  const { hash } = useLocation()
  const { id } = useParams()
  const numId = parseInt(id ?? '-1')

  const { teamInfo } = useGameTeamInfo(numId)
  const challenges = teamInfo?.challenges

  const { game } = useGame(numId)

  const categories = useMemo(() => Object.keys(challenges ?? {}), [challenges])
  const [activeTab, setActiveTab] = useState<ChallengeCategory | 'All'>('All')
  const [hideSolved, setHideSolved] = useLocalStorage({
    key: 'hide-solved',
    defaultValue: false,
    getInitialValueInEffect: false,
  })

  const allChallenges = useMemo(() => Object.values(challenges ?? {}).flat(), [challenges])
  const solvedChallengeMap = useMemo(
    () => new Map(teamInfo?.rank?.solvedChallenges?.map((c) => [c.id, c.type]) ?? []),
    [teamInfo?.rank?.solvedChallenges]
  )
  const solvedChallengeIds = useMemo(() => new Set(solvedChallengeMap.keys()), [solvedChallengeMap])

  const currentChallenges = useMemo(() => {
    if (!challenges) return undefined

    const source = activeTab !== 'All' ? (challenges[activeTab] ?? []) : allChallenges
    return source.filter((chal) => !hideSolved || !solvedChallengeIds.has(chal.id))
  }, [activeTab, allChallenges, challenges, hideSolved, solvedChallengeIds])

  const [challenge, setChallenge] = useState<ChallengeInfo | null>(null)
  const [detailOpened, setDetailOpened] = useState(false)
  const [writeupSubmitOpened, setWriteupSubmitOpened] = useState(false)
  const challengeCategoryLabelMap = useChallengeCategoryLabelMap()
  const { t } = useTranslation()
  const [activeInstanceChallengeIds, setActiveInstanceChallengeIds] = useState<Set<number>>(() => new Set())

  const markInstanceActive = useCallback((challengeId: number) => {
    setActiveInstanceChallengeIds((current) => {
      if (current.has(challengeId)) return current
      const next = new Set(current)
      next.add(challengeId)
      return next
    })
  }, [])

  const markInstanceInactive = useCallback((challengeId: number) => {
    setActiveInstanceChallengeIds((current) => {
      if (!current.has(challengeId)) return current
      const next = new Set(current)
      next.delete(challengeId)
      return next
    })
  }, [])

  const markSelectedInstanceActive = useCallback(() => {
    if (challenge?.id) markInstanceActive(challenge.id)
  }, [challenge?.id, markInstanceActive])

  const markSelectedInstanceInactive = useCallback(() => {
    if (challenge?.id) markInstanceInactive(challenge.id)
  }, [challenge?.id, markInstanceInactive])

  useEffect(() => {
    const challId = hash.slice(1).split('-')[0]
    if (challId && allChallenges) {
      const id = parseInt(challId)
      if (isNaN(id) || id < 0) return
      if (challenge?.id === id) return

      const chal = allChallenges.find((c) => c.id === id)
      if (chal) {
        setChallenge(chal)
        setDetailOpened(true)
      }
    }
  }, [hash, challenge?.id, allChallenges])

  useEffect(() => {
    if (!challenges) return
    const challengeIds = new Set(allChallenges.map((chal) => chal.id))
    const runningIds = new Set(
      allChallenges
        .filter(hasInstanceEntry)
        .map((chal) => chal.id)
    )

    setActiveInstanceChallengeIds((current) => {
      const next = new Set(current)
      let changed = false
      for (const id of runningIds) {
        if (!next.has(id)) {
          next.add(id)
          changed = true
        }
      }
      for (const id of current) {
        if (challengeIds.has(id) && !runningIds.has(id)) {
          next.delete(id)
          changed = true
        }
      }
      return changed ? next : current
    })
  }, [challenges, allChallenges])

  // skeleton for loading
  if (!challenges) {
    return (
      <>
        <Stack className="detail-panel panel-card yy-challenge-filter-panel" p="sm">
          <YinyuHexField cells={24} />
          {Array(9)
            .fill(null)
            .map((_v, i) => (
              <Group key={i} wrap="nowrap" p={10}>
                <Skeleton height="1.5rem" width="1.5rem" />
                <Skeleton height="1rem" />
              </Group>
            ))}
        </Stack>
        <SimpleGrid
          className="challenge-grid yy-challenge-grid yy-challenge-grid-loading"
          p="xs"
          pt={0}
          spacing="sm"
          pos="relative"
          cols={{ base: 3, w18: 4, w24: 6, w30: 8, w36: 10, w42: 12, w48: 14 }}
        >
          {Array(13)
            .fill(null)
            .map((_v, i) => (
              <Card key={i} shadow="sm">
                <Stack gap="sm" pos="relative" style={{ zIndex: 99 }}>
                  <Skeleton height="1.5rem" width="70%" mt={4} />
                  <Divider />
                  <Group wrap="nowrap" justify="space-between" align="start">
                    <Center>
                      <Skeleton height="1.5rem" width="5rem" />
                    </Center>
                    <Stack gap="xs">
                      <Skeleton height="1rem" width="6rem" mt={5} />
                      <Group justify="center" gap="md" h={20}>
                        <Skeleton height="1.2rem" width="1.2rem" />
                        <Skeleton height="1.2rem" width="1.2rem" />
                        <Skeleton height="1.2rem" width="1.2rem" />
                      </Group>
                    </Stack>
                  </Group>
                </Stack>
              </Card>
            ))}
        </SimpleGrid>
      </>
    )
  }

  if (allChallenges.length === 0) {
    return (
      <>
        <Stack className="detail-panel panel-card yy-challenge-filter-panel" p="sm">
          <YinyuHexField cells={24} />
          <Title order={3}>题目筛选</Title>
          <Text className="yy-readable-text">当前比赛暂无可用题目</Text>
        </Stack>
        <Center className="panel-card yy-challenge-empty-main">
          <YinyuHexField cells={40} />
          <Empty
            bordered
            description={t('game.content.no_challenge')}
            fontSize="xl"
            mdiPath={mdiFlagOutline}
            iconSize={8}
          />
        </Center>
      </>
    )
  }

  return (
    <>
      <Stack className="detail-panel panel-card yy-challenge-filter-panel" p="sm">
        <YinyuHexField cells={24} />
        {game?.writeupRequired && (
          <>
            <Button
              px="xs"
              justify="space-between"
              leftSection={<Icon path={mdiFileUploadOutline} size={1} />}
              onClick={() => setWriteupSubmitOpened(true)}
            >
              {t('game.button.submit_writeup')}
            </Button>
            <Divider />
          </>
        )}
        <Switch
          className="yy-challenge-solved-switch"
          checked={hideSolved}
          onChange={(e) => setHideSolved(e.target.checked)}
          classNames={{ body: classes.switch }}
          label={
            <Text fz="md" fw="bold">
              {t('game.button.hide_solved')}
            </Text>
          }
        />
        <div className="yy-challenge-filter-list" role="tablist" aria-label="题目分类">
          <button
            type="button"
            className="yy-challenge-filter-button"
            data-active={activeTab === 'All' ? 'true' : undefined}
            onClick={() => setActiveTab('All')}
          >
            <Icon path={mdiPuzzle} size={1} />
            <span>All</span>
            <strong>{allChallenges.length}</strong>
          </button>
          {categories.map((tab) => {
            const data = challengeCategoryLabelMap.get(tab as ChallengeCategory)!
            return (
              <button
                key={tab}
                type="button"
                className="yy-challenge-filter-button"
                data-active={activeTab === tab ? 'true' : undefined}
                onClick={() => setActiveTab(tab as ChallengeCategory)}
              >
                <Icon path={data?.icon} size={1} />
                <span>{data?.name}</span>
                <strong>{challenges && challenges[tab].length}</strong>
              </button>
            )
          })}
        </div>
      </Stack>
      <ScrollArea
        h="calc(100vh - 6.67rem)"
        pos="relative"
        offsetScrollbars
        scrollbarSize={4}
        className="yy-challenge-list"
        classNames={{ root: classes.scrollArea }}
      >
        {/* if rank is 0, and have no division, means scoreboard not ready yet */}
        {!teamInfo.rank?.divisionId && !teamInfo?.rank?.rank ? (
          <Center h="calc(100vh - 10rem)">
            <Stack gap={0}>
              <Title order={2}>{t('game.content.scoreboard_not_ready.title')}</Title>
              <Text>{t('game.content.scoreboard_not_ready.comment')}</Text>
            </Stack>
          </Center>
        ) : currentChallenges && currentChallenges.length ? (
          <SimpleGrid
            className="challenge-grid yy-challenge-grid"
            p="xs"
            pt={0}
            spacing="sm"
            cols={{ base: 3, w18: 4, w24: 6, w30: 8, w36: 10, w42: 12, w48: 14 }}
          >
            {currentChallenges?.map((chal) => {
              const status = solvedChallengeMap.get(chal.id)
              const solved = status !== SubmissionType.Unaccepted && status !== undefined

              return (
                <ChallengeCard
                  key={chal.id}
                  challenge={chal}
                  onClick={() => {
                    setChallenge(chal)
                    setDetailOpened(true)
                    // update hash after modal opened, so don't trigger useEffect
                    window.location.hash = `#${chal.id}-${encodeURIComponent(chal.title?.replace(/ /g, '-') ?? '')}`
                  }}
                  solved={solved}
                  teamId={teamInfo?.rank?.id}
                  instanceActive={activeInstanceChallengeIds.has(chal.id)}
                />
              )
            })}
          </SimpleGrid>
        ) : (
          <Center h="calc(100vh - 10rem)">
            <Stack gap={0}>
              <Title order={2}>{t('game.content.all_solved.title')}</Title>
              <Text>{t('game.content.all_solved.comment')}</Text>
            </Stack>
          </Center>
        )}
      </ScrollArea>
      {game?.writeupRequired && (
        <WriteupSubmitModal
          opened={writeupSubmitOpened}
          onClose={() => setWriteupSubmitOpened(false)}
          withCloseButton={false}
          size="40%"
          gameId={numId}
          writeupDeadline={teamInfo.writeupDeadline}
        />
      )}
      {challenge?.id && (
        <GameChallengeModal
          gameId={numId}
          gameTitle={game?.title ?? ''}
          opened={detailOpened}
          withCloseButton={false}
          onClose={() => {
            window.location.hash = ''
            setDetailOpened(false)
          }}
          gameEnded={dayjs(game?.end) < dayjs()}
          practiceMode={game?.practiceMode}
          status={teamInfo?.rank?.solvedChallenges?.find((c) => c.id === challenge?.id)?.type}
          cateData={
            challengeCategoryLabelMap.get((challenge?.category as ChallengeCategory) ?? ChallengeCategory.Misc)!
          }
          title={challenge?.title ?? ''}
          score={challenge?.score ?? 0}
          challengeId={challenge.id}
          onInstanceActive={markSelectedInstanceActive}
          onInstanceInactive={markSelectedInstanceInactive}
        />
      )}
    </>
  )
}
