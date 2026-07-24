import { Navigate, useLocation } from 'react-router'
import { DataState } from '../../shared/Primitives'
import { useVNextPageTitle } from '../../shared/useVNextPageTitle'
import { ProfileActivityHeatmap } from './ProfileActivityHeatmap'
import { ProfileGrowthChart } from './ProfileGrowthChart'
import { ProfileHistory } from './ProfileHistory'
import { ProfileFacts, ProfileIdentity } from './ProfileIdentity'
import { ProfileMetricStrip } from './ProfileMetricStrip'
import { ProfileSkillMap } from './ProfileSkillMap'
import { profileTabLabels, profileTabs, type ProfileWindow } from './profileDomain'
import styles from './UserProfilePage.module.css'
import { useUserProfileController } from './useUserProfileController'

export function UserProfilePage() {
  const location = useLocation()
  const controller = useUserProfileController()
  useVNextPageTitle(controller.profile?.userName ? `${controller.profile.userName} 的个人主页` : '个人主页')

  if (controller.isMeRoute && !controller.resolvedUserId && controller.account.error) {
    return <Navigate replace to="/account/login?returnUrl=%2Fusers%2Fme" />
  }
  if (controller.isMeRoute && controller.resolvedUserId) {
    return <Navigate replace to={`/users/${controller.resolvedUserId}${location.search}`} />
  }
  if (!controller.resolvedUserId || controller.profileLoading || controller.overviewLoading) {
    return (
      <div className={styles.statePage}>
        <DataState description="正在读取公开身份与个人统计。" loading title="个人主页加载中" />
      </div>
    )
  }
  if (controller.profileError || controller.overviewError || !controller.profile || !controller.overview) {
    return (
      <div className={styles.statePage}>
        <DataState description="用户不存在，或个人统计暂时无法读取。" title="无法打开个人主页" />
      </div>
    )
  }

  const showSkill = controller.tab === 'overview' || controller.tab === 'challenges'
  const showTrend = controller.tab === 'overview' || controller.tab === 'challenges'

  return (
    <div className={styles.page}>
      <ProfileIdentity isOwnProfile={controller.isOwnProfile} profile={controller.profile} />
      <ProfileMetricStrip metrics={controller.overview.metrics} />

      <div className={styles.profileToolbar}>
        <nav aria-label="个人主页视图" className={styles.profileTabs}>
          {profileTabs.map((tab) => (
            <button
              aria-current={controller.tab === tab ? 'page' : undefined}
              className={controller.tab === tab ? styles.profileTabActive : styles.profileTab}
              key={tab}
              onClick={() => controller.setTab(tab)}
              type="button"
            >
              {profileTabLabels[tab]}
            </button>
          ))}
        </nav>
        <div aria-label="统计时间范围" className={styles.windowControl} role="group">
          {(['90d', '365d'] as ProfileWindow[]).map((window) => (
            <button
              aria-pressed={controller.window === window}
              className={controller.window === window ? styles.windowActive : styles.windowButton}
              key={window}
              onClick={() => controller.setWindow(window)}
              type="button"
            >
              {window === '90d' ? '90 天' : '365 天'}
            </button>
          ))}
        </div>
      </div>

      <div className={styles.contentGrid}>
        <main className={styles.profileMain}>
          <div ref={controller.activityRef}>
            <ProfileActivityHeatmap
              failed={Boolean(controller.activityError)}
              from={controller.range.from}
              loading={controller.activityLoading}
              points={controller.activity}
              to={controller.range.to}
              window={controller.window}
            />
          </div>
          {showSkill ? <ProfileSkillMap dimensions={controller.overview.dimensions} /> : null}
          {showTrend ? <ProfileGrowthChart trend={controller.overview.trend} window={controller.window} /> : null}
          <div ref={controller.historyRef}>
            <ProfileHistory
              failed={Boolean(controller.historyError)}
              hasMore={controller.hasMoreHistory}
              items={controller.historyItems}
              loading={controller.historyLoading}
              loadingMore={controller.historyLoadingMore}
              onLoadMore={controller.loadMoreHistory}
              tab={controller.tab}
            />
          </div>
        </main>
        <ProfileFacts
          isOwnProfile={controller.isOwnProfile}
          privateOverview={controller.privateOverview}
          profile={controller.profile}
        />
      </div>
    </div>
  )
}
