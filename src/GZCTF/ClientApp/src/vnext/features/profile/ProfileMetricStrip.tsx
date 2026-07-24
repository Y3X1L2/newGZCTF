import { Activity, CheckCircle2, ClipboardList, GraduationCap, Target, Trophy } from 'lucide-react'
import type { UserProfileMetrics } from './api/userProfileApi'
import styles from './UserProfilePage.module.css'

const metricDefinitions = [
  { key: 'solved', label: '个人解题', icon: Target },
  { key: 'submissions', label: '有效提交', icon: ClipboardList },
  { key: 'successRate', label: '提交正确率', icon: CheckCircle2 },
  { key: 'gameCount', label: '参赛场次', icon: Trophy },
  { key: 'courseCount', label: '课程', icon: GraduationCap },
  { key: 'activeDays', label: '活跃天数', icon: Activity },
] as const

export function ProfileMetricStrip({ metrics }: { metrics: UserProfileMetrics }) {
  return (
    <section aria-label="个人公开统计" className={styles.metricStrip}>
      {metricDefinitions.map((definition) => {
        const Icon = definition.icon
        const value = metrics[definition.key]
        return (
          <div key={definition.key}>
            <Icon aria-hidden="true" size={18} />
            <span>
              <strong>{definition.key === 'successRate' ? `${value}%` : value}</strong>
              <small>{definition.label}</small>
            </span>
          </div>
        )
      })}
    </section>
  )
}
