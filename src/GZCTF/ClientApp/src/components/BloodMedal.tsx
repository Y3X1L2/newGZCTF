import cx from 'clsx'
import { NoticeType, SubmissionType } from '@Api'

export type BloodTier = 1 | 2 | 3

const tierLabels: Record<BloodTier, string> = {
  1: '一血',
  2: '二血',
  3: '三血',
}

const tierPalettes: Record<BloodTier | 'empty', { back: string; face: string; icon: string; text: string }> = {
  1: { back: '#b88412', face: '#f1c84f', icon: '#ffffff', text: '#2a1900' },
  2: { back: '#8d9ca7', face: '#d7e1e7', icon: '#ffffff', text: '#142028' },
  3: { back: '#9d5226', face: '#d18746', icon: '#ffffff', text: '#241006' },
  empty: { back: '#3c4945', face: '#667771', icon: '#d2dfda', text: '#d2dfda' },
}

export const bloodTierLabel = (tier: BloodTier) => tierLabels[tier]

export const bloodTierFromSubmissionType = (type?: SubmissionType | null): BloodTier | null => {
  if (type === SubmissionType.FirstBlood) return 1
  if (type === SubmissionType.SecondBlood) return 2
  if (type === SubmissionType.ThirdBlood) return 3
  return null
}

export const bloodTierFromNoticeType = (type?: NoticeType | null): BloodTier | null => {
  if (type === NoticeType.FirstBlood) return 1
  if (type === NoticeType.SecondBlood) return 2
  if (type === NoticeType.ThirdBlood) return 3
  return null
}

export function BloodMedal({
  tier,
  type,
  active = true,
  own,
  size = 'md',
  className,
}: {
  tier?: BloodTier
  type?: SubmissionType | NoticeType | null
  active?: boolean
  own?: boolean
  size?: 'xs' | 'sm' | 'md'
  className?: string
}) {
  const resolvedTier =
    tier ?? bloodTierFromSubmissionType(type as SubmissionType | null) ?? bloodTierFromNoticeType(type as NoticeType | null) ?? 1
  const palette = active ? tierPalettes[resolvedTier] : tierPalettes.empty
  const label = active ? tierLabels[resolvedTier] : `暂无${tierLabels[resolvedTier]}`

  return (
    <span
      className={cx('yy-blood-medal', className)}
      data-tier={resolvedTier}
      data-size={size}
      data-active={active || undefined}
      data-own={own || undefined}
      aria-label={label}
      title={label}
    >
      <svg viewBox="0 0 1034 1024" role="img" aria-hidden="true" focusable="false">
        <path
          d="M765.305 169.589 499.425 98.448C360.735 61.081 218.453 143.719 181.086 282.408l-68.985 258.695c-37.367 138.689 45.272 280.971 183.961 318.338l265.88 71.141c138.689 37.367 281.69-45.272 318.338-183.961l68.986-258.694c37.367-138.689-45.272-280.971-183.961-318.338z"
          fill={palette.back}
          opacity={active ? 0.45 : 0.24}
        />
        <path
          d="M667.576 142.282H408.881c-135.096 0-244.323 109.227-244.323 244.323v251.509c0 135.096 109.227 244.322 244.323 244.322h258.695c135.096 0 244.323-109.226 244.323-244.322V386.605c0-135.096-109.227-244.323-244.323-244.323z"
          fill={palette.face}
          opacity={active ? 1 : 0.46}
        />
        <path
          d="M536.073 443.374c10.779 0 21.558 1.437 31.618 2.874L478.585 290.313c-2.156-2.874-5.03-5.03-9.342-5.03H327.68c-3.593 0-7.186 2.156-8.623 5.03-2.156 2.874-2.156 7.186 0 10.06L425.409 486.49c28.744-27.307 67.548-43.116 110.664-43.116zm0 37.367c-71.141 0-127.91 57.488-127.91 127.91 0 71.141 57.488 127.91 127.91 127.91 71.141 0 127.91-57.488 127.91-127.91s-57.488-127.91-127.91-127.91zm217.735-191.147c-2.156-2.874-5.03-5.03-8.623-5.03H602.184c-3.593 0-7.186 2.156-9.342 5.03l-34.493 61.081 83.358 145.156 112.101-195.458c1.437-3.593 1.437-7.186 0-10.779z"
          fill={palette.icon}
          opacity={active ? 0.95 : 0.42}
        />
        <text
          x="536"
          y="640"
          textAnchor="middle"
          dominantBaseline="middle"
          fill={palette.text}
          fontFamily="JetBrains Mono, Fira Code, Consolas, monospace"
          fontSize="210"
          fontWeight="900"
        >
          {resolvedTier}
        </text>
      </svg>
    </span>
  )
}
