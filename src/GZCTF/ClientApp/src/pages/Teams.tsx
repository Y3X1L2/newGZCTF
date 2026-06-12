import { Button, Center, Group, Modal, Stack, Text, TextInput, Title } from '@mantine/core'
import { showNotification } from '@mantine/notifications'
import { mdiAccountMultiplePlus, mdiCheck, mdiClose, mdiHumanGreetingVariant } from '@mdi/js'
import { Icon } from '@mdi/react'
import { FC, useState } from 'react'
import { useTranslation } from 'react-i18next'
import { TeamCard } from '@Components/TeamCard'
import { TeamCreateModal } from '@Components/TeamCreateModal'
import { TeamEditModal } from '@Components/TeamEditModal'
import { WithNavBar } from '@Components/WithNavbar'
import { WithRole } from '@Components/WithRole'
import { YinyuHeartbeatIcon, YinyuHexField, YinyuLoadingState, YinyuModalBody } from '@Components/yinyu/YinyuUI'
import { showErrorMsg } from '@Utils/Shared'
import { useIsMobile } from '@Utils/ThemeOverride'
import { usePageTitle } from '@Hooks/usePageTitle'
import { useTeams, useUser } from '@Hooks/useUser'
import api, { Role, TeamInfoModel } from '@Api'

const heroTitle = '\u961f\u4f0d\u7ba1\u7406'
const heroDesc = '\u7edf\u4e00\u7ba1\u7406\u53c2\u8d5b\u961f\u4f0d\u3001\u9080\u8bf7\u4ee3\u7801\u3001\u961f\u5458\u8eab\u4efd\u4e0e\u8d5b\u4e8b\u51c6\u5165\u72b6\u6001\u3002'
const ownedLabel = '\u6211\u521b\u5efa\u7684\u961f\u4f0d'
const allTeamsLabel = '\u5df2\u52a0\u5165\u961f\u4f0d'
const loadingDescription = '\u6b63\u5728\u8bfb\u53d6\u961f\u4f0d\u4fe1\u606f'

