import { Code, Group, Stack, Text, Tooltip } from '@mantine/core'
import cx from 'clsx'
import dayjs from 'dayjs'
import { ChevronRight } from 'lucide-react'
import { CSSProperties, memo, useMemo } from 'react'
import { Trans } from 'react-i18next'
import { BloodMedal, bloodTierLabel } from '@Components/BloodMedal'
import { useLanguage } from '@Utils/I18n'
import { BloodsTypes, useChallengeCategoryLabelMap } from '@Utils/Shared'
import { ChallengeInfo } from '@Api'
import classes from '@Styles/ChallengeCard.module.css'
import { YinyuStatusText } from './yinyu/YinyuReactBits'
import { YinyuDataBar } from './yinyu/YinyuUI'

interface ChallengeCardProps {
  challenge: ChallengeInfo
  solved?: boolean
  onClick?: () => void
  teamId?: number
  instanceActive?: boolean
}

export const ChallengeCard = memo(function ChallengeCard(props: ChallengeCardProps) {
  const { challenge, solved, onClick, teamId, instanceActive } = props
  const challengeCategoryLabelMap = useChallengeCategoryLabelMap()
  const cateData = challengeCategoryLabelMap.get(challenge.category!)
  const { locale } = useLanguage()

  const isFaded = useMemo(() => {
    if (!challenge.deadline) return false
    return dayjs().isAfter(dayjs(challenge.deadline))
  }, [challenge.deadline])

  const heat = Math.min(100, Math.max(8, (challenge.solved ?? 0) * 8))
  const categoryColor = cateData?.colors?.[5] ?? 'rgba(107, 238, 177, 0.88)'
  const categorySoftColor = cateData?.colors?.[8] ?? 'rgba(107, 238, 177, 0.16)'

  return (
    <button
      onClick={onClick}
      type="button"
      className={cx('challenge-card panel-card', solved && 'is-active')}
      data-faded={solved || isFaded || undefined}
      data-instance-active={instanceActive || undefined}
    >
      <span
        className="challenge-category yy-challenge-category-token"
        style={
          {
            '--challenge-category-color': categoryColor,
            '--challenge-category-soft': categorySoftColor,
          } as CSSProperties
        }
      >
        {cateData?.name ?? challenge.category}
      </span>
      <strong>{challenge.title}</strong>
      <div className="challenge-meta">
        <span>{challenge.score}&nbsp;pts</span>
        <span>
          <Trans i18nKey="challenge.content.solved" values={{ solved: challenge.solved }}>
            _
            <Code fz="sm" fw="bolder" bg="transparent">
              _
            </Code>
            _
          </Trans>
        </span>
      </div>
      <YinyuDataBar value={heat} />
      <div className="challenge-foot">
        <YinyuStatusText tone={solved ? 'success' : isFaded ? 'danger' : 'neutral'} className="yy-challenge-state-text">
          {solved ? '已解出' : isFaded ? '已截止' : '开放'}
        </YinyuStatusText>
        <Group justify="center" gap={7} h={28} wrap="nowrap" className={classes.bloodRack}>
          {BloodsTypes.map((type, index) => {
            const blood = challenge.bloods?.[index]
            const tier = (index + 1) as 1 | 2 | 3
            const label = bloodTierLabel(tier)

            return (
              <Tooltip
                key={type}
                position="bottom"
                withArrow
                withinPortal
                multiline
                label={
                  <Stack gap={0}>
                    <Text fw={600} size="sm">
                      {blood?.name ?? `暂无${label}`}
                    </Text>
                    {blood?.submitTimeUtc && (
                      <Text fw={500} size="xs" className="yy-readable-text">
                        {dayjs(blood.submitTimeUtc).locale(locale).format('SLL LTS')}
                      </Text>
                    )}
                  </Stack>
                }
              >
                <span className={classes.bloodMedalTarget}>
                  <BloodMedal tier={tier} active={!!blood} own={teamId === blood?.id} size="sm" className={classes.bloodMedal} />
                </span>
              </Tooltip>
            )
          })}
        </Group>
        <ChevronRight size={16} />
      </div>
    </button>
  )
})
