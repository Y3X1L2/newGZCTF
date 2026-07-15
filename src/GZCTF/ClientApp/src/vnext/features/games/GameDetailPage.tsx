import { ArrowLeft, ArrowRight, Clock3, ShieldCheck, Users } from 'lucide-react'
import { FormEvent, useEffect, useMemo, useState } from 'react'
import { Link, useParams } from 'react-router'
import api, { GameType, NoticeType, ParticipationStatus } from '@Api'
import { ActionButton, InlineFeedback, VNextConfirmDialog, VNextDialog } from '../../shared/Interaction'
import { MarkdownContent } from '../../shared/MarkdownContent'
import { DataState, GeometricPoster, StatusPill } from '../../shared/Primitives'
import { errorMessage } from '../../shared/errors'
import { useVNextPageTitle } from '../../shared/useVNextPageTitle'
import { useCurrentAccount } from '../account/useCurrentAccount'
import styles from './GameDetailPage.module.css'
import { gameStatusLabel, gameStatusTone, participationLabel } from './gameCatalog'
import { gameModulesFor } from './gameModules'

function gameStatus(start?: number, end?: number) {
  const now = Date.now()
  if (start && now < start) return 'upcoming' as const
  if (end && now >= end) return 'ended' as const
  return 'ongoing' as const
}

function formatFullDate(value?: number) {
  if (!value) return '未设置'
  return new Intl.DateTimeFormat('zh-CN', {
    year: 'numeric',
    month: '2-digit',
    day: '2-digit',
    hour: '2-digit',
    minute: '2-digit',
    hour12: false,
  }).format(value)
}

function durationLabel(target?: number, now = Date.now()) {
  if (!target) return ''
  const seconds = Math.max(0, Math.floor((target - now) / 1000))
  const days = Math.floor(seconds / 86400)
  const hours = Math.floor((seconds % 86400) / 3600)
  const minutes = Math.floor((seconds % 3600) / 60)
  const rest = seconds % 60
  if (days) return `${days} 天 ${hours} 小时`
  if (hours) return `${hours} 小时 ${minutes} 分`
  return `${minutes} 分 ${String(rest).padStart(2, '0')} 秒`
}

function typeLabel(type?: GameType) {
  if (type === GameType.Theory) return '理论考试'
  if (type === GameType.AWDP) return 'AWDP'
  if (type === GameType.Penetration) return '渗透演练'
  if (type === GameType.Mixed) return '混合赛制'
  return 'CTF'
}

function participationStatus(status?: ParticipationStatus) {
  switch (status) {
    case ParticipationStatus.Accepted:
      return { label: '已通过', tone: 'success' as const }
    case ParticipationStatus.Pending:
      return { label: '待审核', tone: 'warning' as const }
    case ParticipationStatus.Rejected:
      return { label: '已拒绝', tone: 'neutral' as const }
    case ParticipationStatus.Suspended:
      return { label: '已暂停', tone: 'warning' as const }
    default:
      return { label: '未报名', tone: 'neutral' as const }
  }
}

function noticeText(type: NoticeType, values: string[]) {
  const last = values.at(-1) || '比赛状态已更新'
  if (type === NoticeType.NewChallenge) return `新题目已开放：${last}`
  if (type === NoticeType.NewHint) return `新提示已发布：${last}`
  if (type === NoticeType.FirstBlood) return `一血产生：${values.join(' · ')}`
  if (type === NoticeType.SecondBlood) return `二血产生：${values.join(' · ')}`
  if (type === NoticeType.ThirdBlood) return `三血产生：${values.join(' · ')}`
  return last
}