const Teams: FC = () => {
  const { user, error: userError } = useUser()
  const { teams, mutate: mutateTeams, error: teamsError } = useTeams()

  const [joinOpened, setJoinOpened] = useState(false)
  const [joinTeamCode, setJoinTeamCode] = useState('')

  const [createOpened, setCreateOpened] = useState(false)
  const [editOpened, setEditOpened] = useState(false)

  const [editTeam, setEditTeam] = useState<TeamInfoModel | null>(null)

  const teamsOwned = teams?.filter((team) => team.members?.some((member) => member?.captain && member.id === user?.userId))
  const disallowCreate = (teamsOwned?.length ?? 0) >= 3

  const isMobile = useIsMobile()

  const { t } = useTranslation()

  usePageTitle(t('team.title.index'))

  const onEditTeam = (team: TeamInfoModel) => {
    setEditTeam(team)
    setEditOpened(true)
  }

  const codePattern = /:\d+:[0-9a-f]{32}$/

  const onJoinTeam = async () => {
    if (!codePattern.test(joinTeamCode)) {
      showNotification({
        color: 'red',
        title: t('common.error.encountered'),
        message: t('team.notification.join.wrong_invite_code'),
        icon: <Icon path={mdiClose} size={1} />,
      })
      return
    }

    try {
      await api.team.teamAccept(joinTeamCode)
      showNotification({
        color: 'teal',
        title: t('team.notification.join.success'),
        message: t('team.notification.updated'),
        icon: <Icon path={mdiCheck} size={1} />,
      })
      mutateTeams()
    } catch (e) {
      showErrorMsg(e, t)
    } finally {
      setJoinTeamCode('')
      setJoinOpened(false)
    }
  }

  const btns = (
    <>
      <Button
        leftSection={<Icon path={mdiHumanGreetingVariant} size={1} />}
        variant="outline"
        className="yy-team-action yy-team-action-join"
        onClick={() => setJoinOpened(true)}
      >
        {t('team.button.join')}
      </Button>
      <Button
        leftSection={<Icon path={mdiAccountMultiplePlus} size={1} />}
        variant="filled"
        className="yy-team-action yy-team-action-create"
        onClick={() => setCreateOpened(true)}
      >
        {t('team.button.create')}
      </Button>
    </>
  )

  return (
    <WithNavBar minWidth={0} width="var(--container)">
      <WithRole requiredRole={Role.User}>
        <Stack className="yy-page-frame view-stack yy-soft-enter yy-team-page">
          <section className="panel-card yy-team-hero">
            <YinyuHexField cells={34} />
            <div className="yy-team-hero-left">
              <div className="yy-team-hero-copy">
                <span className="yy-section-kicker">TEAM CENTER</span>
                <Title order={1} className="yy-brand-title">
                  {heroTitle}
                </Title>
                <Text>{heroDesc}</Text>
              </div>
              <Group className="yy-team-actions" justify={isMobile ? 'stretch' : 'left'} grow={isMobile}>
                {btns}
              </Group>
            </div>
            <div className="yy-team-hero-stats" aria-label="team status summary">
              <div>
                <span>{allTeamsLabel}</span>
                <strong>{teams?.length ?? '-'}</strong>
              </div>
              <div>
                <span>{ownedLabel}</span>
                <strong>{teamsOwned?.length ?? '-'}</strong>
              </div>
            </div>
          </section>

          {teams && !teamsError && user && !userError ? (
            teams.length > 0 ? (
              <div className="yy-team-grid">
                {teams.map((team) => (
                  <TeamCard
                    key={team.id ?? team.name}
                    team={team}
                    isCaptain={team.members?.some((member) => member?.captain && member.id === user?.userId) ?? false}
                    onEdit={() => onEditTeam(team)}
                  />
                ))}
              </div>
            ) : (
              <Center w="100%" mih="48vh" className="state-card panel-card yy-team-empty-state">
                <YinyuHexField cells={30} />
                <Stack align="center" gap="md" maw={isMobile ? '90%' : '100%'}>
                  <YinyuHeartbeatIcon label="team empty signal" />
                  <Icon path={mdiAccountMultiplePlus} size={4} />
                  <Title order={2} ta="center" style={{ wordBreak: 'break-word', hyphens: 'auto' }}>
                    {t('team.content.no_team.title')}
                  </Title>
                  <Text size="sm" className="yy-readable-text" ta="center" style={{ wordBreak: 'break-word', hyphens: 'auto' }}>
                    {t('team.content.no_team.hint')}
                  </Text>
                </Stack>
              </Center>
            )
          ) : (
            <YinyuLoadingState title={t('team.title.index')} description={loadingDescription} />
          )}
        </Stack>

        <Modal opened={joinOpened} title={t('team.button.join')} onClose={() => setJoinOpened(false)}>
          <YinyuModalBody>
            <Text size="sm">{t('team.content.join')}</Text>
            <TextInput
              label={t('team.label.invite_code')}
              type="text"
              placeholder="team:0:01234567890123456789012345678901"
              w="100%"
              value={joinTeamCode}
              onChange={(event) => setJoinTeamCode(event.currentTarget.value)}
            />
            <Button fullWidth variant="outline" className="yy-team-action yy-team-action-join" onClick={onJoinTeam}>
              {t('team.button.join')}
            </Button>
          </YinyuModalBody>
        </Modal>

        <TeamCreateModal
          opened={createOpened}
          title={t('team.button.create')}
          disallowCreate={disallowCreate ?? false}
          onClose={() => setCreateOpened(false)}
          mutate={mutateTeams}
        />

        <TeamEditModal
          opened={editOpened}
          title={t('team.button.edit')}
          onClose={() => setEditOpened(false)}
          team={editTeam}
          isCaptain={editTeam?.members?.some((member) => member?.captain && member.id === user?.userId) ?? false}
        />
      </WithRole>
    </WithNavBar>
  )
}

export default Teams
