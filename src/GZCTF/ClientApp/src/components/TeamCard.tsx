import { Avatar, Card, Group, Stack, Text, Title, Tooltip } from '@mantine/core'
import { mdiAccountGroupOutline, mdiChevronRight, mdiCrown, mdiLockOutline, mdiShieldAccountOutline } from '@mdi/js'
import { Icon } from '@mdi/react'
import { FC, KeyboardEvent } from 'react'
import { useTranslation } from 'react-i18next'
import { YinyuHexField } from '@Components/yinyu/YinyuUI'
import { useIsMobile } from '@Utils/ThemeOverride'
import { TeamInfoModel } from '@Api'
import teamCardClasses from '@Styles/TeamCard.module.css'

interface TeamCardProps {
  team: TeamInfoModel
  isCaptain: boolean
  onEdit: () => void
}

const fallbackBio = '\u8fd9\u652f\u961f\u4f0d\u6682\u672a\u586b\u5199\u7b80\u4ecb'
const captainLabel = '\u961f\u957f'
const memberLabel = '\u6210\u5458'
const lockedLabel = '\u5df2\u9501\u5b9a'
const openLabel = '\u53ef\u7ba1\u7406'
const actionLabel = '\u67e5\u770b\u961f\u4f0d\u8be6\u60c5'

export const TeamCard: FC<TeamCardProps> = (props) => {
  const { team, isCaptain, onEdit } = props

  const { t } = useTranslation()
  const isMobile = useIsMobile()
  const members = team.members ?? []
  const captain = members.find((m) => m?.captain)

  const onKeyDown = (event: KeyboardEvent<HTMLDivElement>) => {
    if (event.key !== 'Enter' && event.key !== ' ') return

    event.preventDefault()
    onEdit()
  }

  return (
    <Card
      shadow="md"
      radius="lg"
      onClick={onEdit}
      onKeyDown={onKeyDown}
      role="button"
      tabIndex={0}
      aria-label={`${actionLabel}: ${team.name ?? 'team'}`}
      className={`panel-card ${isMobile ? teamCardClasses.cardMobile : teamCardClasses.card}`}
    >
      <YinyuHexField cells={34} />
      <div className={teamCardClasses.scanGlow} />

      <Stack className={teamCardClasses.content} gap="md">
        <Group className={teamCardClasses.top} wrap="nowrap" align="flex-start">
          <div className={teamCardClasses.avatarShell}>
            <Avatar alt="avatar" size={isMobile ? 68 : 82} radius="xl" src={team.avatar} className={teamCardClasses.avatar}>
              {team.name?.slice(0, 1) ?? 'T'}
            </Avatar>
          </div>

          <Stack gap={7} className={teamCardClasses.identity}>
            <Group gap="xs" justify="space-between" wrap="nowrap" align="flex-start">
              <Title order={2} className={teamCardClasses.title} title={team.name ?? undefined}>
                {team.name}
              </Title>
              <span className={teamCardClasses.openCue} aria-hidden="true">
                <Icon path={mdiChevronRight} size={0.9} />
              </span>
            </Group>

            <Text className={teamCardClasses.bio} lineClamp={2}>
              {team.bio || t('team.placeholder.bio', { defaultValue: fallbackBio })}
            </Text>

            <Group gap="xs" className={teamCardClasses.badgeRow}>
              {isCaptain && (
                <span className={`${teamCardClasses.statusBadge} ${teamCardClasses.statusWarm}`}>
                  <Icon path={mdiCrown} size={0.62} />
                  {captainLabel}
                </span>
              )}
              <span className={`${teamCardClasses.statusBadge} ${team.locked ? teamCardClasses.statusLocked : teamCardClasses.statusGreen}`}>
                <Icon path={team.locked ? mdiLockOutline : mdiShieldAccountOutline} size={0.62} />
                {team.locked ? lockedLabel : openLabel}
              </span>
            </Group>
          </Stack>
        </Group>

        <div className={teamCardClasses.metrics}>
          <div>
            <span>{memberLabel}</span>
            <strong>{members.length}</strong>
          </div>
          <div>
            <span>{captainLabel}</span>
            <strong>{captain?.userName ?? '-'}</strong>
          </div>
        </div>

        <Group justify="space-between" align="center" gap="sm" wrap="nowrap" className={teamCardClasses.footer}>
          <Group gap={0} wrap="nowrap" className={teamCardClasses.memberRail}>
            {members.slice(0, 7).map((m) => (
              <Tooltip key={m.id} label={m.userName} withArrow>
                <Avatar alt="avatar" radius="xl" size="md" src={m.avatar}>
                  {m.userName?.slice(0, 1) ?? 'U'}
                </Avatar>
              </Tooltip>
            ))}
            {members.length > 7 && (
              <Avatar radius="xl" size="md" className={teamCardClasses.moreAvatar}>
                +{members.length - 7}
              </Avatar>
            )}
          </Group>

          <span className={teamCardClasses.actionText}>
            <Icon path={mdiAccountGroupOutline} size={0.74} />
            {actionLabel}
          </span>
        </Group>
      </Stack>
    </Card>
  )
}
