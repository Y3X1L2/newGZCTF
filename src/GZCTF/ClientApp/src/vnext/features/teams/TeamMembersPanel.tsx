import { TeamInfoModel } from '@Api'
import { Link } from 'react-router'
import { StatusPill } from '../../shared/Primitives'
import { MemberAvatar } from './TeamAvatar'
import { TeamConfirmation } from './TeamConfirmationDialog'
import styles from './TeamsPage.module.css'

interface TeamMembersPanelProps {
  team: TeamInfoModel
  isCaptain: boolean
  onConfirm: (confirmation: TeamConfirmation) => void
}

export function TeamMembersPanel({ team, isCaptain, onConfirm }: TeamMembersPanelProps) {
  return (
    <section className={styles.memberTableSection}>
      <header>
        <span>ROSTER</span>
        <h3>战队成员</h3>
      </header>
      <div className={styles.memberTable}>
        {(team.members ?? []).map((member) => (
          <div className={styles.memberRow} key={member.id}>
            <span className={styles.memberAvatar}>
              <MemberAvatar name={member.userName} src={member.avatar} />
            </span>
            <span className={styles.memberIdentity}>
              {member.id ? (
                <Link to={`/users/${member.id}`}>{member.userName || '未命名用户'}</Link>
              ) : (
                <strong>{member.userName || '未命名用户'}</strong>
              )}
              <small>{member.bio || '暂无简介'}</small>
            </span>
            <StatusPill tone={member.captain ? 'success' : 'neutral'}>{member.captain ? '队长' : '成员'}</StatusPill>
            {isCaptain && !member.captain ? (
              <span className={styles.memberActions}>
                <button
                  onClick={() =>
                    member.id &&
                    onConfirm({ kind: 'transfer', userId: member.id, memberName: member.userName || '该成员' })
                  }
                  type="button"
                >
                  转让队长
                </button>
                <button
                  onClick={() =>
                    member.id && onConfirm({ kind: 'kick', userId: member.id, memberName: member.userName || '该成员' })
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
  )
}
