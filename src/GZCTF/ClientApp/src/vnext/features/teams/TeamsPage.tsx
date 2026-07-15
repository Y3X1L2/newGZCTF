import {
  Check,
  ChevronLeft,
  Clipboard,
  Crown,
  LogOut,
  Plus,
  RefreshCw,
  Search,
  Settings,
  Trash2,
  UserPlus,
  X,
} from 'lucide-react'
import { useEffect, useState } from 'react'
import { useSearchParams } from 'react-router'
import api, { TeamJoinRequestStatus } from '@Api'
import { ActionButton, InlineFeedback } from '../../shared/Interaction'
import { DataState, PageHeading, StatusPill } from '../../shared/Primitives'
import { errorMessage } from '../../shared/errors'
import { useVNextPageTitle } from '../../shared/useVNextPageTitle'
import { useCurrentAccount } from '../account/useCurrentAccount'
import { MemberAvatar, TeamAvatar } from './TeamAvatar'
import { TeamConfirmation, TeamConfirmationDialog } from './TeamConfirmationDialog'
import { TeamDialogs } from './TeamDialogs'
import styles from './TeamsPage.module.css'

type TeamTab = 'overview' | 'members' | 'requests' | 'settings'
const validTabs = new Set<TeamTab>(['overview', 'members', 'requests', 'settings'])

function parseTeamId(value: string | null) {
  const id = Number(value)
  return Number.isInteger(id) && id > 0 ? id : null
}

