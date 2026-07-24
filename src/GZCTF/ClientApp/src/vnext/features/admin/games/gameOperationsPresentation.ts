import { GameNotice, ParticipationInfoModel, ParticipationStatus } from '@Api'
import type { AdminStatusTone } from '../shared/AdminWorkbench'

export function participationStatusMeta(status: ParticipationStatus) {
  const values: Record<ParticipationStatus, { label: string; tone: AdminStatusTone }> = {
    [ParticipationStatus.Pending]: { label: '待审核', tone: 'warning' },
    [ParticipationStatus.Accepted]: { label: '已通过', tone: 'success' },
    [ParticipationStatus.Rejected]: { label: '已拒绝', tone: 'danger' },
    [ParticipationStatus.Suspended]: { label: '已暂停', tone: 'warning' },
    [ParticipationStatus.Unsubmitted]: { label: '未提交', tone: 'neutral' },
  }
  return values[status]
}

export function participationSearchText(participation: ParticipationInfoModel) {
  const memberNames = participation.team.members?.map((member) => `${member.userName ?? ''} ${member.realName ?? ''}`).join(' ') ?? ''
  return `${participation.id} ${participation.team.name ?? ''} ${participation.team.bio ?? ''} ${memberNames}`.toLocaleLowerCase('zh-CN')
}

export function noticeContent(notice: GameNotice) {
  return notice.values.at(-1)?.trim() ?? ''
}

export function noticeSummary(notice: GameNotice, maxLength = 120) {
  const content = noticeContent(notice).replace(/\s+/g, ' ')
  return content.length > maxLength ? `${content.slice(0, maxLength)}...` : content
}
