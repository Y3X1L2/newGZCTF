import { Avatar, Card, Stack, Title } from '@mantine/core'
import { FC, KeyboardEvent, memo } from 'react'
import { useYinyuMagicBento } from '@Components/yinyu/YinyuReactBits'
import { YinyuHexField } from '@Components/yinyu/YinyuUI'
import { TeamInfoModel } from '@Api'
import teamCardClasses from '@Styles/TeamCard.module.css'

interface TeamCardProps {
  team: TeamInfoModel
  isCaptain: boolean
  onEdit: () => void
}

const actionLabel = '\u67e5\u770b\u961f\u4f0d\u8be6\u60c5'

export const TeamCard: FC<TeamCardProps> = memo((props) => {
  const { team, onEdit } = props
  const bento = useYinyuMagicBento<HTMLDivElement>()

  const onKeyDown = (event: KeyboardEvent<HTMLDivElement>) => {
    if (event.key !== 'Enter' && event.key !== ' ') return

    event.preventDefault()
    onEdit()
  }

  return (
    <Card
      ref={bento.ref}
      shadow="md"
      radius="lg"
      onClick={onEdit}
      onKeyDown={onKeyDown}
      onPointerEnter={bento.onPointerEnter}
      onPointerMove={bento.onPointerMove}
      onPointerLeave={bento.onPointerLeave}
      role="button"
      tabIndex={0}
      aria-label={`${actionLabel}: ${team.name ?? 'team'}`}
      className={`panel-card yy-magic-bento-card ${teamCardClasses.card}`}
    >
      <YinyuHexField cells={24} />
      <div className={teamCardClasses.scanGlow} />

      <Stack className={teamCardClasses.content} gap="sm" align="center" justify="center">
        <div className={teamCardClasses.avatarShell}>
          <Avatar alt="avatar" size={64} radius="xl" src={team.avatar} className={teamCardClasses.avatar}>
            {team.name?.slice(0, 1) ?? 'T'}
          </Avatar>
        </div>

        <Title order={3} className={teamCardClasses.title} title={team.name ?? undefined}>
          {team.name ?? 'team'}
        </Title>
      </Stack>
    </Card>
  )
})
