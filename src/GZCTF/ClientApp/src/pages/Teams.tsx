import {
  Avatar,
  Badge,
  Button,
  Center,
  Group,
  Modal,
  Select,
  Stack,
  Text,
  TextInput,
  Title,
} from '@mantine/core'
import { showNotification } from '@mantine/notifications'
import {
  mdiAccountGroup,
  mdiAccountMultiplePlus,
  mdiChartTimelineVariant,
  mdiCheck,
  mdiClose,
  mdiCrown,
  mdiHumanGreetingVariant,
  mdiPencil,
} from '@mdi/js'
import { Icon } from '@mdi/react'
import dayjs from 'dayjs'
import type { EChartsOption, SeriesOption } from 'echarts'
import { FC, useEffect, useMemo, useState } from 'react'
import { useTranslation } from 'react-i18next'
import { EchartsContainer } from '@Components/charts/EchartsContainer'
import { TeamCreateModal } from '@Components/TeamCreateModal'
import { TeamEditModal } from '@Components/TeamEditModal'
import { WithNavBar } from '@Components/WithNavbar'
import { WithRole } from '@Components/WithRole'
import { YinyuHeartbeatIcon, YinyuLoadingState, YinyuModalBody } from '@Components/yinyu/YinyuUI'
import { normalizeLanguage, useLanguage } from '@Utils/I18n'
import { showErrorMsg } from '@Utils/Shared'
import { useIsMobile } from '@Utils/ThemeOverride'
import { OnceSWRConfig } from '@Hooks/useConfig'
import { usePageTitle } from '@Hooks/usePageTitle'
import { useTeams, useUser } from '@Hooks/useUser'
import api, { BasicGameInfoModel, Role, TeamInfoModel, TimeLine, TopTimeLine } from '@Api'

const codePattern = /:\d+:[0-9a-f]{32}$/

interface TeamScoreCurveProps {
  team?: TeamInfoModel
}

const findTeamTimeline = (timelines: TopTimeLine[] | undefined, team?: TeamInfoModel) => {
  if (!team) return undefined
  const teamName = team.name?.trim()
  if (!teamName) return undefined
  return timelines?.find((item) => item.name === teamName)
}

