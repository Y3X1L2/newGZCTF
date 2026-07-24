import { CalendarDays, Edit3, GraduationCap, ShieldCheck, Users } from 'lucide-react'
import { Link } from 'react-router'
import { roleLabel } from '../account/useCurrentAccount'
import type { PublicUserProfile, UserPrivateOverview } from './api/userProfileApi'
import styles from './UserProfilePage.module.css'

function initials(value: string) {
  return value.trim().slice(0, 2).toUpperCase() || 'YY'
}

function formatJoinDate(value: number) {
  return new Intl.DateTimeFormat('zh-CN', { year: 'numeric', month: 'long' }).format(value)
}

export function ProfileIdentity({ profile, isOwnProfile }: { profile: PublicUserProfile; isOwnProfile: boolean }) {
  const variant = profile.id.split('').reduce((sum, value) => sum + value.charCodeAt(0), 0) % 3

  return (
    <header className={styles.identityHero} data-variant={variant}>
      <span className={styles.identityPlane} aria-hidden="true" />
      <span className={styles.identityRoute} aria-hidden="true" />
      <span className={styles.profileAvatar}>
        {profile.avatar ? <img alt="" src={profile.avatar} /> : initials(profile.userName)}
      </span>
      <div className={styles.identityCopy}>
        <div className={styles.identityTags}>
          <span>{roleLabel(profile.role)}</span>
          {profile.publicTeam ? (
            <Link to={`/teams?team=${profile.publicTeam.id}`}>
              <Users size={14} />
              {profile.publicTeam.name}
            </Link>
          ) : null}
        </div>
        <h1>{profile.userName}</h1>
        <p>{profile.bio || '这位用户还没有填写公开简介。'}</p>
        <span className={styles.joinedAt}>
          <CalendarDays size={15} />
          {formatJoinDate(profile.registeredAt)}加入平台
        </span>
      </div>
      {isOwnProfile ? (
        <Link className={styles.editProfile} to="/settings/profile">
          <Edit3 size={16} />
          编辑资料
        </Link>
      ) : null}
    </header>
  )
}

export function ProfileFacts({
  profile,
  privateOverview,
  isOwnProfile,
}: {
  profile: PublicUserProfile
  privateOverview?: UserPrivateOverview
  isOwnProfile: boolean
}) {
  return (
    <aside className={styles.factsRail}>
      <section>
        <span className={styles.panelEyebrow}>IDENTITY FACTS</span>
        <h2>身份事实</h2>
        <dl className={styles.factList}>
          <div>
            <dt>平台角色</dt>
            <dd>
              <ShieldCheck size={15} />
              {roleLabel(profile.role)}
            </dd>
          </div>
          <div>
            <dt>加入时间</dt>
            <dd>{formatJoinDate(profile.registeredAt)}</dd>
          </div>
          <div>
            <dt>公开战队</dt>
            <dd>
              {profile.publicTeam ? <Link to={`/teams?team=${profile.publicTeam.id}`}>{profile.publicTeam.name}</Link> : '暂无'}
            </dd>
          </div>
        </dl>
      </section>

      {profile.taughtCourses.length ? (
        <section>
          <span className={styles.panelEyebrow}>TEACHING</span>
          <h2>授课课程</h2>
          <div className={styles.taughtCourses}>
            {profile.taughtCourses.map((course) => (
              <Link key={course.id} to={`/training/courses/${course.id}`}>
                <GraduationCap size={16} />
                <span>{course.title}</span>
              </Link>
            ))}
          </div>
        </section>
      ) : null}

      {isOwnProfile && privateOverview ? (
        <section>
          <span className={styles.panelEyebrow}>PRIVATE OVERVIEW</span>
          <h2>我的学习摘要</h2>
          <dl className={styles.privateFacts}>
            <div>
              <dt>学习中</dt>
              <dd>{privateOverview.learningCourses}</dd>
            </div>
            <div>
              <dt>已完成</dt>
              <dd>{privateOverview.completedCourses}</dd>
            </div>
            <div>
              <dt>理论交卷</dt>
              <dd>{privateOverview.submittedTheoryAssignments}</dd>
            </div>
          </dl>
          <small>本区仅本人可见</small>
        </section>
      ) : null}
    </aside>
  )
}
