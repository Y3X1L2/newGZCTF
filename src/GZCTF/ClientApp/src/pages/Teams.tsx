import {
  ActionIcon,
  Avatar,
  Badge,
  Button,
  Center,
  FileButton,
  Group,
  Modal,
  Select,
  Stack,
  Text,
  Textarea,
  TextInput,
  Title,
  Tooltip,
} from '@mantine/core'
import { useModals } from '@mantine/modals'
import { showNotification } from '@mantine/notifications'
import {
  mdiAccountGroup,
  mdiAccountCheck,
  mdiAccountCancel,
  mdiAccountMultiplePlus,
  mdiAccountSwitch,
  mdiChartTimelineVariant,
  mdiCheck,
  mdiClose,
  mdiContentCopy,
  mdiCrown,
  mdiHumanGreetingVariant,
  mdiImageEdit,
  mdiMagnify,
  mdiRefresh,
  mdiSend,
  mdiTrashCanOutline,
} from '@mdi/js'
import { Icon } from '@mdi/react'
import dayjs from 'dayjs'
import type { EChartsOption, SeriesOption } from 'echarts'
import { FC, useEffect, useMemo, useRef, useState } from 'react'
import { useTranslation } from 'react-i18next'
import { EchartsContainer } from '@Components/charts/EchartsContainer'
import { TeamCreateModal } from '@Components/TeamCreateModal'
import { WithNavBar } from '@Components/WithNavbar'
import { WithRole } from '@Components/WithRole'
import { YinyuHeartbeatIcon, YinyuLoadingState, YinyuModalBody } from '@Components/yinyu/YinyuUI'
import { normalizeLanguage, useLanguage } from '@Utils/I18n'
import { copyText, showErrorMsg } from '@Utils/Shared'
import { useIsMobile } from '@Utils/ThemeOverride'
import { OnceSWRConfig } from '@Hooks/useConfig'
import { usePageTitle } from '@Hooks/usePageTitle'
import { useTeams, useUser } from '@Hooks/useUser'
import api, { BasicGameInfoModel, ContentType, Role, TeamInfoModel, TeamUserInfoModel, TimeLine, TopTimeLine } from '@Api'

const codePattern = /:\d+:[0-9a-f]{32}$/

interface TeamJoinRequestModel {
  id: number
  teamId: number
  teamName?: string | null
  user: TeamUserInfoModel
  message?: string | null
  status: 'Pending' | 'Accepted' | 'Rejected'
  createdAtUtc: number | string
  reviewedAtUtc?: number | string | null
}

interface TeamScoreCurveProps {
  team?: TeamInfoModel
}

const searchJoinTeams = async (hint: string) => {
  const response = await api.request<TeamInfoModel[], unknown>({
    path: '/api/team/search',
    method: 'GET',
    query: { hint },
    format: 'json',
  })

  return response.data
}

const createJoinRequest = async (teamId: number, message: string) =>
  api.request<TeamJoinRequestModel, unknown>({
    path: `/api/team/${teamId}/requests`,
    method: 'POST',
    body: { message },
    type: ContentType.Json,
    format: 'json',
  })

const getJoinRequests = async (teamId: number) => {
  const response = await api.request<TeamJoinRequestModel[], unknown>({
    path: `/api/team/${teamId}/requests`,
    method: 'GET',
    format: 'json',
  })

  return response.data
}

const reviewJoinRequest = async (teamId: number, requestId: number, accepted: boolean) => {
  const response = await api.request<TeamInfoModel, unknown>({
    path: `/api/team/${teamId}/requests/${requestId}`,
    method: 'POST',
    body: { accepted },
    type: ContentType.Json,
    format: 'json',
  })

  return response.data
}

