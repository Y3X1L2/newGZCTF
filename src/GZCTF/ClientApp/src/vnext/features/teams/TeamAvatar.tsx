import { useEffect, useState } from 'react'
import { TeamInfoModel } from '@Api'
import styles from './TeamsPage.module.css'

function initials(name?: string | null) {
  return name?.trim().slice(0, 2).toUpperCase() || 'YY'
}

function AvatarMedia({ src, fallback }: { src?: string | null; fallback: string }) {
  const [failed, setFailed] = useState(false)

  useEffect(() => setFailed(false), [src])

  if (src && !failed) return <img alt="" onError={() => setFailed(true)} src={src} />
  return <>{fallback}</>
}

export function TeamAvatar({ team, large = false }: { team: TeamInfoModel; large?: boolean }) {
  return (
    <span className={large ? styles.teamAvatarLarge : styles.teamAvatar}>
      <AvatarMedia fallback={initials(team.name)} src={team.avatar} />
    </span>
  )
}

export function MemberAvatar({ name, src }: { name?: string | null; src?: string | null }) {
  return <AvatarMedia fallback={initials(name)} src={src} />
}
