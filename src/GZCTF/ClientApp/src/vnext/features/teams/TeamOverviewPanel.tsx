import { Clipboard, RefreshCw } from 'lucide-react'
import { TeamInfoModel } from '@Api'
import { ActionButton } from '../../shared/Interaction'
import { MemberAvatar } from './TeamAvatar'
import styles from './TeamsPage.module.css'

interface TeamOverviewPanelProps {
  team: TeamInfoModel
  isCaptain: boolean
  inviteCode?: string
  onRefreshInviteCode: () => Promise<void>
}

export function TeamOverviewPanel({ team, isCaptain, inviteCode, onRefreshInviteCode }: TeamOverviewPanelProps) {
  return (
    <div className={styles.overviewGrid}>
      <section className={styles.metricBand}>
        <div>
          <span>成员</span>
          <strong>{team.members?.length ?? 0}</strong>
        </div>
        <div>
          <span>战队状态</span>
          <strong>{team.locked ? '已锁定' : '可用'}</strong>
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
        {(team.members ?? []).slice(0, 6).map((member) => (
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
              disabled={!inviteCode}
              icon={<Clipboard size={15} />}
              onClick={() => inviteCode && void navigator.clipboard.writeText(inviteCode)}
              type="button"
            >
              复制
            </ActionButton>
            <ActionButton icon={<RefreshCw size={15} />} onClick={() => void onRefreshInviteCode()} type="button">
              刷新
            </ActionButton>
          </div>
        </section>
      ) : null}
    </div>
  )
}
