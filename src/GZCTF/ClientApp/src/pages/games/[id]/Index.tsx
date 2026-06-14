import { Alert, Anchor, Badge, Button, Group, Image, Stack, Text, Title, useMantineTheme } from '@mantine/core'
import { useModals } from '@mantine/modals'
import { showNotification } from '@mantine/notifications'
import { mdiAlertCircle, mdiCheck, mdiTimerSand } from '@mdi/js'
import { Icon } from '@mdi/react'
import { FC, useEffect, useState } from 'react'
import { Trans, useTranslation } from 'react-i18next'
import { Link, useNavigate, useParams } from 'react-router'
import { GameJoinModal } from '@Components/GameJoinModal'
import { GameProgress } from '@Components/GameProgress'
import { Markdown } from '@Components/MarkdownRenderer'
import { WithNavBar } from '@Components/WithNavbar'
import { BrandMark } from '@Components/yinyu/BrandMark'
import { YinyuGridScan, YinyuStatusText } from '@Components/yinyu/YinyuReactBits'
import { useLanguage } from '@Utils/I18n'
import { showErrorMsg } from '@Utils/Shared'
import { useIsMobile } from '@Utils/ThemeOverride'
import { getGameStatus, useGame } from '@Hooks/useGame'
import { usePageTitle } from '@Hooks/usePageTitle'
import { useTeams, useUser } from '@Hooks/useUser'
import api, { GameJoinModel, GameType, ParticipationStatus } from '@Api'

const GetAlert = (status: ParticipationStatus, team: string) => {
  const { t } = useTranslation()

  const GameAlertMap = new Map([
    [
      ParticipationStatus.Pending,
      {
        color: 'yellow',
        icon: mdiTimerSand,
        title: t('game.participation.alert.pending.title', { team }),
        content: t('game.participation.alert.pending.content'),
      },
    ],
    [ParticipationStatus.Accepted, null],
    [
      ParticipationStatus.Rejected,
      {
        color: 'red',
        icon: mdiAlertCircle,
        title: t('game.participation.alert.rejected.title'),
        content: t('game.participation.alert.rejected.content'),
      },
    ],
    [
      ParticipationStatus.Suspended,
      {
        color: 'red',
        icon: mdiAlertCircle,
        title: t('game.participation.alert.suspended.title', { team }),
        content: t('game.participation.alert.suspended.content'),
      },
    ],
    [ParticipationStatus.Unsubmitted, null],
  ])

  const data = GameAlertMap.get(status)
  if (data) {
    return (
      <Alert color={data.color} icon={<Icon path={data.icon} />} title={data.title}>
        {data.content}
      </Alert>
    )
  }
  return null
}

