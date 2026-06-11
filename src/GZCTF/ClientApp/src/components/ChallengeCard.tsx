import { Box, Code, Group, Stack, Text, Tooltip } from '@mantine/core'
import { Icon } from '@mdi/react'
import cx from 'clsx'
import dayjs from 'dayjs'
import { ChevronRight } from 'lucide-react'
import { FC, useMemo } from 'react'
import { Trans } from 'react-i18next'
import { useLanguage } from '@Utils/I18n'
import { BloodsTypes, PartialIconProps, useChallengeCategoryLabelMap } from '@Utils/Shared'
import { ChallengeInfo, SubmissionType } from '@Api'
import classes from '@Styles/ChallengeCard.module.css'
import { YinyuDataBar, YinyuHexField, YinyuStatusPill } from './yinyu/YinyuUI'

interface ChallengeCardProps {
  challenge: ChallengeInfo
  solved?: boolean
  onClick?: () => void
  iconMap: Map<SubmissionType, PartialIconProps | undefined>
  colorMap: Map<SubmissionType, string | undefined>
  teamId?: number
}

export const ChallengeCard: FC<ChallengeCardProps> = (props: ChallengeCardProps) => {
  const { challenge, solved, onClick, iconMap, teamId, colorMap } = props
  const challengeCategoryLabelMap = useChallengeCategoryLabelMap()
  const cateData = challengeCategoryLabelMap.get(challenge.category!)
  const { locale } = useLanguage()

  const isFaded = useMemo(() => {
    if (!challenge.deadline) return false
    return dayjs().isAfter(dayjs(challenge.deadline))
  }, [challenge.deadline])

  const heat = Math.min(100, Math.max(8, (challenge.solved ?? 0) * 8))
  const state = solved ? 'solved' : isFaded ? 'alert' : 'open'

  return (
    <button
      onClick={onClick}
      type="button"
      className={cx('challenge-card panel-card', solved && 'is-active')}
      data-faded={solved || isFaded || undefined}
    >
      <YinyuHexField cells={28} />
      <span className="challenge-category">{cateData?.name ?? challenge.category}</span>
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
        <YinyuStatusPill tone={solved ? 'success' : isFaded ? 'danger' : 'neutral'} state={state}>
          {solved ? '已解出' : isFaded ? '已截止' : '开放'}
        </YinyuStatusPill>
        <Group justify="center" gap="sm" h={20} wrap="nowrap">
          {challenge.bloods?.map((blood, idx) => {
            const iconProps = iconMap.get(BloodsTypes[idx])!
            return (
              <Tooltip.Floating
                key={idx}
                position="bottom"
                multiline
                label={
                  <Stack gap={0}>
                    <Text fw={500} size="sm">
                      {blood?.name}
                    </Text>
                    <Text fw={500} size="xs" c="dimmed">
                      {dayjs(blood?.submitTimeUtc).locale(locale).format('SLL LTS')}
                    </Text>
                  </Stack>
                }
              >
                <span style={{ position: 'relative', height: 20 }}>
                  <span className={classes.blood}>
                    <Icon {...iconProps} />
                  </span>
                  <Box
                    className={classes.spike}
                    data-blood={teamId === blood?.id || undefined}
                    __vars={{
                      '--blood-color': colorMap.get(BloodsTypes[idx]),
                    }}
                  />
                </span>
              </Tooltip.Floating>
            )
          })}
        </Group>
        <ChevronRight size={16} />
      </div>
    </button>
  )
}
