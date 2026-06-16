import cx from 'clsx'
import { NoticeType, SubmissionType } from '@Api'

export type BloodTier = 1 | 2 | 3

const tierLabels: Record<BloodTier, string> = {
  1: '一血',
  2: '二血',
  3: '三血',
}

const tierPalettes: Record<BloodTier | 'empty', { edge: string; core: string; shade: string; text: string }> = {
  1: { edge: '#ffe082', core: '#e0ac32', shade: '#8d6415', text: '#251800' },
  2: { edge: '#f1f6f7', core: '#aebcc1', shade: '#647177', text: '#0d1416' },
  3: { edge: '#eaa66c', core: '#ad682f', shade: '#6e3714', text: '#1f1006' },
  empty: { edge: '#78847f', core: '#34413d', shade: '#1d2624', text: '#c2d0ca' },
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
      <svg viewBox="0 0 64 78" role="img" aria-hidden="true" focusable="false">
        <path d="M19 5h26l-5.8 22H24.8L19 5Z" fill={palette.shade} opacity={active ? 0.9 : 0.42} />
        <path d="M23 5h18l-3.7 20H26.7L23 5Z" fill={palette.core} opacity={active ? 0.82 : 0.36} />
        <circle cx="32" cy="48" r="23" fill={palette.core} opacity={active ? 1 : 0.52} />
        <circle cx="32" cy="48" r="23" fill="none" stroke={palette.edge} strokeOpacity={active ? 0.95 : 0.42} strokeWidth="3" />
        <path d="M15 43c5-12 23-18 36-6" fill="none" stroke="#ffffff" strokeOpacity={active ? 0.22 : 0.06} strokeWidth="5" strokeLinecap="round" />
        <path d="M17 60c9 6 23 5 30-5" fill="none" stroke={palette.shade} strokeOpacity={active ? 0.3 : 0.12} strokeWidth="5" strokeLinecap="round" />
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