const findTeamTimeline = (timelines: TopTimeLine[] | undefined, team?: TeamInfoModel) => {
  if (typeof team?.id === 'number') {
    const byId = timelines?.find((item) => item.id === team.id)
    if (byId) return byId
  }

  const teamName = team?.name?.trim()
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
        label: game.title ?? `比赛 #${game.id}`,
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
          style={{ width: '100%', height: 'clamp(220px, 28vh, 300px)', display: 'flex' }}
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
  const [joinSearch, setJoinSearch] = useState('')
  const [joinMessage, setJoinMessage] = useState('')
  const [joinResults, setJoinResults] = useState<TeamInfoModel[]>([])
  const [joinLoading, setJoinLoading] = useState(false)
  const [createOpened, setCreateOpened] = useState(false)
  const [selectedTeamId, setSelectedTeamId] = useState<number | undefined>()
  const [teamDraft, setTeamDraft] = useState({ name: '', bio: '' })
  const [inviteCode, setInviteCode] = useState('')
  const [pendingRequests, setPendingRequests] = useState<TeamJoinRequestModel[]>([])
  const [requestsLoading, setRequestsLoading] = useState(false)
  const [working, setWorking] = useState(false)
  const inviteInputRef = useRef<HTMLInputElement>(null)
  const isMobile = useIsMobile()
  const { t } = useTranslation()
  const modals = useModals()

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

  useEffect(() => {
    setTeamDraft({
      name: selectedTeam?.name ?? '',
      bio: selectedTeam?.bio ?? '',
    })
    setInviteCode('')
    setPendingRequests([])
  }, [selectedTeam?.id, selectedTeam?.name, selectedTeam?.bio])

  useEffect(() => {
    if (!selectedTeam?.id || !selectedIsCaptain) {
      setInviteCode('')
      setPendingRequests([])
      return
    }

    let ignore = false
    const loadCaptainData = async () => {
      setRequestsLoading(true)

      try {
        const [invite, requests] = await Promise.all([
          api.team.teamInviteCode(selectedTeam.id!),
          getJoinRequests(selectedTeam.id!),
        ])

        if (ignore) return
        setInviteCode(invite.data)
        setPendingRequests(requests)
      } catch (e) {
        if (!ignore) showErrorMsg(e, t)
      } finally {
        if (!ignore) setRequestsLoading(false)
      }
    }

    void loadCaptainData()

    return () => {
      ignore = true
    }
  }, [selectedIsCaptain, selectedTeam?.id, t])

  const updateSelectedTeam = (nextTeam: TeamInfoModel) => {
    void mutateTeams(
      teams?.map((team) => (team.id === nextTeam.id ? nextTeam : team)),
      { revalidate: false }
    )
  }

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

  const onSearchJoinTeams = async () => {
    if (!joinSearch.trim()) return

    setJoinLoading(true)

    try {
      const results = await searchJoinTeams(joinSearch.trim())
      setJoinResults(results)
    } catch (e) {
      showErrorMsg(e, t)
    } finally {
      setJoinLoading(false)
    }
  }

  const onCreateJoinRequest = async (team: TeamInfoModel) => {
    if (!team.id) return

    setJoinLoading(true)

    try {
      await createJoinRequest(team.id, joinMessage)
      showNotification({
        color: 'teal',
        title: '入队申请已提交',
        message: '请等待队长处理申请。',
        icon: <Icon path={mdiCheck} size={1} />,
      })
      setJoinMessage('')
      setJoinOpened(false)
    } catch (e) {
      showErrorMsg(e, t)
    } finally {
      setJoinLoading(false)
    }
  }

  const onSaveTeam = async () => {
    if (!selectedTeam?.id || !selectedIsCaptain) return

    setWorking(true)

    try {
      const response = await api.team.teamUpdateTeam(selectedTeam.id, teamDraft)
      updateSelectedTeam(response.data)
      showNotification({
        color: 'teal',
        message: t('team.notification.updated'),
        icon: <Icon path={mdiCheck} size={1} />,
      })
    } catch (e) {
      showErrorMsg(e, t)
    } finally {
      setWorking(false)
    }
  }

  const onRefreshInviteCode = async () => {
    if (!selectedTeam?.id || !selectedIsCaptain) return

    setWorking(true)

    try {
      const response = await api.team.teamUpdateInviteToken(selectedTeam.id)
      setInviteCode(response.data)
      showNotification({
        color: 'teal',
        message: t('team.notification.invite_code.updated'),
        icon: <Icon path={mdiCheck} size={1} />,
      })
    } catch (e) {
      showErrorMsg(e, t)
    } finally {
      setWorking(false)
    }
  }

  const onCopyInviteCode = async () => {
    inviteInputRef.current?.focus()
    inviteInputRef.current?.select()
    const copied = await copyText(inviteCode)

    showNotification({
      color: copied ? 'teal' : 'red',
      message: copied ? t('team.notification.invite_code.copied') : '复制失败，请手动选中邀请码复制。',
      icon: <Icon path={copied ? mdiCheck : mdiClose} size={1} />,
    })
  }

  const onUploadAvatar = async (file: File | null) => {
    if (!file || !selectedTeam?.id || !selectedIsCaptain) return

    setWorking(true)

    try {
      const response = await api.team.teamAvatar(selectedTeam.id, { file })
      updateSelectedTeam({ ...selectedTeam, avatar: response.data })
      showNotification({
        color: 'teal',
        message: t('common.avatar.uploaded'),
        icon: <Icon path={mdiCheck} size={1} />,
      })
    } catch (e) {
      showErrorMsg(e, t)
    } finally {
      setWorking(false)
    }
  }

  const onKickMember = async (member: TeamUserInfoModel) => {
    if (!selectedTeam?.id || !member.id || !selectedIsCaptain) return

    try {
      const response = await api.team.teamKickUser(selectedTeam.id, member.id)
      updateSelectedTeam(response.data)
      showNotification({
        color: 'teal',
        title: t('team.notification.kick.success'),
        message: t('team.notification.updated'),
        icon: <Icon path={mdiCheck} size={1} />,
      })
    } catch (e) {
      showErrorMsg(e, t)
    }
  }

  const onTransferCaptain = async (member: TeamUserInfoModel) => {
    if (!selectedTeam?.id || !member.id || !selectedIsCaptain) return

    try {
      const response = await api.team.teamTransfer(selectedTeam.id, { newCaptainId: member.id })
      updateSelectedTeam(response.data)
      showNotification({
        color: 'teal',
        title: t('team.notification.transfer.success'),
        message: t('team.notification.updated'),
        icon: <Icon path={mdiCheck} size={1} />,
      })
    } catch (e) {
      showErrorMsg(e, t)
    }
  }

  const onReviewRequest = async (request: TeamJoinRequestModel, accepted: boolean) => {
    if (!selectedTeam?.id || !selectedIsCaptain) return

    setWorking(true)

    try {
      const nextTeam = await reviewJoinRequest(selectedTeam.id, request.id, accepted)
      updateSelectedTeam(nextTeam)
      setPendingRequests((current) => current.filter((item) => item.id !== request.id))
      showNotification({
        color: accepted ? 'teal' : 'orange',
        message: accepted ? '已同意入队申请' : '已拒绝入队申请',
        icon: <Icon path={accepted ? mdiCheck : mdiClose} size={1} />,
      })
    } catch (e) {
      showErrorMsg(e, t)
    } finally {
      setWorking(false)
    }
  }

  const onLeaveTeam = async () => {
    if (!selectedTeam?.id || selectedIsCaptain) return

    setWorking(true)
    try {
      await api.team.teamLeave(selectedTeam.id)
      showNotification({ color: 'teal', message: '已退出队伍。', icon: <Icon path={mdiCheck} size={1} /> })
      setSelectedTeamId(undefined)
      await mutateTeams()
    } catch (e) {
      showErrorMsg(e, t)
    } finally {
      setWorking(false)
    }
  }

  const onDeleteTeam = async () => {
    if (!selectedTeam?.id || !selectedIsCaptain) return

    setWorking(true)
    try {
      await api.team.teamDeleteTeam(selectedTeam.id)
      showNotification({ color: 'teal', message: '队伍已解散。', icon: <Icon path={mdiCheck} size={1} /> })
      setSelectedTeamId(undefined)
      await mutateTeams()
    } catch (e) {
      showErrorMsg(e, t)
    } finally {
      setWorking(false)
    }
  }

  return (
    <WithNavBar minWidth={0} width="100%">
      <WithRole requiredRole={Role.User}>
        <Stack
          className="yy-page-frame view-stack yy-soft-enter yy-team-page yy-team-workspace"
          style={{ width: '100%', maxWidth: 'none', alignItems: 'stretch' }}
        >
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
                  <section className="panel-card yy-team-detail-panel yy-team-profile-panel">
                    <Group justify="space-between" align="flex-start" gap="md" className="yy-team-section-head">
                      <div>
                        <span className="yy-section-kicker">TEAM PROFILE</span>
                        <Title order={2}>{selectedTeam?.name ?? '队伍信息'}</Title>
                        <Text size="sm" className="yy-readable-text">
                          {selectedIsCaptain ? '你是该队伍队长，可以维护队伍信息与成员。' : '你是该队伍成员，可以查看队伍资料与成员信息。'}
                        </Text>
                      </div>
                    </Group>

                    <div className={`yy-team-profile-layout ${selectedIsCaptain ? 'is-captain' : 'is-member'}`}>
                      <aside className="yy-team-profile-identity">
                        <TextInput
                          label="队伍名称"
                          value={selectedIsCaptain ? teamDraft.name : selectedTeam?.name ?? ''}
                          readOnly={!selectedIsCaptain}
                          onChange={(event) => setTeamDraft((current) => ({ ...current, name: event.currentTarget.value }))}
                          className="yy-team-inline-input"
                        />
                        <div className="yy-team-avatar-edit-shell">
                          <Avatar
                            src={selectedTeam?.avatar}
                            alt={selectedTeam?.name ?? 'team'}
                            radius="xl"
                            size={132}
                            className="yy-team-current-avatar"
                          >
                            {selectedTeam?.name?.slice(0, 1) ?? 'T'}
                          </Avatar>
                          {selectedIsCaptain ? (
                            <FileButton onChange={onUploadAvatar} accept="image/png,image/jpeg,image/webp,image/gif">
                              {(props) => (
                                <ActionIcon
                                  {...props}
                                  className="yy-team-avatar-edit-button"
                                  variant="filled"
                                  loading={working}
                                  aria-label="更换队伍头像"
                                >
                                  <Icon path={mdiImageEdit} size={0.85} />
                                </ActionIcon>
                              )}
                            </FileButton>
                          ) : null}
                        </div>
                        <Badge
                          className="yy-team-role-badge"
                          leftSection={<Icon path={selectedIsCaptain ? mdiCrown : mdiAccountGroup} size={0.8} />}
                        >
                          {selectedIsCaptain ? '队长' : '队员'}
                        </Badge>
                        {selectedIsCaptain ? (
                          <Stack gap="xs" w="100%" className="yy-team-identity-actions">
                            <Button
                              fullWidth
                              className="yy-team-action yy-team-action-create"
                              variant="filled"
                              loading={working}
                              onClick={onSaveTeam}
                            >
                              保存资料
                            </Button>
                            <Button
                              fullWidth
                              variant="light"
                              color="red"
                              loading={working}
                              leftSection={<Icon path={mdiTrashCanOutline} size={0.82} />}
                              onClick={() => {
                                modals.openConfirmModal({
                                  title: '解散队伍',
                                  children: <Text size="sm">确认解散 {selectedTeam?.name ?? '当前队伍'}？该操作不可撤销。</Text>,
                                  confirmProps: { color: 'red' },
                                  labels: { confirm: '确认解散', cancel: '取消' },
                                  zIndex: 10000,
                                  onConfirm: () => void onDeleteTeam(),
                                })
                              }}
                            >
                              解散队伍
                            </Button>
                          </Stack>
                        ) : (
                          <Stack gap="xs" w="100%" className="yy-team-identity-actions">
                            <Text size="xs" className="yy-readable-text yy-team-readonly-note">
                              队伍资料由队长维护。
                            </Text>
                            <Button
                              fullWidth
                              variant="light"
                              color="red"
                              loading={working}
                              leftSection={<Icon path={mdiTrashCanOutline} size={0.82} />}
                              onClick={() => {
                                modals.openConfirmModal({
                                  title: '退出队伍',
                                  children: <Text size="sm">确认退出 {selectedTeam?.name ?? '当前队伍'}？</Text>,
                                  confirmProps: { color: 'red' },
                                  labels: { confirm: '确认退出', cancel: '取消' },
                                  zIndex: 10000,
                                  onConfirm: () => void onLeaveTeam(),
                                })
                              }}
                            >
                              退出队伍
                            </Button>
                          </Stack>
                        )}
                      </aside>

                      <section className="yy-team-members-panel">
                        <Group justify="space-between" align="center" className="yy-team-subhead">
                          <div>
                            <span className="yy-section-kicker">MEMBERS</span>
                            <Title order={3}>队伍成员</Title>
                          </div>
                          <Badge className="yy-team-role-badge">{selectedMembers.length} 人</Badge>
                        </Group>
                        <div className="yy-team-roster-list yy-team-roster-grid">
                          {selectedMembers.map((member) => (
                            <article
                              key={member.id ?? member.userName}
                              className={`yy-team-roster-row ${member.captain ? 'is-captain' : ''}`}
                            >
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
                              {selectedIsCaptain && !member.captain ? (
                                <Group gap={5} wrap="nowrap" className="yy-team-roster-actions">
                                  <Tooltip label="转让队长">
                                    <ActionIcon
                                      variant="light"
                                      color="yellow"
                                      onClick={() => {
                                        modals.openConfirmModal({
                                          title: '转让队长',
                                          children: (
                                            <Text size="sm">
                                              确认将 {selectedTeam?.name ?? '当前队伍'} 的队长转让给 {member.userName ?? '该成员'}？
                                            </Text>
                                          ),
                                          confirmProps: { color: 'orange' },
                                          zIndex: 10000,
                                          onConfirm: () => void onTransferCaptain(member),
                                        })
                                      }}
                                    >
                                      <Icon path={mdiAccountSwitch} size={0.78} />
                                    </ActionIcon>
                                  </Tooltip>
                                  <Tooltip label="剔除队员">
                                    <ActionIcon
                                      variant="light"
                                      color="red"
                                      onClick={() => {
                                        modals.openConfirmModal({
                                          title: '剔除队员',
                                          children: <Text size="sm">确认将 {member.userName ?? '该成员'} 移出队伍？</Text>,
                                          confirmProps: { color: 'red' },
                                          zIndex: 10000,
                                          onConfirm: () => void onKickMember(member),
                                        })
                                      }}
                                    >
                                      <Icon path={mdiTrashCanOutline} size={0.78} />
                                    </ActionIcon>
                                  </Tooltip>
                                </Group>
                              ) : null}
                            </article>
                          ))}
                        </div>
                        <section className={`yy-team-requests-panel ${selectedIsCaptain ? '' : 'is-readonly'}`}>
                          <Group justify="space-between" align="center" className="yy-team-subhead">
                            <div>
                              <span className="yy-section-kicker">JOIN REQUESTS</span>
                              <Title order={3}>入队申请</Title>
                            </div>
                            <Badge className="yy-team-role-badge">{selectedIsCaptain ? pendingRequests.length : '只读'}</Badge>
                          </Group>
                          {selectedIsCaptain ? (
                            requestsLoading ? (
                              <Text size="sm" className="yy-readable-text">
                                正在读取入队申请...
                              </Text>
                            ) : pendingRequests.length > 0 ? (
                              <div className="yy-team-request-list">
                                {pendingRequests.map((request) => (
                                  <article className="yy-team-request-row" key={request.id}>
                                    <Avatar src={request.user.avatar} alt={request.user.userName ?? 'user'} radius="xl" size={38}>
                                      {request.user.userName?.slice(0, 1) ?? 'U'}
                                    </Avatar>
                                    <div className="yy-team-roster-user">
                                      <strong>{request.user.userName ?? 'user'}</strong>
                                      <span>{request.message || request.user.bio || '申请加入队伍'}</span>
                                    </div>
                                    <Group gap={5} wrap="nowrap">
                                      <ActionIcon
                                        variant="light"
                                        color="teal"
                                        loading={working}
                                        aria-label="同意入队"
                                        onClick={() => void onReviewRequest(request, true)}
                                      >
                                        <Icon path={mdiAccountCheck} size={0.8} />
                                      </ActionIcon>
                                      <ActionIcon
                                        variant="light"
                                        color="red"
                                        loading={working}
                                        aria-label="拒绝入队"
                                        onClick={() => void onReviewRequest(request, false)}
                                      >
                                        <Icon path={mdiAccountCancel} size={0.8} />
                                      </ActionIcon>
                                    </Group>
                                  </article>
                                ))}
                              </div>
                            ) : (
                              <Text size="sm" className="yy-readable-text">
                                暂无待处理入队申请。
                              </Text>
                            )
                          ) : (
                            <div className="yy-team-request-list yy-team-request-list-readonly">
                              <article className="yy-team-request-row yy-team-request-row-muted">
                                <Avatar radius="xl" size={38} className="yy-team-ghost-avatar">
                                  <Icon path={mdiAccountCheck} size={0.86} />
                                </Avatar>
                                <div className="yy-team-roster-user">
                                  <strong>队长审核</strong>
                                  <span>入队申请由队长统一处理，成员可查看队伍资料与成员列表。</span>
                                </div>
                                <Badge className="yy-team-role-badge">成员视图</Badge>
                              </article>
                            </div>
                          )}
                        </section>
                      </section>
                    </div>

                    <div className="yy-team-info-strip">
                      <article>
                        <span>队伍签名</span>
                        {selectedIsCaptain ? (
                          <Textarea
                            value={teamDraft.bio}
                            placeholder="填写队伍签名"
                            autosize
                            minRows={2}
                            maxRows={3}
                            onChange={(event) => setTeamDraft((current) => ({ ...current, bio: event.currentTarget.value }))}
                            className="yy-team-inline-input"
                          />
                        ) : (
                          <strong>{selectedTeam?.bio || '暂无队伍签名'}</strong>
                        )}
                      </article>
                      <article>
                        <span>队长</span>
                        <strong>{selectedCaptain?.userName ?? '-'}</strong>
                      </article>
                      <article>
                        <span>邀请码</span>
                        {selectedIsCaptain ? (
                          <Group gap="xs" wrap="nowrap" className="yy-team-invite-control">
                            <TextInput
                              ref={inviteInputRef}
                              value={inviteCode}
                              readOnly
                              className="yy-team-inline-input"
                              onFocus={(event) => event.currentTarget.select()}
                            />
                            <ActionIcon
                              variant="light"
                              aria-label="复制邀请码"
                              onClick={() => void onCopyInviteCode()}
                            >
                              <Icon path={mdiContentCopy} size={0.82} />
                            </ActionIcon>
                            <ActionIcon variant="light" loading={working} aria-label="刷新邀请码" onClick={onRefreshInviteCode}>
                              <Icon path={mdiRefresh} size={0.82} />
                            </ActionIcon>
                          </Group>
                        ) : (
                          <strong>仅队长可查看</strong>
                        )}
                      </article>
                      <article>
                        <span>入队申请</span>
                        <strong>{selectedIsCaptain ? `${pendingRequests.length} 个待处理申请` : '由队长统一处理'}</strong>
                      </article>
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

        <Modal
          opened={joinOpened}
          title={t('team.button.join')}
          size="lg"
          onClose={() => {
            setJoinOpened(false)
            setJoinResults([])
            setJoinSearch('')
            setJoinMessage('')
          }}
        >
          <YinyuModalBody>
            <section className="yy-team-join-section">
              <Text fw={900}>邀请码加入</Text>
              <Text size="sm" className="yy-readable-text">
                已获得队长邀请码时，可以直接加入队伍。
              </Text>
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
            </section>

            <section className="yy-team-join-section">
              <Text fw={900}>申请加入队伍</Text>
              <Text size="sm" className="yy-readable-text">
                未获得邀请码时，可以搜索队伍并提交入队申请，由队长审核。
              </Text>
              <Group align="flex-end" gap="sm" wrap="nowrap">
                <TextInput
                  label="搜索队伍"
                  placeholder="输入队伍名称或 ID"
                  value={joinSearch}
                  onChange={(event) => setJoinSearch(event.currentTarget.value)}
                  onKeyDown={(event) => {
                    if (event.key === 'Enter') void onSearchJoinTeams()
                  }}
                  leftSection={<Icon path={mdiMagnify} size={0.86} />}
                  className="yy-team-join-search"
                />
                <Button
                  className="yy-team-action yy-team-action-create"
                  loading={joinLoading}
                  onClick={onSearchJoinTeams}
                >
                  搜索
                </Button>
              </Group>
              <Textarea
                label="申请说明"
                placeholder="简要说明身份或参赛意向"
                value={joinMessage}
                autosize
                minRows={2}
                maxRows={3}
                onChange={(event) => setJoinMessage(event.currentTarget.value)}
              />
              <div className="yy-team-join-results">
                {joinResults.map((team) => {
                  const joined = teams?.some((item) => item.id === team.id)

                  return (
                    <article className="yy-team-request-row" key={team.id ?? team.name}>
                      <Avatar src={team.avatar} alt={team.name ?? 'team'} radius="xl" size={40}>
                        {team.name?.slice(0, 1) ?? 'T'}
                      </Avatar>
                      <div className="yy-team-roster-user">
                        <strong>{team.name ?? 'team'}</strong>
                        <span>{team.bio || `${team.members?.length ?? 0} 名成员`}</span>
                      </div>
                      <Button
                        size="xs"
                        leftSection={<Icon path={mdiSend} size={0.76} />}
                        disabled={joined}
                        loading={joinLoading}
                        onClick={() => void onCreateJoinRequest(team)}
                      >
                        {joined ? '已加入' : '提交申请'}
                      </Button>
                    </article>
                  )
                })}
              </div>
            </section>
          </YinyuModalBody>
        </Modal>

        <TeamCreateModal
          opened={createOpened}
          title={t('team.button.create')}
          disallowCreate={disallowCreate}
          onClose={() => setCreateOpened(false)}
          mutate={mutateTeams}
        />

      </WithRole>
    </WithNavBar>
  )
}

export default Teams
