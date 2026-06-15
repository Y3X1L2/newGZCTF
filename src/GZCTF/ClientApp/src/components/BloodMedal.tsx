import cx from 'clsx'
import { useId } from 'react'
import { NoticeType, SubmissionType } from '@Api'

export type BloodTier = 1 | 2 | 3

const tierLabels: Record<BloodTier, string> = {
  1: '一血',
  2: '二血',
  3: '三血',
}

const tierPalettes: Record<BloodTier | 'empty', { edge: string; core: string; shine: string; text: string }> = {
  1: { edge: '#fff1a6', core: '#f2c84b', shine: '#fff7cf', text: '#261900' },
  2: { edge: '#f4fbff', core: '#c7d4db', shine: '#ffffff', text: '#11191b' },
  3: { edge: '#f2b174', core: '#c17434', shine: '#ffdfb8', text: '#231207' },
  empty: { edge: '#697974', core: '#283431', shine: '#c6d8d0', text: '#cadbd3' },
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
  const uid = useId().replace(/:/g, '')
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
      <svg viewBox="0 0 64 78" role="img" aria-hidden="true" focusable="false">
        <defs>
          <linearGradient id={`yy-medal-ribbon-${uid}`} x1="12" y1="5" x2="52" y2="35" gradientUnits="userSpaceOnUse">
            <stop offset="0" stopColor={palette.shine} stopOpacity={active ? 0.78 : 0.24} />
            <stop offset="0.42" stopColor={palette.core} stopOpacity={active ? 0.95 : 0.46} />
            <stop offset="1" stopColor={palette.edge} stopOpacity={active ? 0.72 : 0.3} />
          </linearGradient>
          <radialGradient id={`yy-medal-face-${uid}`} cx="30%" cy="22%" r="78%">
            <stop offset="0" stopColor={palette.shine} stopOpacity={active ? 0.96 : 0.36} />
            <stop offset="0.38" stopColor={palette.core} stopOpacity={active ? 0.98 : 0.48} />
            <stop offset="1" stopColor={palette.edge} stopOpacity={active ? 0.9 : 0.34} />
          </radialGradient>
          <linearGradient id={`yy-medal-sheen-${uid}`} x1="12" y1="20" x2="48" y2="68" gradientUnits="userSpaceOnUse">
            <stop offset="0" stopColor="#ffffff" stopOpacity={active ? 0.58 : 0.16} />
            <stop offset="0.5" stopColor="#ffffff" stopOpacity="0" />
            <stop offset="1" stopColor="#ffffff" stopOpacity={active ? 0.24 : 0.06} />
          </linearGradient>
          <filter id={`yy-medal-shadow-${uid}`} x="-40%" y="-30%" width="180%" height="180%">
            <feDropShadow dx="0" dy="4" stdDeviation="4" floodColor={palette.core} floodOpacity={active ? 0.38 : 0.12} />
            <feDropShadow dx="0" dy="9" stdDeviation="7" floodColor="#000000" floodOpacity="0.32" />
          </filter>
        </defs>
        <g filter={`url(#yy-medal-shadow-${uid})`}>
          <path d="M18 4h28l-6.4 23.2H24.4L18 4Z" fill={`url(#yy-medal-ribbon-${uid})`} opacity={active ? 0.94 : 0.46} />
          <path d="M23 4h18l-4 20H27L23 4Z" fill="#ffffff" opacity={active ? 0.12 : 0.04} />
          <circle cx="32" cy="48" r="24" fill={`url(#yy-medal-face-${uid})`} />
          <circle cx="32" cy="48" r="20" fill="none" stroke={palette.edge} strokeOpacity={active ? 0.74 : 0.28} strokeWidth="2" />
          <path d="M18 38c5.8-9.2 22.6-12.2 31.6-2.2C40.4 33.8 29.2 38.2 18 38Z" fill="#ffffff" opacity={active ? 0.2 : 0.06} />
          <path d="M17 64c10.5-2.5 23.2 0.8 31.2-10.8C45.8 67.2 26.2 74.5 17 64Z" fill={`url(#yy-medal-sheen-${uid})`} />
        </g>
        <text
          x="32"
          y="51"
          textAnchor="middle"
          dominantBaseline="middle"
          fill={palette.text}
          fontFamily="JetBrains Mono, Fira Code, Consolas, monospace"
          fontSize="20"
          fontWeight="900"
        >
          {resolvedTier}
        </text>
        <text
          x="32"
          y="63"
          textAnchor="middle"
          dominantBaseline="middle"
          fill={palette.text}
          fontFamily="Noto Sans SC, Microsoft YaHei, sans-serif"
          fontSize="7"
          fontWeight="900"
          opacity={active ? 0.88 : 0.56}
        >
          {tierLabels[resolvedTier]}
        </text>
      </svg>
    </span>
  )
}