export function TeamsPage() {
  const account = useCurrentAccount()
  const {
    data: teams,
    error: teamsError,
    mutate: mutateTeams,
  } = api.team.useTeamGetTeamsInfo({ revalidateOnFocus: false }, account.isAuthenticated)
  const [searchParams, setSearchParams] = useSearchParams()
  const selectedId = parseTeamId(searchParams.get('team'))
  const tabValue = searchParams.get('tab') as TeamTab | null
  const activeTab = tabValue && validTabs.has(tabValue) ? tabValue : 'overview'
  const { data: detailedTeam, mutate: mutateDetailedTeam } = api.team.useTeamGetBasicInfo(
    selectedId ?? 0,
    { revalidateOnFocus: false },
    Boolean(selectedId)
  )
  const selectedTeam = detailedTeam ?? teams?.find((team) => team.id === selectedId)
  const isCaptain = Boolean(
    selectedTeam?.members?.some((member) => member.id === account.user?.userId && member.captain)
  )
  const { data: requests, mutate: mutateRequests } = api.team.useTeamGetJoinRequests(
    selectedId ?? 0,
    { revalidateOnFocus: false },
    Boolean(selectedId && isCaptain && activeTab === 'requests')
  )
  const { data: inviteCode, mutate: mutateInviteCode } = api.team.useTeamInviteCode(
    selectedId ?? 0,
    { revalidateOnFocus: false },
    Boolean(selectedId && isCaptain && activeTab === 'overview')
  )

  const [createOpen, setCreateOpen] = useState(false)
  const [joinOpen, setJoinOpen] = useState(false)
  const [searchOpen, setSearchOpen] = useState(false)
  const [editName, setEditName] = useState('')
  const [editBio, setEditBio] = useState('')
  const [avatarFile, setAvatarFile] = useState<File | null>(null)
  const [submitting, setSubmitting] = useState(false)
  const [mobileDetailOpen, setMobileDetailOpen] = useState(Boolean(selectedId))
  const [confirmation, setConfirmation] = useState<TeamConfirmation | null>(null)
  const [feedback, setFeedback] = useState<{ tone: 'success' | 'danger'; message: string } | null>(null)

  useVNextPageTitle('战队协作')

  useEffect(() => {
    if (!teams?.length) return
    if (selectedId && teams.some((team) => team.id === selectedId)) return
    const next = new URLSearchParams(searchParams)
    next.set('team', String(teams[0].id))
    next.delete('tab')
    setSearchParams(next, { replace: true })
  }, [searchParams, selectedId, setSearchParams, teams])

  useEffect(() => {
    if (!selectedTeam) return
    setEditName(selectedTeam.name ?? '')
    setEditBio(selectedTeam.bio ?? '')
  }, [selectedTeam])

  const setSelectedTeam = (id: number) => {
    const next = new URLSearchParams(searchParams)
    next.set('team', String(id))
    next.delete('tab')
    setSearchParams(next)
    setMobileDetailOpen(true)
  }

  const setTab = (tab: TeamTab) => {
    const next = new URLSearchParams(searchParams)
    if (tab === 'overview') next.delete('tab')
    else next.set('tab', tab)
    setSearchParams(next)
  }

  const refreshTeamData = async () => {
    await Promise.all([mutateTeams(), mutateDetailedTeam(), isCaptain ? mutateRequests() : Promise.resolve()])
  }

  const saveTeam = async () => {
    if (!selectedTeam?.id) return
    setSubmitting(true)
    setFeedback(null)
    try {
      await api.team.teamUpdateTeam(selectedTeam.id, { name: editName.trim(), bio: editBio.trim() })
      await refreshTeamData()
      setFeedback({ tone: 'success', message: '战队资料已保存。' })
    } catch (error) {
      setFeedback({ tone: 'danger', message: errorMessage(error, '战队资料保存失败。') })
    } finally {
      setSubmitting(false)
    }
  }

  const uploadAvatar = async () => {
    if (!selectedTeam?.id || !avatarFile) return
    setSubmitting(true)
    setFeedback(null)
    try {
      await api.team.teamAvatar(selectedTeam.id, { file: avatarFile })
      setAvatarFile(null)
      await refreshTeamData()
      setFeedback({ tone: 'success', message: '战队头像已更新。' })
    } catch (error) {
      setFeedback({ tone: 'danger', message: errorMessage(error, '战队头像上传失败。') })
    } finally {
      setSubmitting(false)
    }
  }

  const reviewRequest = async (requestId: number | undefined, accepted: boolean) => {
    if (!selectedTeam?.id || !requestId) return
    setSubmitting(true)
    try {
      await api.team.teamReviewJoinRequest(selectedTeam.id, requestId, { accepted })
      await refreshTeamData()
    } catch (error) {
      setFeedback({ tone: 'danger', message: errorMessage(error, '申请处理失败。') })
    } finally {
      setSubmitting(false)
    }
  }

  const kickMember = async (userId?: string | null) => {
    if (!selectedTeam?.id || !userId) return false
    setSubmitting(true)
    try {
      await api.team.teamKickUser(selectedTeam.id, userId)
      await refreshTeamData()
      return true
    } catch (error) {
      setFeedback({ tone: 'danger', message: errorMessage(error, '移除成员失败。') })
      return false
    } finally {
      setSubmitting(false)
    }
  }

  const transferCaptain = async (userId?: string | null) => {
    if (!selectedTeam?.id || !userId) return false
    setSubmitting(true)
    try {
      await api.team.teamTransfer(selectedTeam.id, { newCaptainId: userId })
      await refreshTeamData()
      return true
    } catch (error) {
      setFeedback({ tone: 'danger', message: errorMessage(error, '队长转让失败。') })
      return false
    } finally {
      setSubmitting(false)
    }
  }

  const leaveTeam = async () => {
    if (!selectedTeam?.id) return false
    setSubmitting(true)
    try {
      await api.team.teamLeave(selectedTeam.id)
      const next = new URLSearchParams(searchParams)
      next.delete('team')
      next.delete('tab')
      setSearchParams(next)
      setMobileDetailOpen(false)
      await mutateTeams()
      return true
    } catch (error) {
      setFeedback({ tone: 'danger', message: errorMessage(error, '退出战队失败。') })
      return false
    } finally {
      setSubmitting(false)
    }
  }

  const deleteTeam = async () => {
    if (!selectedTeam?.id) return false
    setSubmitting(true)
    try {
      await api.team.teamDeleteTeam(selectedTeam.id)
      const next = new URLSearchParams(searchParams)
      next.delete('team')
      next.delete('tab')
      setSearchParams(next)
      setMobileDetailOpen(false)
      await mutateTeams()
      return true
    } catch (error) {
      setFeedback({ tone: 'danger', message: errorMessage(error, '删除战队失败。') })
      return false
    } finally {
      setSubmitting(false)
    }
  }

  const runConfirmedAction = async () => {
    if (!confirmation) return false
    if (confirmation.kind === 'kick') return kickMember(confirmation.userId)
    if (confirmation.kind === 'transfer') return transferCaptain(confirmation.userId)
    if (confirmation.kind === 'leave') return leaveTeam()
    return deleteTeam()
  }

  if (!account.isAuthenticated && !account.error)
    return (
      <div className={styles.statePage}>
        <DataState description="正在确认账户状态。" loading title="账户检查中" />
      </div>
    )
  if (!account.isAuthenticated)
    return (
      <div className={styles.statePage}>
        <DataState description="登录后可以创建、加入和管理战队。" title="需要登录" />
      </div>
    )

  return (
    <div className={styles.page}>
      <PageHeading
        actions={
          <div className={styles.headingActions}>
            <ActionButton icon={<Plus size={16} />} onClick={() => setCreateOpen(true)} tone="primary" type="button">
              创建战队
            </ActionButton>
            <ActionButton icon={<UserPlus size={16} />} onClick={() => setJoinOpen(true)} type="button">
              邀请码加入
            </ActionButton>
          </div>
        }
        description="管理队伍成员、加入申请和比赛协作关系。"
        eyebrow="TEAM OPERATIONS"
        title="战队协作"
      />

      {feedback ? (
        <div className={styles.pageFeedback}>
          <InlineFeedback tone={feedback.tone}>{feedback.message}</InlineFeedback>
        </div>
      ) : null}

      <div className={`${styles.workspace} ${mobileDetailOpen ? styles.workspaceMobileDetail : ''}`}>
        <aside className={styles.teamRail}>
          <div className={styles.teamRailHeader}>
            <span>MY TEAMS</span>
            <strong>{teams?.length ?? 0} 支战队</strong>
          </div>
          <div className={styles.teamList}>
            {!teams && !teamsError ? <span className={styles.railLoading}>正在读取战队...</span> : null}
            {(teams ?? []).map((team) => (
              <button
                className={team.id === selectedId ? styles.teamListItemActive : styles.teamListItem}
                key={team.id}
                onClick={() => team.id && setSelectedTeam(team.id)}
                type="button"
              >
                <TeamAvatar team={team} />
                <span className={styles.teamListCopy}>
                  <strong>{team.name || `战队 ${team.id}`}</strong>
                  <small>{team.members?.length ?? 0} 名成员</small>
                </span>
              </button>
            ))}
            {teamsError ? <span className={styles.railLoading}>战队列表加载失败。</span> : null}
            {teams && !teams.length ? <span className={styles.railLoading}>尚未加入战队。</span> : null}
          </div>
          <button className={styles.searchTeamButton} onClick={() => setSearchOpen(true)} type="button">
            <Search size={16} />
            搜索并申请加入
          </button>
        </aside>

        <section className={styles.teamDetail}>
          {!selectedTeam ? (
            <DataState description="创建战队、使用邀请码加入，或搜索公开战队提交申请。" title="选择一支战队开始" />
          ) : (
            <>
              <button className={styles.mobileBack} onClick={() => setMobileDetailOpen(false)} type="button">
                <ChevronLeft size={16} />
                返回战队列表
              </button>
              <header className={styles.teamHeader}>
                <TeamAvatar large team={selectedTeam} />
                <div>
                  <div className={styles.teamNameLine}>
                    <h2>{selectedTeam.name}</h2>
                    {isCaptain ? (
                      <StatusPill tone="success">
                        <Crown size={13} />
                        队长
                      </StatusPill>
                    ) : null}
                    {selectedTeam.locked ? <StatusPill>已锁定</StatusPill> : null}
                  </div>
                  <p>{selectedTeam.bio || '这支战队还没有填写简介。'}</p>
                </div>
              </header>
              <nav aria-label="战队详情" className={styles.tabs}>
                <button
                  className={activeTab === 'overview' ? styles.tabActive : styles.tab}
                  onClick={() => setTab('overview')}
                  type="button"
                >
                  概览
                </button>
                <button
                  className={activeTab === 'members' ? styles.tabActive : styles.tab}
                  onClick={() => setTab('members')}
                  type="button"
                >
                  成员
                </button>
                {isCaptain ? (
                  <button
                    className={activeTab === 'requests' ? styles.tabActive : styles.tab}
                    onClick={() => setTab('requests')}
                    type="button"
                  >
                    加入申请
                  </button>
                ) : null}
                {isCaptain ? (
                  <button
                    className={activeTab === 'settings' ? styles.tabActive : styles.tab}
                    onClick={() => setTab('settings')}
                    type="button"
                  >
                    设置
                  </button>
                ) : null}
              </nav>

              <div className={styles.tabBody}>
                {activeTab === 'overview' ? (
                  <div className={styles.overviewGrid}>
                    <section className={styles.metricBand}>
                      <div>
                        <span>成员</span>
                        <strong>{selectedTeam.members?.length ?? 0}</strong>
                      </div>
                      <div>
                        <span>战队状态</span>
                        <strong>{selectedTeam.locked ? '已锁定' : '可用'}</strong>
                      </div>
                      <div>
                        <span>我的角色</span>
                        <strong>{isCaptain ? '队长' : '成员'}</strong>
                      </div>
                    </section>
                    <section className={styles.memberPreview}>
                      <header>
                        <span>MEMBERS</span>
                        <h3>成员构成</h3>
                      </header>
                      {(selectedTeam.members ?? []).slice(0, 6).map((member) => (
                        <div className={styles.memberCompact} key={member.id}>
                          <span className={styles.memberAvatar}>
                            <MemberAvatar name={member.userName} src={member.avatar} />
                          </span>
                          <span>
                            <strong>{member.userName || '未命名用户'}</strong>
                            <small>{member.captain ? '队长' : '成员'}</small>
                          </span>
                        </div>
                      ))}
                    </section>
                    {isCaptain ? (
                      <section className={styles.invitePanel}>
                        <header>
                          <span>INVITATION</span>
                          <h3>邀请码</h3>
                        </header>
                        <p>邀请码仅供可信成员使用，刷新后旧邀请码立即失效。</p>
                        <code>{inviteCode || '读取中...'}</code>
                        <div className={styles.inlineActions}>
                          <ActionButton
                            icon={<Clipboard size={15} />}
                            onClick={() => inviteCode && navigator.clipboard.writeText(inviteCode)}
                            type="button"
                          >
                            复制
                          </ActionButton>
                          <ActionButton
                            icon={<RefreshCw size={15} />}
                            onClick={async () => {
                              if (!selectedTeam.id) return
                              await api.team.teamUpdateInviteToken(selectedTeam.id)
                              await mutateInviteCode()
                            }}
                            type="button"
                          >
                            刷新
                          </ActionButton>
                        </div>
                      </section>
                    ) : null}
                  </div>
                ) : null}

                {activeTab === 'members' ? (
                  <section className={styles.memberTableSection}>
                    <header>
                      <span>ROSTER</span>
                      <h3>战队成员</h3>
                    </header>
                    <div className={styles.memberTable}>
                      {(selectedTeam.members ?? []).map((member) => (
                        <div className={styles.memberRow} key={member.id}>
                          <span className={styles.memberAvatar}>
                            <MemberAvatar name={member.userName} src={member.avatar} />
                          </span>
                          <span className={styles.memberIdentity}>
                            <strong>{member.userName || '未命名用户'}</strong>
                            <small>{member.bio || '暂无简介'}</small>
                          </span>
                          {member.captain ? (
                            <StatusPill tone="success">队长</StatusPill>
                          ) : (
                            <StatusPill>成员</StatusPill>
                          )}
                          {isCaptain && !member.captain ? (
                            <span className={styles.memberActions}>
                              <button
                                onClick={() =>
                                  member.id &&
                                  setConfirmation({
                                    kind: 'transfer',
                                    userId: member.id,
                                    memberName: member.userName || '该成员',
                                  })
                                }
                                type="button"
                              >
                                转让队长
                              </button>
                              <button
                                onClick={() =>
                                  member.id &&
                                  setConfirmation({
                                    kind: 'kick',
                                    userId: member.id,
                                    memberName: member.userName || '该成员',
                                  })
                                }
                                type="button"
                              >
                                移除
                              </button>
                            </span>
                          ) : null}
                        </div>
                      ))}
                    </div>
                  </section>
                ) : null}

                {activeTab === 'requests' && isCaptain ? (
                  <section className={styles.memberTableSection}>
                    <header>
                      <span>JOIN REQUESTS</span>
                      <h3>待审核申请</h3>
                    </header>
                    {!requests ? (
                      <DataState description="正在读取加入申请。" loading title="申请加载中" />
                    ) : requests.filter((request) => request.status === TeamJoinRequestStatus.Pending).length ? (
                      <div className={styles.requestList}>
                        {requests
                          .filter((request) => request.status === TeamJoinRequestStatus.Pending)
                          .map((request) => (
                            <article key={request.id}>
                              <div>
                                <strong>{request.user?.userName || '未命名用户'}</strong>
                                <p>{request.message || '未填写申请说明。'}</p>
                              </div>
                              <div className={styles.requestActions}>
                                <ActionButton
                                  disabled={submitting}
                                  icon={<X size={15} />}
                                  onClick={() => reviewRequest(request.id, false)}
                                  tone="danger"
                                  type="button"
                                >
                                  拒绝
                                </ActionButton>
                                <ActionButton
                                  disabled={submitting}
                                  icon={<Check size={15} />}
                                  onClick={() => reviewRequest(request.id, true)}
                                  tone="primary"
                                  type="button"
                                >
                                  通过
                                </ActionButton>
                              </div>
                            </article>
                          ))}
                      </div>
                    ) : (
                      <DataState description="当前没有等待处理的加入申请。" title="申请已处理完毕" />
                    )}
                  </section>
                ) : null}

                {activeTab === 'settings' && isCaptain ? (
                  <div className={styles.settingsStack}>
                    <section className={styles.teamSettings}>
                      <header>
                        <span>TEAM PROFILE</span>
                        <h3>战队资料</h3>
                      </header>
                      <label>
                        <span>战队名称</span>
                        <input
                          maxLength={20}
                          onChange={(event) => setEditName(event.currentTarget.value)}
                          value={editName}
                        />
                      </label>
                      <label>
                        <span>战队简介</span>
                        <textarea
                          maxLength={72}
                          onChange={(event) => setEditBio(event.currentTarget.value)}
                          rows={4}
                          value={editBio}
                        />
                      </label>
                      <div className={styles.settingsActions}>
                        <ActionButton
                          disabled={submitting || !editName.trim()}
                          icon={<Settings size={15} />}
                          onClick={saveTeam}
                          tone="primary"
                          type="button"
                        >
                          保存资料
                        </ActionButton>
                      </div>
                    </section>
                    <section className={styles.avatarUpload}>
                      <div>
                        <span>TEAM AVATAR</span>
                        <h3>战队头像</h3>
                        <p>选择图片后单独上传，不会覆盖未保存的文字资料。</p>
                      </div>
                      <label className={styles.fileButton}>
                        选择图片
                        <input
                          accept="image/*"
                          onChange={(event) => setAvatarFile(event.currentTarget.files?.[0] ?? null)}
                          type="file"
                        />
                      </label>
                      <ActionButton disabled={!avatarFile || submitting} onClick={uploadAvatar} type="button">
                        上传
                      </ActionButton>
                    </section>
                    <section className={styles.dangerZone}>
                      <div>
                        <span>DANGER ZONE</span>
                        <h3>高风险操作</h3>
                        <p>删除战队不可恢复；队长必须先转让权限才能退出。</p>
                      </div>
                      <div>
                        <ActionButton
                          icon={<LogOut size={15} />}
                          onClick={() => setConfirmation({ kind: 'leave' })}
                          tone="secondary"
                          type="button"
                        >
                          退出战队
                        </ActionButton>
                        <ActionButton
                          icon={<Trash2 size={15} />}
                          onClick={() => setConfirmation({ kind: 'delete' })}
                          tone="danger"
                          type="button"
                        >
                          删除战队
                        </ActionButton>
                      </div>
                    </section>
                  </div>
                ) : null}
              </div>
            </>
          )}
        </section>
      </div>

      <TeamDialogs
        createOpen={createOpen}
        joinOpen={joinOpen}
        onCreateClose={() => setCreateOpen(false)}
        onFeedback={setFeedback}
        onJoinClose={() => setJoinOpen(false)}
        onSearchClose={() => setSearchOpen(false)}
        onTeamCreated={(teamId) => {
          if (teamId) setSelectedTeam(teamId)
        }}
        onTeamsChanged={mutateTeams}
        searchOpen={searchOpen}
      />
      <TeamConfirmationDialog
        action={confirmation}
        onClose={() => setConfirmation(null)}
        onConfirm={runConfirmedAction}
        teamName={selectedTeam?.name}
      />
    </div>
  )
}
