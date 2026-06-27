import {
  ActionIcon,
  Avatar,
  Badge,
  BoxProps,
  Group,
  Stack,
  Text,
  useMantineTheme,
} from '@mantine/core'
import { mdiDownload } from '@mdi/js'
import { Icon } from '@mdi/react'
import dayjs from 'dayjs'
import { FC } from 'react'
import { Link } from 'react-router'
import { ScrollingText } from '@Components/ScrollingText'
import { YinyuPanel } from '@Components/yinyu/YinyuUI'
import { useLanguage } from '@Utils/I18n'
import { WriteupInfo } from '@Api'
import misc from '@Styles/Misc.module.css'

interface TeamWriteupCardProps extends BoxProps {
  writeup: WriteupInfo
  selected?: boolean
  onClick: () => void
  divisionName?: string
}

export const TeamWriteupCard: FC<TeamWriteupCardProps> = ({ writeup, selected, divisionName, ...props }) => {
  const { locale } = useLanguage()
  const theme = useMantineTheme()
  const borderColor = selected ? theme.colors[theme.primaryColor][8] : 'transparent'

  return (
    <YinyuPanel {...props} p="sm" className={misc.hoverCard} bd={`2px solid ${borderColor}`} data-no-move>
      <Group wrap="nowrap" gap={3} justify="space-between">
        <Group gap="sm" wrap="nowrap" justify="space-between" maw="calc(100% - 2rem)">
          <Avatar alt="avatar" src={writeup.team?.avatar} size="md">
            {writeup.team?.name?.slice(0, 1)}
          </Avatar>
          <Stack gap={0} justify="space-between" maw="calc(100% - 3rem)">
            <Group gap="xs">
              <Text size="0.8rem" lineClamp={1} className="yy-readable-text">
                #{writeup.team?.id}
              </Text>
              {divisionName && (
                <Badge size="xs" variant="light">
                  {divisionName}
                </Badge>
              )}
            </Group>
            <ScrollingText size="md" fw={600} text={writeup.team?.name ?? ''} />
            <Text size="xs" lineClamp={1} className="yy-readable-text">
              {dayjs(writeup.uploadTimeUtc).locale(locale).format('SLL LT')}
            </Text>
          </Stack>
        </Group>
        <ActionIcon component={Link} target="_blank" rel="noopener noreferrer" to={writeup.url ?? '#'}>
          <Icon path={mdiDownload} size={1} />
        </ActionIcon>
      </Group>
    </YinyuPanel>
  )
}
