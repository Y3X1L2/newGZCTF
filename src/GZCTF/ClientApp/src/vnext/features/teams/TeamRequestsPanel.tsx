import { Check, X } from 'lucide-react'
import { TeamJoinRequestModel, TeamJoinRequestStatus } from '@Api'
import { ActionButton } from '../../shared/Interaction'
import { DataState } from '../../shared/Primitives'
import styles from './TeamsPage.module.css'

interface TeamRequestsPanelProps {
  requests?: TeamJoinRequestModel[]
  submitting: boolean
  onReview: (requestId: number | undefined, accepted: boolean) => Promise<void>
}

export function TeamRequestsPanel({ requests, submitting, onReview }: TeamRequestsPanelProps) {
  const pendingRequests = requests?.filter((request) => request.status === TeamJoinRequestStatus.Pending)

  return (
    <section className={styles.memberTableSection}>
      <header>
        <span>JOIN REQUESTS</span>
        <h3>待审核申请</h3>
      </header>
      {!requests ? (
        <DataState description="正在读取加入申请。" loading title="申请加载中" />
      ) : pendingRequests?.length ? (
        <div className={styles.requestList}>
          {pendingRequests.map((request) => (
            <article key={request.id}>
              <div>
                <strong>{request.user?.userName || '未命名用户'}</strong>
                <p>{request.message || '未填写申请说明。'}</p>
              </div>
              <div className={styles.requestActions}>
                <ActionButton
                  disabled={submitting}
                  icon={<X size={15} />}
                  onClick={() => void onReview(request.id, false)}
                  tone="danger"
                  type="button"
                >
                  拒绝
                </ActionButton>
                <ActionButton
                  disabled={submitting}
                  icon={<Check size={15} />}
                  onClick={() => void onReview(request.id, true)}
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
  )
}
