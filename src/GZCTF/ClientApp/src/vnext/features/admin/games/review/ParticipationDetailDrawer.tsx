import { useEffect, useMemo, useState } from 'react'
import { Division, ParticipationEditModel, ParticipationInfoModel, ParticipationStatus } from '@Api'
import { SelectField } from '../../../../shared/FormControls'
import { ActionButton, InlineFeedback, VNextDrawer, type DrawerRequestClose } from '../../../../shared/Interaction'
import { StatusBadge } from '../../shared/AdminWorkbench'
import styles from '../GameOperations.module.css'
import { participationStatusMeta } from '../gameOperationsPresentation'

export function ParticipationDetailDrawer({
  participation,
  divisions,
  open,
  onClose,
  onSave,
}: {
  participation: ParticipationInfoModel | null
  divisions: Division[]
  open: boolean
  onClose: () => void
  onSave: (participationId: number, payload: ParticipationEditModel) => Promise<boolean>
}) {
  const [status, setStatus] = useState<ParticipationStatus>(ParticipationStatus.Pending)
  const [divisionId, setDivisionId] = useState('')
  const [saving, setSaving] = useState(false)
  const [feedback, setFeedback] = useState<string | null>(null)

  useEffect(() => {
    if (!open || !participation) return
    setStatus(participation.status)
    setDivisionId(participation.divisionId?.toString() ?? '')
    setSaving(false)
    setFeedback(null)
  }, [open, participation])

  const registered = useMemo(() => new Set(participation?.registeredMembers ?? []), [participation])
  const statusMeta = participationStatusMeta(status)
  const dirty = Boolean(participation && (status !== participation.status || divisionId !== (participation.divisionId?.toString() ?? '')))

  const save = async (requestClose: DrawerRequestClose) => {
    if (!participation || !dirty) return
    setSaving(true)
    setFeedback(null)
    const payload: ParticipationEditModel = {
      status,
      divisionId: status === ParticipationStatus.Rejected || !divisionId ? null : Number(divisionId),
    }
    try {
      if (await onSave(participation.id, payload)) requestClose()
    } catch {
      setFeedback('报名信息更新失败。')
    } finally {
      setSaving(false)
    }
  }

  return (
    <VNextDrawer
      description="核对战队成员、报名赛区和审核状态。"
      eyebrow="PARTICIPATION DETAIL"
      footer={(requestClose) => <><ActionButton disabled={saving} onClick={() => requestClose()} type="button">关闭</ActionButton><ActionButton disabled={saving || !dirty} onClick={() => void save(requestClose)} tone="primary" type="button">{saving ? '正在保存' : '保存审核结果'}</ActionButton></>}
      onClose={onClose}
      open={open}
      size="wide"
      title={participation?.team.name ?? '报名详情'}
    >
      {participation ? (
        <div className={styles.drawerStack}>
          {feedback ? <InlineFeedback tone="danger">{feedback}</InlineFeedback> : null}
          <section className={styles.drawerSection}>
            <h3>报名事实</h3>
            <div className={styles.facts}>
              <div className={styles.fact}><span>报名编号</span><strong>#{participation.id}</strong></div>
              <div className={styles.fact}><span>当前状态</span><StatusBadge tone={participationStatusMeta(participation.status).tone}>{participationStatusMeta(participation.status).label}</StatusBadge></div>
              <div className={styles.fact}><span>战队成员</span><strong>{participation.team.members?.length ?? 0} 人</strong></div>
              <div className={styles.fact}><span>报名成员</span><strong>{participation.registeredMembers.length} 人</strong></div>
            </div>
          </section>
          <section className={styles.drawerSection}>
            <h3>审核决策</h3>
            <p>通过报名会锁定战队，并由服务端准备该比赛已配置的实例资源。</p>
            {status === ParticipationStatus.Accepted && participation.status !== ParticipationStatus.Accepted ? <InlineFeedback tone="danger">确认通过后，服务端可能立即为该战队创建或准备比赛实例。</InlineFeedback> : null}
            <div className={styles.fieldGrid}>
              <SelectField label="报名状态" onValueChange={(value) => { const next = value as ParticipationStatus; setStatus(next); if (next === ParticipationStatus.Rejected) setDivisionId('') }} value={status}>
                {Object.values(ParticipationStatus).map((value) => <option key={value} value={value}>{participationStatusMeta(value).label}</option>)}
              </SelectField>
              <SelectField disabled={status === ParticipationStatus.Rejected} label="所属赛区" onValueChange={setDivisionId} value={status === ParticipationStatus.Rejected ? '' : divisionId}>
                <option value="">未分配赛区</option>
                {divisions.map((division) => <option key={division.id} value={division.id}>{division.name}</option>)}
              </SelectField>
            </div>
            <StatusBadge tone={statusMeta.tone}>保存后状态：{statusMeta.label}</StatusBadge>
          </section>
          <section className={styles.drawerSection}>
            <h3>战队成员</h3>
            <div className={styles.memberList}>
              {(participation.team.members ?? []).map((member) => {
                const memberId = member.userId ?? ''
                const isCaptain = participation.team.captainId === memberId
                const isRegistered = registered.has(memberId)
                return (
                  <div className={styles.memberRow} key={memberId || member.userName}>
                    <span className={styles.memberAvatar}>{member.avatar ? <img alt="" src={member.avatar} /> : (member.userName?.slice(0, 1) ?? 'U')}</span>
                    <span className={styles.memberCopy}><strong>{member.userName ?? '未命名用户'}{isCaptain ? ' · 队长' : ''}</strong><small>{member.realName || member.email || memberId || '无补充信息'}</small></span>
                    <StatusBadge tone={isRegistered ? 'success' : 'neutral'}>{isRegistered ? '参加比赛' : '未报名'}</StatusBadge>
                  </div>
                )
              })}
            </div>
          </section>
        </div>
      ) : null}
    </VNextDrawer>
  )
}