const TeamScoreCurve: FC<TeamScoreCurveProps> = ({ team }) => {
  const { t } = useTranslation()
  const { language } = useLanguage()
  const locale = normalizeLanguage(language)
  const { data: games } = api.game.useGameRecentGames({ limit: 50 }, OnceSWRConfig)
  const [selectedGameId, setSelectedGameId] = useState<string | null>(null)

  const gameList = useMemo(() => games ?? [], [games])
  const selectedGame = useMemo(
    () => gameList.find((item) => String(item.id) === selectedGameId) ?? gameList[0],
    [gameList, selectedGameId]
  )
  const { data: scoreboard } = api.game.useGameScoreboard(selectedGame?.id ?? 0, OnceSWRConfig, !!selectedGame?.id)

  useEffect(() => {
    if (!selectedGameId && gameList[0]) {
      setSelectedGameId(String(gameList[0].id))
    }
  }, [gameList, selectedGameId])

  const gameOptions = useMemo(
    () =>
      gameList.map((game: BasicGameInfoModel) => ({
        value: String(game.id),
        label: game.title ?? `赛事 #${game.id}`,
      })),
    [gameList]
  )

  const teamTimeline = useMemo(() => {
    const overall = scoreboard?.timelines?.find((item) => item.divisionId === undefined || item.divisionId === 0)?.teams
    const direct = findTeamTimeline(overall, team)
    if (direct) return direct

    for (const timeline of scoreboard?.timelines ?? []) {
      const found = findTeamTimeline(timeline.teams, team)
      if (found) return found
    }

    return undefined
  }, [scoreboard?.timelines, team])

  const chartSeries = useMemo<SeriesOption[]>(() => {
    if (!selectedGame || !teamTimeline) return []

    const now = dayjs()
    const end = dayjs(selectedGame.end)
    const last = end.diff(now, 's') < 0 ? end : now
    const items = teamTimeline.items ?? []

    return [
      {
        type: 'line',
        step: 'end',
        name: teamTimeline.name,
        showSymbol: true,
        symbol: 'circle',
        symbolSize: 6,
        lineStyle: {
          width: 2.8,
          shadowBlur: 12,
          shadowColor: 'rgba(107, 238, 177, 0.22)',
        },
        areaStyle: {
          color: 'rgba(107, 238, 177, 0.12)',
        },
        emphasis: {
          focus: 'series',
          lineStyle: { width: 3.4 },
        },
        data: [
          [dayjs(selectedGame.start).toDate(), 0],
          ...items.map((item: TimeLine) => [item.time, item.score]),
          [last.toDate(), items[items.length - 1]?.score ?? 0],
        ],
      },
    ]
  }, [selectedGame, teamTimeline])

  const option = useMemo<EChartsOption>(() => {
    const labelColor = 'rgba(244, 245, 245, 0.84)'
    const lineColor = 'rgba(244, 245, 245, 0.14)'

    return {
      animation: false,
      backgroundColor: 'transparent',
      color: ['#6beeb1', '#8f7aff'],
      xAxis: {
        type: 'time',
        min: selectedGame ? dayjs(selectedGame.start).toDate() : undefined,
        max: selectedGame ? dayjs(selectedGame.end).toDate() : undefined,
        axisLabel: { color: labelColor },
        axisLine: { lineStyle: { color: lineColor } },
        axisTick: { lineStyle: { color: lineColor } },
        splitLine: { show: true, lineStyle: { color: 'rgba(244, 245, 245, 0.06)' } },
      },
      yAxis: {
        type: 'value',
        name: t('game.label.score'),
        nameTextStyle: { color: labelColor },
        axisLabel: { color: labelColor, formatter: t('game.label.score_formatter') },
        splitLine: { show: true, lineStyle: { color: lineColor, type: 'dashed' } },
      },
      tooltip: {
        trigger: 'axis',
        confine: true,
        backgroundColor: 'rgba(8, 12, 12, 0.94)',
        borderColor: 'rgba(107, 238, 177, 0.22)',
        textStyle: { color: labelColor },
      },
      grid: { top: 46, left: 64, right: 28, bottom: 72 },
      dataZoom: [
        {
          type: 'slider',
          start: 0,
          end: 100,
          xAxisIndex: 0,
          bottom: 24,
          height: 24,
          brushSelect: false,
          borderColor: 'rgba(107, 238, 177, 0.16)',
          fillerColor: 'rgba(107, 238, 177, 0.16)',
          backgroundColor: 'rgba(255, 255, 255, 0.045)',
          textStyle: { color: labelColor, fontSize: 11 },
          labelFormatter: (value: number | string) => dayjs(value).format('MM/DD HH:mm'),
        },
      ],
      series: chartSeries,
    }
  }, [chartSeries, selectedGame, t])

  return (
    <section className="panel-card yy-team-score-panel">
      <Group justify="space-between" align="flex-start" gap="md" className="yy-team-section-head">
        <div>
          <span className="yy-section-kicker">SCORE CURVE</span>
          <Title order={2}>积分曲线</Title>
          <Text size="sm" className="yy-readable-text">
            选择比赛后仅展示当前队伍的积分变化。
          </Text>
        </div>
        <Select
          className="yy-team-score-select"
          data={gameOptions}
          value={selectedGame ? String(selectedGame.id) : null}
          placeholder="选择比赛"
          onChange={setSelectedGameId}
          leftSection={<Icon path={mdiChartTimelineVariant} size={0.9} />}
        />
      </Group>
      {teamTimeline && chartSeries.length > 0 ? (
        <EchartsContainer
          option={option}
          opts={{ renderer: 'svg', locale }}
          style={{ width: '100%', height: '360px', display: 'flex' }}
        />
      ) : (
        <Center className="yy-team-score-empty">
          <Stack align="center" gap="xs">
            <YinyuHeartbeatIcon label="team score empty" />
            <Text fw={800}>暂无该队伍积分曲线</Text>
            <Text size="sm" className="yy-readable-text">
              当前比赛没有找到该队伍的计分时间线。
            </Text>
          </Stack>
        </Center>
      )}
    </section>
  )
}

