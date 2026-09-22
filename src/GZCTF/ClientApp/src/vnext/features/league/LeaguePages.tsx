import { ArrowLeft, Plus, RefreshCw, Swords } from 'lucide-react'
import { Link, useLocation, useNavigate, useParams, useSearchParams } from 'react-router'
import { ActionButton, InlineFeedback } from '../../shared/Interaction'
import { DataState, PageHeading, StatusPill } from '../../shared/Primitives'
import { useVNextPageTitle } from '../../shared/useVNextPageTitle'
import styles from './League.module.css'
import { LeagueDraftForm } from './LeagueDraftForm'
import { LeagueGate } from './LeagueGate'
import { LeagueRegistrations } from './LeagueRegistrations'
import { leagueApi } from './api/leagueApi'
import { leagueError, matchStateLabel } from './leaguePresentation'
import { useLeagueDetail, useLeagueIdentity, useLeagueList, useLeagueMutation } from './useLeagueController'

const uuid = /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i

export function LeagueListPage() {
  useVNextPageTitle('数字攻防联赛')
  const account = useLeagueIdentity()
  return (
    <div className={styles.page}>
      <LeagueGate account={account}>
        <LeagueList key={account.identity} />
      </LeagueGate>
    </div>
  )
}

function LeagueList() {
  const [params, setParams] = useSearchParams()
  const cursors = params.getAll('after').filter((value) => uuid.test(value))
  const { account, request } = useLeagueList(cursors.at(-1))
  const rows = request.data?.items ?? []
  const turn = (next: string[]) => {
    const query = new URLSearchParams()
    next.forEach((value) => query.append('after', value))
    setParams(query)
  }
  return (
    <>
      <PageHeading
        eyebrow="LEAGUE / PHASE 01"
        title="数字攻防联赛"
        description="双队对抗，从报名与场景配置开始。"
        actions={
          <div className={styles.actions}>
            <ActionButton
              icon={<RefreshCw size={16} />}
              disabled={request.isValidating}
              onClick={() => void request.mutate()}
            >
              刷新
            </ActionButton>
            {account.isAdmin && request.data && !request.error ? (
              <Link className={styles.primaryLink} to="/league/new">
                <Plus size={16} />
                创建场次
              </Link>
            ) : null}
          </div>
        }
      />
      <div className={styles.intro}>
        <Swords size={24} aria-hidden="true" />
        <div>
          <strong>报名 → 审核 → 双队配置</strong>
          <p>由管理员选定场景版本与参赛席位。开赛、核心 Flag 提交和金币账务将在后续接入。</p>
        </div>
      </div>
      {request.error ? <InlineFeedback tone="danger">{leagueError(request.error)}</InlineFeedback> : null}
      {request.isLoading ? (
        <DataState loading title="正在读取场次" />
      ) : !request.error && !rows.length ? (
        <DataState
          title="暂无联赛场次"
          description={account.isAdmin ? '创建首个场次，开放战队报名。' : '管理员创建场次后，即可在此查看并报名。'}
        />
      ) : null}
      {request.isValidating && request.data ? <p role="status">正在刷新场次…</p> : null}
      <div className={styles.matches}>
        {rows.map((match) => (
          <Link
            key={match.id}
            className={styles.matchCard}
            to={`/league/${match.id}`}
            state={{ listSearch: params.toString() }}
          >
            <div className={styles.cardTop}>
              <span>双队场次</span>
              <StatusPill>{matchStateLabel(match)}</StatusPill>
            </div>
            <h2>{match.name}</h2>
            <p>{match.createdAt ? new Date(match.createdAt).toLocaleString('zh-CN') : '创建时间待确认'}</p>
            <span>查看详情 →</span>
          </Link>
        ))}
      </div>
      <nav className={styles.actions} aria-label="联赛分页">
        <ActionButton disabled={!cursors.length || request.isValidating} onClick={() => turn(cursors.slice(0, -1))}>
          上一页
        </ActionButton>
        <span>第 {cursors.length + 1} 页</span>
        <ActionButton
          disabled={!request.data?.nextCursor || request.isValidating || Boolean(request.error)}
          onClick={() => {
            if (request.data?.nextCursor) turn([...cursors, request.data.nextCursor])
          }}
        >
          下一页
        </ActionButton>
      </nav>
    </>
  )
}

export function LeagueCreatePage() {
  useVNextPageTitle('创建联赛场次')
  const account = useLeagueIdentity()
  return (
    <div className={styles.page}>
      <Link to="/league" className={styles.back}>
        <ArrowLeft size={16} />
        联赛列表
      </Link>
      <LeagueGate account={account} admin>
        <LeagueCreate key={account.identity} />
      </LeagueGate>
    </div>
  )
}