const GameDetail: FC = () => {
  const { id } = useParams()
  const numId = parseInt(id ?? '-1')
  const navigate = useNavigate()

  const { game, error, mutate, status } = useGame(numId)
  const theme = useMantineTheme()
  const { startTime, endTime, finished, started, progress } = getGameStatus(game)
  const { locale } = useLanguage()
  const { user } = useUser()
  const { teams } = useTeams()
  const modals = useModals()
  const isMobile = useIsMobile()
  const { t } = useTranslation()

  usePageTitle(game?.title)

  useEffect(() => {
    if (error) {
      showErrorMsg(error, t)
      navigate('/games')
    }
  }, [error, navigate])

  const [joinModalOpen, setJoinModalOpen] = useState(false)

  const GameActionMap = new Map([
    [ParticipationStatus.Pending, t('game.participation.actions.pending')],
    [ParticipationStatus.Accepted, t('game.participation.actions.accepted')],
    [ParticipationStatus.Rejected, t('game.participation.actions.rejected')],
    [ParticipationStatus.Suspended, t('game.participation.actions.suspended')],
    [ParticipationStatus.Unsubmitted, t('game.participation.actions.unsubmitted')],
  ])

  const onSubmitJoin = async (info: GameJoinModel) => {
    try {
      if (!numId) return

      await api.game.gameJoinGame(numId, info)
      showNotification({
        color: 'teal',
        message: t('game.notification.joined'),
        icon: <Icon path={mdiCheck} size={1} />,
      })
      mutate()
    } catch (err) {
      return showErrorMsg(err, t)
    }
  }

  const onSubmitLeave = async () => {
    try {
      if (!numId) return
      await api.game.gameLeaveGame(numId)

      showNotification({
        color: 'teal',
        message: t('game.notification.left'),
        icon: <Icon path={mdiCheck} size={1} />,
      })
      mutate()
    } catch (err) {
      return showErrorMsg(err, t)
    }
  }

  const isGameOpenForJoin = !finished || game?.practiceMode
  const isTheoryOnly = game?.gameType === GameType.Theory
  const isAwdOnly = game?.gameType === GameType.AWDP

  const canSubmit =
    (status === ParticipationStatus.Unsubmitted || status === ParticipationStatus.Rejected) &&
    isGameOpenForJoin &&
    user &&
    teams &&
    teams.length > 0

  const teamRequire =
    user && status === ParticipationStatus.Unsubmitted && isGameOpenForJoin && teams && teams.length === 0

  const onJoin = () =>
    modals.openConfirmModal({
      title: t('game.content.join.confirm'),
      children: (
        <Stack gap="xs">
          <Text size="sm">{t('game.content.join.content.0')}</Text>
          <Text size="sm">
            <Trans i18nKey="game.content.join.content.1" />
          </Text>
          <Text size="sm">
            <Trans i18nKey="game.content.join.content.2" />
          </Text>
        </Stack>
      ),
      onConfirm: () => setJoinModalOpen(true),
      confirmProps: { color: theme.primaryColor },
    })

  const onLeave = () =>
    modals.openConfirmModal({
      title: t('game.content.leave.confirm'),
      children: (
        <Stack gap="xs">
          <Text size="sm">{t('game.content.leave.content.0')}</Text>
          <Text size="sm">{t('game.content.leave.content.1')}</Text>
        </Stack>
      ),
      onConfirm: onSubmitLeave,
      confirmProps: { color: theme.primaryColor },
    })

  const ControlButtons = (
    <>
      <Button className="yy-game-action-button" disabled={!canSubmit} onClick={onJoin}>
        {!isGameOpenForJoin
          ? t('game.button.finished')
          : !user
            ? t('game.button.login_required')
            : GameActionMap.get(status)}
      </Button>
      {started && (
        <Button
          className="yy-game-action-button"
          component={Link}
          to={`/games/${numId}/${isTheoryOnly ? 'theory-scoreboard' : 'scoreboard'}`}
        >
          {isTheoryOnly ? '查看理论榜单' : t('game.button.scoreboard')}
        </Button>
      )}
      {(status === ParticipationStatus.Pending || status === ParticipationStatus.Rejected) && (
        <Button className="yy-game-action-button" color="red" variant="outline" onClick={onLeave}>
          {t('game.button.leave')}
        </Button>
      )}
      {status === ParticipationStatus.Accepted && started && !isMobile && (!finished || game?.practiceMode) && (
        <Button
          className="yy-game-action-button"
          component={Link}
          to={`/games/${numId}/${isTheoryOnly ? 'theory' : isAwdOnly ? 'awdp' : 'challenges'}`}
        >
          {isTheoryOnly ? '进入理论考试' : isAwdOnly ? t('game.tab.awd') : t('game.button.challenges')}
        </Button>
      )}
    </>
  )

  const statusText = started && !finished ? '进行中' : finished ? '已结束' : '未开始'
  const hasIntro = Boolean(game?.content?.trim())

  return (
    <WithNavBar width="min(100%, calc(100vw - 7.25rem))" isLoading={!game} minWidth={0}>
      <section className="yy-page-frame yy-game-detail-page yy-game-entry-page">
        <header className="panel-card yy-game-detail-hero yy-game-entry-hero">
          <YinyuGridScan className="yy-game-entry-gridscan" linesColor="#245A46" scanColor="#72F1B8" />
          <Stack gap="md" className="yy-game-detail-copy yy-game-entry-copy">
            <Group gap="xs" className="yy-game-detail-kicker">
              <YinyuStatusText tone={started && !finished ? 'success' : finished ? 'neutral' : 'warm'}>
                {statusText}
              </YinyuStatusText>
              <Badge variant="outline">
                {!game || game.limit === 0
                  ? t('game.tag.multiplayer')
                  : game.limit === 1
                    ? t('game.tag.individual')
                    : t('game.tag.limited', { count: game.limit })}
              </Badge>
              {game?.hidden && <Badge variant="outline">{t('game.tag.hidden')}</Badge>}
            </Group>
            <Title order={1}>{game?.title}</Title>
            <div className="yy-game-detail-emblem" aria-hidden="true" data-has-poster={game?.poster ? 'true' : undefined}>
              {game?.poster ? (
                <Image src={game.poster} alt="" fit="contain" className="yy-game-detail-poster" />
              ) : (
                <BrandMark className="yy-game-detail-brand" />
              )}
            </div>
            <Text className="yy-readable-text">
              <Trans i18nKey="game.content.joined_status" values={{ count: game?.teamCount ?? 0 }} />
            </Text>
            <div className="yy-game-time-grid">
              <div>
                <span>{t('game.content.start_time')}</span>
                <strong>{startTime.locale(locale).format('LLL')}</strong>
              </div>
              <div>
                <span>{t('game.content.end_time')}</span>
                <strong>{endTime.locale(locale).format('LLL')}</strong>
              </div>
            </div>
            <GameProgress percentage={progress} />
            <Stack gap="xs" className="yy-game-detail-alerts">
              {GetAlert(status, game?.teamName ?? '')}
              {teamRequire && (
                <Alert
                  color="yellow"
                  icon={<Icon path={mdiAlertCircle} />}
                  title={t('game.participation.alert.team_required.title')}
                >
                  <Trans i18nKey="game.participation.alert.team_required.content">
                    _
                    <Anchor component={Link} size="sm" to="/teams">
                      _
                    </Anchor>
                    _
                  </Trans>
                </Alert>
              )}
              {status === ParticipationStatus.Accepted && !started && (
                <Alert color="teal" icon={<Icon path={mdiCheck} />} title={t('game.participation.alert.not_started.title')}>
                  {t('game.participation.alert.not_started.content', {
                    team: game?.teamName ?? '',
                  })}
                  {isMobile && t('game.participation.alert.not_started.mobile')}
                </Alert>
              )}
            </Stack>
            {hasIntro && (
              <div className="yy-game-detail-intro">
                <Markdown source={game?.content ?? ''} />
              </div>
            )}
            <Group className="yy-game-detail-actions">{ControlButtons}</Group>
          </Stack>
        </header>
        <GameJoinModal
          title={t('game.content.join.title')}
          opened={joinModalOpen}
          withCloseButton={false}
          onClose={() => setJoinModalOpen(false)}
          onSubmitJoin={onSubmitJoin}
        />
      </section>
    </WithNavBar>
  )
}

export default GameDetail
