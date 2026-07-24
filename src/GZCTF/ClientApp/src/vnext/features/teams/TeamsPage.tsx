import { ChevronLeft, Crown, Plus, Search, UserPlus } from 'lucide-react'
import { ActionButton, InlineFeedback } from '../../shared/Interaction'
import { DataState, PageHeading, StatusPill } from '../../shared/Primitives'
import { useVNextPageTitle } from '../../shared/useVNextPageTitle'
import { TeamAvatar } from './TeamAvatar'
import { TeamConfirmationDialog } from './TeamConfirmationDialog'
import { TeamDialogs } from './TeamDialogs'
import { TeamMembersPanel } from './TeamMembersPanel'
import { TeamOverviewPanel } from './TeamOverviewPanel'
import { TeamRequestsPanel } from './TeamRequestsPanel'
import { TeamSettingsPanel } from './TeamSettingsPanel'
import styles from './TeamsPage.module.css'
import { useTeamWorkspaceController } from './useTeamWorkspaceController'

export function TeamsPage() {
  const controller = useTeamWorkspaceController()
  useVNextPageTitle('战队协作')

  if (!controller.account.isAuthenticated && !controller.account.error) {
    return (
      <div className={styles.statePage}>
        <DataState description="正在确认账户状态。" loading title="账户检查中" />
      </div>
    )
  }

  if (!controller.account.isAuthenticated) {
    return (
      <div className={styles.statePage}>
        <DataState description="登录后可以创建、加入和管理战队。" title="需要登录" />
      </div>
    )
  }

  const team = controller.selectedTeam

  return (
    <div className={styles.page}>
      <PageHeading
        actions={
          <div className={styles.headingActions}>
            <ActionButton
              icon={<Plus size={16} />}
              onClick={() => controller.setCreateOpen(true)}
              tone="primary"
              type="button"
            >
              创建战队
            </ActionButton>
            <ActionButton icon={<UserPlus size={16} />} onClick={() => controller.setJoinOpen(true)} type="button">
              邀请码加入
            </ActionButton>
          </div>
        }
        description="管理队伍成员、加入申请和比赛协作关系。"
        eyebrow="TEAM OPERATIONS"
        title="战队协作"
      />

      {controller.feedback ? (
        <div className={styles.pageFeedback}>
          <InlineFeedback tone={controller.feedback.tone}>{controller.feedback.message}</InlineFeedback>
        </div>
      ) : null}

      <div className={`${styles.workspace} ${controller.mobileDetailOpen ? styles.workspaceMobileDetail : ''}`}>
        <aside className={styles.teamRail}>
          <div className={styles.teamRailHeader}>
            <span>MY TEAMS</span>
            <strong>{controller.teams?.length ?? 0} 支战队</strong>
          </div>
          <div className={styles.teamList}>
            {!controller.teams && !controller.teamsError ? (
              <span className={styles.railLoading}>正在读取战队...</span>
            ) : null}
            {(controller.teams ?? []).map((item) => (
              <button
                className={item.id === controller.selectedId ? styles.teamListItemActive : styles.teamListItem}
                key={item.id}
                onClick={() => item.id && controller.selectTeam(item.id)}
                type="button"
              >
                <TeamAvatar team={item} />
                <span className={styles.teamListCopy}>
                  <strong>{item.name || `战队 ${item.id}`}</strong>
                  <small>{item.members?.length ?? 0} 名成员</small>
                </span>
              </button>
            ))}
            {controller.teamsError ? <span className={styles.railLoading}>战队列表加载失败。</span> : null}
            {controller.teams && !controller.teams.length ? (
              <span className={styles.railLoading}>尚未加入战队。</span>
            ) : null}
          </div>
          <button className={styles.searchTeamButton} onClick={() => controller.setSearchOpen(true)} type="button">
            <Search size={16} />
            搜索并申请加入
          </button>
        </aside>

        <section className={styles.teamDetail}>
          {!team ? (
            <DataState description="创建战队、使用邀请码加入，或搜索公开战队提交申请。" title="选择一支战队开始" />
          ) : (
            <>
              <button className={styles.mobileBack} onClick={() => controller.setMobileDetailOpen(false)} type="button">
                <ChevronLeft size={16} />
                返回战队列表
              </button>
              <header className={styles.teamHeader}>
                <TeamAvatar large team={team} />
                <div>
                  <div className={styles.teamNameLine}>
                    <h2>{team.name}</h2>
                    {controller.isCaptain ? (
                      <StatusPill tone="success">
                        <Crown size={13} />
                        队长
                      </StatusPill>
                    ) : null}
                    {team.locked ? <StatusPill>已锁定</StatusPill> : null}
                  </div>
                  <p>{team.bio || '这支战队还没有填写简介。'}</p>
                </div>
              </header>

              <nav aria-label="战队详情" className={styles.tabs}>
                {(['overview', 'members'] as const).map((tab) => (
                  <button
                    className={controller.activeTab === tab ? styles.tabActive : styles.tab}
                    key={tab}
                    onClick={() => controller.setTab(tab)}
                    type="button"
                  >
                    {tab === 'overview' ? '概览' : '成员'}
                  </button>
                ))}
                {controller.isCaptain ? (
                  <>
                    <button
                      className={controller.activeTab === 'requests' ? styles.tabActive : styles.tab}
                      onClick={() => controller.setTab('requests')}
                      type="button"
                    >
                      加入申请
                    </button>
                    <button
                      className={controller.activeTab === 'settings' ? styles.tabActive : styles.tab}
                      onClick={() => controller.setTab('settings')}
                      type="button"
                    >
                      设置
                    </button>
                  </>
                ) : null}
              </nav>

              <div className={styles.tabBody}>
                {controller.activeTab === 'overview' ? (
                  <TeamOverviewPanel
                    inviteCode={controller.inviteCode}
                    isCaptain={controller.isCaptain}
                    onRefreshInviteCode={controller.refreshInviteCode}
                    team={team}
                  />
                ) : null}
                {controller.activeTab === 'members' ? (
                  <TeamMembersPanel
                    isCaptain={controller.isCaptain}
                    onConfirm={controller.setConfirmation}
                    team={team}
                  />
                ) : null}
                {controller.activeTab === 'requests' && controller.isCaptain ? (
                  <TeamRequestsPanel
                    onReview={controller.reviewRequest}
                    requests={controller.requests}
                    submitting={controller.submitting}
                  />
                ) : null}
                {controller.activeTab === 'settings' && controller.isCaptain ? (
                  <TeamSettingsPanel
                    avatarFile={controller.avatarFile}
                    editBio={controller.editBio}
                    editName={controller.editName}
                    onAvatarFile={controller.setAvatarFile}
                    onConfirm={controller.setConfirmation}
                    onEditBio={controller.setEditBio}
                    onEditName={controller.setEditName}
                    onSave={controller.saveTeam}
                    onUploadAvatar={controller.uploadAvatar}
                    submitting={controller.submitting}
                  />
                ) : null}
              </div>
            </>
          )}
        </section>
      </div>

      <TeamDialogs
        createOpen={controller.createOpen}
        joinOpen={controller.joinOpen}
        onCreateClose={() => controller.setCreateOpen(false)}
        onFeedback={controller.setFeedback}
        onJoinClose={() => controller.setJoinOpen(false)}
        onSearchClose={() => controller.setSearchOpen(false)}
        onTeamCreated={(teamId) => teamId && controller.selectTeam(teamId)}
        onTeamsChanged={controller.mutateTeams}
        searchOpen={controller.searchOpen}
      />
      <TeamConfirmationDialog
        action={controller.confirmation}
        onClose={() => controller.setConfirmation(null)}
        onConfirm={controller.runConfirmedAction}
        teamName={team?.name}
      />
    </div>
  )
}