function LeagueCreate() {
  const { account, request } = useLeagueList()
  const action = useLeagueMutation()
  const navigate = useNavigate()
  if (request.error)
    return (
      <>
        <InlineFeedback tone="danger">{leagueError(request.error)}</InlineFeedback>
        <ActionButton onClick={() => void request.mutate()}>重试</ActionButton>
      </>
    )
  if (!request.data) return <DataState loading title="正在确认联赛可用状态" />
  return (
    <>
      <PageHeading
        eyebrow="NEW MATCH"
        title="创建联赛场次"
        description="创建后所有已登录用户均可在联赛列表中看到此场次。"
      />
      {action.feedback ? (
        <InlineFeedback tone={action.feedback.failed ? 'danger' : 'success'}>{action.feedback.text}</InlineFeedback>
      ) : null}
      <LeagueDraftForm
        identity={account.identity!}
        busy={action.busy}
        onSave={(draft) =>
          action.run(async () => {
            const result = await leagueApi.create(draft)
            navigate(`/league/${result.match!.id}`, { replace: true })
            return result
          })
        }
      />
    </>
  )
}

export function LeagueDetailPage() {
  const { matchId = '' } = useParams()
  const account = useLeagueIdentity()
  useVNextPageTitle('联赛详情')
  return (
    <div className={styles.page}>
      <LeagueGate account={account}>
        {uuid.test(matchId) ? (
          <LeagueDetail key={`${account.identity}:${matchId}`} matchId={matchId} />
        ) : (
          <DataState title="场次地址无效" description="请从联赛列表重新进入。" />
        )}
      </LeagueGate>
    </div>
  )
}

function LeagueDetail({ matchId }: { matchId: string }) {
  const controller = useLeagueDetail(matchId)
  const { account, request, teams, action, write } = controller
  const detail = request.data
  const location = useLocation()
  const search = (location.state as { listSearch?: string } | null)?.listSearch
  return (
    <>
      <Link to={`/league${search ? `?${search}` : ''}`} className={styles.back}>
        <ArrowLeft size={16} />
        联赛列表
      </Link>
      <PageHeading
        eyebrow="MATCH / REGISTRATION"
        title={detail?.match?.name ?? '联赛详情'}
        description="报名、审核和场景配置以服务器保存的结果为准。"
        actions={
          <ActionButton
            icon={<RefreshCw size={16} />}
            disabled={request.isValidating || action.busy}
            onClick={() => {
              void request.mutate()
              void teams.mutate()
            }}
          >
            刷新详情
          </ActionButton>
        }
      />
      {request.error ? <InlineFeedback tone="danger">{leagueError(request.error)}</InlineFeedback> : null}
      {request.isLoading ? <DataState loading title="正在读取场次详情" /> : null}
      {action.feedback ? (
        <InlineFeedback tone={action.feedback.failed ? 'danger' : 'success'}>{action.feedback.text}</InlineFeedback>
      ) : null}
      {detail ? (
        <>
          {request.isValidating ? <p role="status">正在刷新详情…</p> : null}
          <div className={styles.summary}>
            <StatusPill>{matchStateLabel(detail.match ?? {})}</StatusPill>
            <span>
              初始金币 <strong>{detail.initialCoins}</strong> / 队
            </span>
            <span>配置版本 {detail.match?.revision}</span>
          </div>
          {teams.error ? (
            <InlineFeedback tone="danger">战队列表读取失败：{leagueError(teams.error)}</InlineFeedback>
          ) : null}
          {teams.isLoading ? <p role="status">正在读取我的战队…</p> : null}
          <div className={styles.detailGrid}>
            <LeagueRegistrations
              detail={detail}
              admin={account.isAdmin}
              userId={account.user!.userId!}
              teams={teams.data ?? []}
              busy={action.busy || Boolean(request.error)}
              register={(teamId) => void write(() => leagueApi.register(matchId, teamId))}
              review={(teamId, approved) => void write(() => leagueApi.review(matchId, teamId, approved))}
              select={(revision, first, second) => write(() => leagueApi.selectTeams(matchId, revision, first, second))}
            />
            {account.isAdmin && detail.allowedActions?.includes('edit') ? (
              <LeagueDraftForm
                identity={account.identity!}
                detail={detail}
                busy={action.busy || Boolean(request.error)}
                onSave={(draft, revision) => write(() => leagueApi.update(matchId, revision!, draft))}
              />
            ) : (
              <section className={styles.panel}>
                <h2>场景配置</h2>
                <p>{detail.releaseId ? '本场已配置固定场景版本。' : '管理员尚未配置场景版本。'}</p>
                <p>初始金币：{detail.initialCoins} / 队</p>
                <p>当前阶段不开放配置修改。</p>
              </section>
            )}
          </div>
        </>
      ) : null}
    </>
  )
}