export function GameDetailPage() {
  const { gameId = '' } = useParams()
  const id = Number(gameId)
  const validId = Number.isInteger(id) && id > 0
  const account = useCurrentAccount()
  const { data: game, error, mutate } = api.game.useGameGame(id, { revalidateOnFocus: false }, validId)
  const { data: teams } = api.team.useTeamGetTeamsInfo({ revalidateOnFocus: false }, account.isAuthenticated)
  const { data: notices } = api.game.useGameNotices(id, { count: 5, skip: 0 }, { revalidateOnFocus: false }, validId)
  const [joinOpen, setJoinOpen] = useState(false)
  const [leaveConfirmOpen, setLeaveConfirmOpen] = useState(false)
  const { data: checkInfo, mutate: mutateCheck } = api.game.useGameGetGameJoinCheckInfo(
    id,
    { revalidateOnFocus: false },
    validId && joinOpen && account.isAuthenticated
  )
  const [teamId, setTeamId] = useState<number | null>(null)
  const [divisionId, setDivisionId] = useState<number | null>(null)
  const [inviteCode, setInviteCode] = useState('')
  const [submitting, setSubmitting] = useState(false)
  const [feedback, setFeedback] = useState<{ tone: 'success' | 'danger'; message: string } | null>(null)
  const [now, setNow] = useState(Date.now())

  const status = gameStatus(game?.start, game?.end)
  const participation = participationStatus(game?.status)
  const modules = gameModulesFor(game?.gameType)
  const accepted = game?.status === ParticipationStatus.Accepted
  const joinedTeamIds = useMemo(
    () => new Set(checkInfo?.joinedTeams?.map((team) => team.id) ?? []),
    [checkInfo?.joinedTeams]
  )
  const selectableTeams = (teams ?? []).filter((team) => team.id && !joinedTeamIds.has(team.id))
  const divisions = (game?.divisions ?? []).filter(
    (division) =>
      !checkInfo?.joinableDivisions?.length || (division.id && checkInfo.joinableDivisions.includes(division.id))
  )
  const selectedDivision = divisions.find((division) => division.id === divisionId)
  const needsInvite = Boolean(game?.inviteCodeRequired || selectedDivision?.inviteCodeRequired)

  useVNextPageTitle(game?.title || '赛事详情')

  useEffect(() => {
    const target = status === 'upcoming' ? game?.start : status === 'ongoing' ? game?.end : undefined
    if (!target || target <= Date.now()) return undefined
    const update = () => {
      if (!document.hidden) setNow(Date.now())
    }
    const timer = window.setInterval(update, 1000)
    document.addEventListener('visibilitychange', update)
    return () => {
      window.clearInterval(timer)
      document.removeEventListener('visibilitychange', update)
    }
  }, [game?.end, game?.start, status])

  useEffect(() => {
    if (!joinOpen || !checkInfo) return
    if (!teamId || joinedTeamIds.has(teamId)) setTeamId(selectableTeams[0]?.id ?? null)
    if (!divisionId || !divisions.some((division) => division.id === divisionId))
      setDivisionId(divisions[0]?.id ?? null)
  }, [checkInfo, divisionId, divisions, joinOpen, joinedTeamIds, selectableTeams, teamId])

  const joinGame = async (event: FormEvent) => {
    event.preventDefault()
    if (!teamId) return
    setSubmitting(true)
    setFeedback(null)
    try {
      await api.game.gameJoinGame(id, { teamId, divisionId, inviteCode: inviteCode.trim() || null })
      await Promise.all([mutate(), mutateCheck()])
      setJoinOpen(false)
      setInviteCode('')
      setFeedback({ tone: 'success', message: '报名信息已提交，当前状态已刷新。' })
    } catch (requestError) {
      setFeedback({ tone: 'danger', message: errorMessage(requestError, '比赛报名失败。') })
    } finally {
      setSubmitting(false)
    }
  }

  const leaveGame = async () => {
    setSubmitting(true)
    setFeedback(null)
    try {
      await api.game.gameLeaveGame(id)
      await mutate()
      setFeedback({ tone: 'success', message: '已撤回当前报名。' })
      return true
    } catch (requestError) {
      setFeedback({ tone: 'danger', message: errorMessage(requestError, '撤回报名失败。') })
      return false
    } finally {
      setSubmitting(false)
    }
  }

  if (!validId)
    return (
      <div className={styles.statePage}>
        <DataState description="赛事编号格式不正确。" title="无法识别赛事" />
      </div>
    )
  if (!game && !error)
    return (
      <div className={styles.statePage}>
        <DataState description="正在读取比赛规则、时间和报名状态。" loading title="赛事加载中" />
      </div>
    )
  if (!game)
    return (
      <div className={styles.statePage}>
        <DataState description="赛事不存在、已隐藏或当前账户无权访问。" title="赛事加载失败" />
      </div>
    )

  const countdownTarget = status === 'upcoming' ? game.start : status === 'ongoing' ? game.end : undefined
  const countdownLabel = status === 'upcoming' ? '距离开始' : '距离结束'

  return (
    <div className={styles.page}>
      <Link className={styles.backLink} to="/games">
        <ArrowLeft size={16} />
        返回赛事中心
      </Link>

      {feedback ? (
        <div className={styles.pageFeedback}>
          <InlineFeedback tone={feedback.tone}>{feedback.message}</InlineFeedback>
        </div>
      ) : null}

      <section className={styles.hero}>
        <div className={styles.poster}>
          <GeometricPoster
            alt={`${game.title || '赛事'}海报`}
            src={game.poster}
            tone={status === 'upcoming' ? 'blue' : status === 'ended' ? 'neutral' : 'green'}
          />
        </div>
        <div className={styles.heroContent}>
          <div className={styles.badges}>
            <StatusPill tone={gameStatusTone(status)}>{gameStatusLabel(status)}</StatusPill>
            <StatusPill tone="info">{typeLabel(game.gameType)}</StatusPill>
            {game.practiceMode ? <StatusPill>赛后练习开放</StatusPill> : null}
          </div>
          <h1>{game.title || `赛事 ${id}`}</h1>
          <p>{game.summary || '赛事简介尚未填写。'}</p>
          <div className={styles.heroMeta}>
            <span>
              <Clock3 size={16} />
              {formatFullDate(game.start)} 至 {formatFullDate(game.end)}
            </span>
            <span>
              <Users size={16} />
              {participationLabel(game.limit)} · {game.teamCount ?? 0} 支队伍
            </span>
          </div>
        </div>
      </section>

      <div className={styles.layout}>
        <main className={styles.mainContent}>
          {(accepted || status === 'ended') && modules.length ? (
            <section className={styles.moduleSection}>
              <header>
                <span>COMPETITION MODULES</span>
                <h2>{status === 'ended' ? '比赛结果与赛后入口' : '进入比赛'}</h2>
              </header>
              <div className={styles.moduleGrid}>
                {modules.map((module) => {
                  const Icon = module.icon
                  return (
                    <Link className={styles.moduleCard} key={module.id} to={`/games/${id}/${module.id}`}>
                      <span className={styles.moduleIcon}>
                        <Icon size={20} />
                      </span>
                      <span>
                        <strong>{module.label}</strong>
                        <small>{module.description}</small>
                      </span>
                      <ArrowRight size={17} />
                    </Link>
                  )
                })}
              </div>
            </section>
          ) : null}

          <section className={styles.rulesHeader}>
            <span>RULES & BRIEFING</span>
            <h2>比赛说明</h2>
          </section>
          <article className={styles.rules}>
            <MarkdownContent source={game.content || game.summary || '暂无比赛说明。'} />
          </article>
        </main>

        <aside className={styles.sidebar}>
          <section className={styles.participationPanel}>
            <div className={styles.panelHeader}>
              <span>PARTICIPATION</span>
              <StatusPill tone={participation.tone}>{participation.label}</StatusPill>
            </div>
            {game.teamName ? (
              <div className={styles.currentTeam}>
                <ShieldCheck size={18} />
                <span>
                  <small>参赛战队</small>
                  <strong>{game.teamName}</strong>
                </span>
              </div>
            ) : null}
            {countdownTarget && countdownTarget > now ? (
              <div className={styles.countdown}>
                <span>{countdownLabel}</span>
                <strong>{durationLabel(countdownTarget, now)}</strong>
              </div>
            ) : null}
            <div className={styles.primaryAction}>
              {!account.isAuthenticated ? (
                <Link className={styles.primaryLink} to={`/account/login?returnUrl=/games/${id}`}>
                  登录后报名
                  <ArrowRight size={16} />
                </Link>
              ) : null}
              {account.isAuthenticated &&
              (!game.status ||
                game.status === ParticipationStatus.Unsubmitted ||
                game.status === ParticipationStatus.Rejected) &&
              status !== 'ended' ? (
                <ActionButton onClick={() => setJoinOpen(true)} tone="primary" type="button">
                  {game.status === ParticipationStatus.Rejected ? '重新报名' : '报名参赛'}
                </ActionButton>
              ) : null}
              {game.status === ParticipationStatus.Pending ? (
                <>
                  <p>报名正在等待管理员审核，通过后会开放比赛工作区。</p>
                  <ActionButton disabled={submitting} onClick={() => setLeaveConfirmOpen(true)} type="button">
                    撤回报名
                  </ActionButton>
                </>
              ) : null}
              {game.status === ParticipationStatus.Accepted && status === 'upcoming' ? (
                <p>报名已通过，比赛开始后将自动开放对应工作区。</p>
              ) : null}
              {game.status === ParticipationStatus.Accepted && status === 'ongoing' && modules[0] ? (
                <Link className={styles.primaryLink} to={`/games/${id}/${modules[0].id}`}>
                  进入{modules[0].label}
                  <ArrowRight size={16} />
                </Link>
              ) : null}
              {game.status === ParticipationStatus.Suspended ? <p>当前参赛资格已暂停，请联系比赛管理员。</p> : null}
              {status === 'ended' && !accepted ? <p>比赛已结束，可查看公开结果或管理员允许的赛后内容。</p> : null}
            </div>
          </section>

          <section className={styles.noticePanel}>
            <header>
              <span>GAME NOTICE</span>
              <h2>比赛公告</h2>
            </header>
            <div>
              {(notices ?? []).slice(0, 5).map((notice) => (
                <article key={notice.id}>
                  <p>{noticeText(notice.type, notice.values)}</p>
                  <time>{formatFullDate(notice.time)}</time>
                </article>
              ))}
              {notices && !notices.length ? <p className={styles.emptyNotice}>暂无比赛公告。</p> : null}
              {!notices ? <p className={styles.emptyNotice}>正在读取公告...</p> : null}
            </div>
          </section>
        </aside>
      </div>

      <VNextDialog
        description="先选择参赛战队和赛区；提交后可能需要管理员审核。"
        eyebrow="GAME REGISTRATION"
        footer={
          <>
            <ActionButton onClick={() => setJoinOpen(false)} type="button">
              取消
            </ActionButton>
            <ActionButton
              disabled={submitting || !teamId || (needsInvite && !inviteCode.trim())}
              form="vnext-game-join-form"
              tone="primary"
              type="submit"
            >
              提交报名
            </ActionButton>
          </>
        }
        onClose={() => setJoinOpen(false)}
        open={joinOpen}
        title="报名参赛"
      >
        {!checkInfo ? (
          <DataState description="正在检查可用战队和赛区。" loading title="报名检查中" />
        ) : (
          <form className={styles.joinForm} id="vnext-game-join-form" onSubmit={joinGame}>
            {!selectableTeams.length ? (
              <InlineFeedback tone="neutral">
                没有可用于报名的战队。请先前往战队页面创建或加入战队。<Link to="/teams">打开战队协作</Link>
              </InlineFeedback>
            ) : (
              <label>
                <span>参赛战队</span>
                <select onChange={(event) => setTeamId(Number(event.currentTarget.value) || null)} value={teamId ?? ''}>
                  {selectableTeams.map((team) => (
                    <option key={team.id} value={team.id}>
                      {team.name}（{team.members?.length ?? 0} 人）
                    </option>
                  ))}
                </select>
              </label>
            )}
            {divisions.length ? (
              <label>
                <span>参赛赛区</span>
                <select
                  onChange={(event) => {
                    setDivisionId(Number(event.currentTarget.value) || null)
                    setInviteCode('')
                  }}
                  value={divisionId ?? ''}
                >
                  {divisions.map((division) => (
                    <option key={division.id} value={division.id}>
                      {division.name || `赛区 ${division.id}`}
                    </option>
                  ))}
                </select>
              </label>
            ) : (
              <InlineFeedback tone="neutral">本比赛使用默认赛区。</InlineFeedback>
            )}
            {needsInvite ? (
              <label>
                <span>比赛或赛区邀请码</span>
                <input
                  autoComplete="off"
                  onChange={(event) => setInviteCode(event.currentTarget.value)}
                  required
                  value={inviteCode}
                />
              </label>
            ) : null}
          </form>
        )}
      </VNextDialog>
      <VNextConfirmDialog
        confirmLabel="撤回报名"
        message="撤回后当前参赛资格会被取消；如比赛仍开放报名，可以重新提交。"
        onClose={() => setLeaveConfirmOpen(false)}
        onConfirm={leaveGame}
        open={leaveConfirmOpen}
        title="确认撤回当前比赛报名？"
      />
    </div>
  )
}