const Teams: FC = () => {
  const { user, error: userError } = useUser()
  const { teams, mutate: mutateTeams, error: teamsError } = useTeams()
  const [joinOpened, setJoinOpened] = useState(false)
  const [joinTeamCode, setJoinTeamCode] = useState('')
  const [createOpened, setCreateOpened] = useState(false)
  const [editOpened, setEditOpened] = useState(false)
  const [editTeam, setEditTeam] = useState<TeamInfoModel | null>(null)
  const [selectedTeamId, setSelectedTeamId] = useState<number | undefined>()
  const isMobile = useIsMobile()
  const { t } = useTranslation()

  usePageTitle(t('team.title.index'))

  const teamsOwned = useMemo(
    () => teams?.filter((team) => team.members?.some((member) => member?.captain && member.id === user?.userId)) ?? [],
    [teams, user?.userId]
  )
  const disallowCreate = teamsOwned.length >= 3
  const selectedTeam = useMemo(
    () => teams?.find((team) => team.id === selectedTeamId) ?? teams?.[0],
    [selectedTeamId, teams]
  )
  const selectedMembers = useMemo(
    () => [...(selectedTeam?.members ?? [])].sort((left, right) => Number(Boolean(right.captain)) - Number(Boolean(left.captain))),
    [selectedTeam?.members]
  )
  const selectedCaptain = selectedMembers.find((member) => member.captain)
  const currentMember = selectedMembers.find((member) => member.id === user?.userId)
  const selectedIsCaptain = Boolean(currentMember?.captain)

  useEffect(() => {
    if (!teams?.length) {
      setSelectedTeamId(undefined)
      return
    }

    if (!selectedTeamId || !teams.some((team) => team.id === selectedTeamId)) {
      setSelectedTeamId(teams[0].id)
    }
  }, [selectedTeamId, teams])

  const onJoinTeam = async () => {
    if (!codePattern.test(joinTeamCode)) {
      showNotification({
        color: 'red',
        title: t('common.error.encountered'),
        message: t('team.notification.join.wrong_invite_code'),
        icon: <Icon path={mdiClose} size={1} />,
      })
      return
    }

    try {
      await api.team.teamAccept(joinTeamCode)
      showNotification({
        color: 'teal',
        title: t('team.notification.join.success'),
        message: t('team.notification.updated'),
        icon: <Icon path={mdiCheck} size={1} />,
      })
      void mutateTeams()
    } catch (e) {
      showErrorMsg(e, t)
    } finally {
      setJoinTeamCode('')
      setJoinOpened(false)
    }
  }

  const openEditTeam = (team: TeamInfoModel) => {
    setEditTeam(team)
    setEditOpened(true)
  }

  return (
    <WithNavBar minWidth={0} width="var(--container)">
      <WithRole requiredRole={Role.User}>
        <Stack className="yy-page-frame view-stack yy-soft-enter yy-team-page yy-team-workspace">
          {teams && !teamsError && user && !userError ? (
            teams.length > 0 ? (
              <div className="yy-team-workspace-grid">
                <aside className="panel-card yy-team-sidebar">
                  <div className="yy-team-sidebar-head">
                    <span className="yy-section-kicker">TEAM CENTER</span>
                    <Title order={1} className="yy-brand-title">
                      队伍管理
                    </Title>
                    <Text size="sm" className="yy-readable-text">
                      创建、加入队伍，并查看当前队伍资料与比赛积分走势。
                    </Text>
                  </div>

                  <div className="yy-team-sidebar-actions">
                    <Button
                      fullWidth
                      leftSection={<Icon path={mdiHumanGreetingVariant} size={1} />}
                      variant="outline"
                      className="yy-team-action yy-team-action-join"
                      onClick={() => setJoinOpened(true)}
                    >
                      {t('team.button.join')}
                    </Button>
                    <Button
                      fullWidth
                      leftSection={<Icon path={mdiAccountMultiplePlus} size={1} />}
                      variant="filled"
                      className="yy-team-action yy-team-action-create"
                      onClick={() => setCreateOpened(true)}
                    >
                      {t('team.button.create')}
                    </Button>
                  </div>

                  <div className="yy-team-sidebar-list">
                    <Group justify="space-between">
                      <Text fw={900}>已加入队伍</Text>
                      <Badge className="yy-team-role-badge">{teams.length}</Badge>
                    </Group>
                    {teams.map((team) => {
                      const captain = team.members?.some((member) => member?.captain && member.id === user.userId) ?? false

                      return (
                        <button
                          key={team.id ?? team.name}
                          type="button"
                          className={`yy-team-switch-card ${team.id === selectedTeam?.id ? 'is-active' : ''}`}
                          onClick={() => setSelectedTeamId(team.id)}
                        >
                          <Avatar src={team.avatar} alt={team.name ?? 'team'} radius="xl" size={42}>
                            {team.name?.slice(0, 1) ?? 'T'}
                          </Avatar>
                          <span>
                            <strong>{team.name ?? 'team'}</strong>
                            <small>{captain ? '队长' : '队员'}</small>
                          </span>
                        </button>
                      )
                    })}
                  </div>
                </aside>

                <main className="yy-team-main">
                  <section className="panel-card yy-team-detail-panel">
                    <Group justify="space-between" align="flex-start" gap="md" className="yy-team-section-head">
                      <div>
                        <span className="yy-section-kicker">TEAM PROFILE</span>
                        <Title order={2}>{selectedTeam?.name ?? '队伍信息'}</Title>
                        <Text size="sm" className="yy-readable-text">
                          {selectedIsCaptain ? '你是该队伍队长，可以维护队伍信息。' : '你是该队伍成员，可以查看队伍信息。'}
                        </Text>
                      </div>
                      {selectedTeam && (
                        <Button
                          leftSection={<Icon path={mdiPencil} size={1} />}
                          className="yy-team-action yy-team-action-create"
                          variant={selectedIsCaptain ? 'filled' : 'outline'}
                          onClick={() => openEditTeam(selectedTeam)}
                        >
                          {selectedIsCaptain ? t('team.button.edit') : '查看详情'}
                        </Button>
                      )}
                    </Group>

                    <div className="yy-team-profile-grid">
                      <div className="yy-team-current yy-team-current-compact">
                        <Avatar
                          src={selectedTeam?.avatar}
                          alt={selectedTeam?.name ?? 'team'}
                          radius="xl"
                          size={72}
                          className="yy-team-current-avatar"
                        >
                          {selectedTeam?.name?.slice(0, 1) ?? 'T'}
                        </Avatar>
                        <div>
                          <Group gap="xs" wrap="wrap">
                            <Title order={3}>{selectedTeam?.name ?? 'team'}</Title>
                            <Badge
                              className="yy-team-role-badge"
                              leftSection={<Icon path={selectedIsCaptain ? mdiCrown : mdiAccountGroup} size={0.8} />}
                            >
                              {selectedIsCaptain ? '队长' : '队员'}
                            </Badge>
                          </Group>
                          <Text>{selectedTeam?.bio || '暂无队伍简介'}</Text>
                        </div>
                      </div>

                      <div className="yy-team-member-summary yy-team-member-summary-compact">
                        <div>
                          <span>队长</span>
                          <strong>{selectedCaptain?.userName ?? '-'}</strong>
                        </div>
                        <div>
                          <span>成员</span>
                          <strong>{selectedMembers.length}</strong>
                        </div>
                        <div>
                          <span>我创建的队伍</span>
                          <strong>{teamsOwned.length}</strong>
                        </div>
                      </div>
                    </div>

                    <div className="yy-team-roster-list yy-team-roster-grid">
                      {selectedMembers.map((member) => (
                        <article key={member.id ?? member.userName} className={`yy-team-roster-row ${member.captain ? 'is-captain' : ''}`}>
                          <Avatar src={member.avatar} alt={member.userName ?? 'user'} radius="xl" size={50}>
                            {member.userName?.slice(0, 1) ?? 'U'}
                          </Avatar>
                          <div className="yy-team-roster-user">
                            <strong>{member.userName ?? 'user'}</strong>
                            <span>{member.bio || '暂无个人简介'}</span>
                          </div>
                          <Badge
                            className="yy-team-role-badge"
                            leftSection={<Icon path={member.captain ? mdiCrown : mdiAccountGroup} size={0.78} />}
                          >
                            {member.captain ? '队长' : '队员'}
                          </Badge>
                        </article>
                      ))}
                    </div>
                  </section>

                  <TeamScoreCurve team={selectedTeam} />
                </main>
              </div>
            ) : (
              <Center w="100%" mih="48vh" className="state-card panel-card yy-team-empty-state">
                <Stack align="center" gap="md" maw={isMobile ? '90%' : '100%'}>
                  <YinyuHeartbeatIcon label="team empty signal" />
                  <Icon path={mdiAccountMultiplePlus} size={4} />
                  <Title order={2} ta="center">
                    {t('team.content.no_team.title')}
                  </Title>
                  <Text size="sm" className="yy-readable-text" ta="center">
                    {t('team.content.no_team.hint')}
                  </Text>
                  <Group justify="center">
                    <Button
                      leftSection={<Icon path={mdiHumanGreetingVariant} size={1} />}
                      variant="outline"
                      className="yy-team-action yy-team-action-join"
                      onClick={() => setJoinOpened(true)}
                    >
                      {t('team.button.join')}
                    </Button>
                    <Button
                      leftSection={<Icon path={mdiAccountMultiplePlus} size={1} />}
                      className="yy-team-action yy-team-action-create"
                      onClick={() => setCreateOpened(true)}
                    >
                      {t('team.button.create')}
                    </Button>
                  </Group>
                </Stack>
              </Center>
            )
          ) : (
            <YinyuLoadingState title={t('team.title.index')} description="正在读取队伍信息" />
          )}
        </Stack>

        <Modal opened={joinOpened} title={t('team.button.join')} onClose={() => setJoinOpened(false)}>
          <YinyuModalBody>
            <Text size="sm">{t('team.content.join')}</Text>
            <TextInput
              label={t('team.label.invite_code')}
              type="text"
              placeholder="team:0:01234567890123456789012345678901"
              w="100%"
              value={joinTeamCode}
              onChange={(event) => setJoinTeamCode(event.currentTarget.value)}
            />
            <Button fullWidth variant="outline" className="yy-team-action yy-team-action-join" onClick={onJoinTeam}>
              {t('team.button.join')}
            </Button>
          </YinyuModalBody>
        </Modal>

        <TeamCreateModal
          opened={createOpened}
          title={t('team.button.create')}
          disallowCreate={disallowCreate}
          onClose={() => setCreateOpened(false)}
          mutate={mutateTeams}
        />

        <TeamEditModal
          opened={editOpened}
          title={t('team.button.edit')}
          onClose={() => setEditOpened(false)}
          team={editTeam}
          isCaptain={editTeam?.members?.some((member) => member?.captain && member.id === user?.userId) ?? false}
        />
      </WithRole>
    </WithNavBar>
  )
}

export default Teams
