import { Avatar, Badge, Button, Center, Group, Modal, Stack, Text, TextInput, Title } from '@mantine/core'
import { showNotification } from '@mantine/notifications'
import { mdiAccountGroup, mdiAccountMultiplePlus, mdiCheck, mdiClose, mdiCrown, mdiHumanGreetingVariant, mdiPencil } from '@mdi/js'
import { Icon } from '@mdi/react'
import { FC, useEffect, useMemo, useState } from 'react'
import { useTranslation } from 'react-i18next'
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

const heroTitle = '队伍管理'
const heroDesc = '查看我的参赛队伍、队员身份、队长权限与邀请加入状态。'
const ownedLabel = '我创建的队伍'
const allTeamsLabel = '已加入队伍'
const loadingDescription = '正在读取队伍信息'
const memberSectionTitle = '队伍管理'
const captainLabel = '队长'
const memberLabel = '队员'

const Teams: FC = () => {
  const { user, error: userError } = useUser()
  const { teams, mutate: mutateTeams, error: teamsError } = useTeams()

  const [joinOpened, setJoinOpened] = useState(false)
  const [joinTeamCode, setJoinTeamCode] = useState('')

  const [createOpened, setCreateOpened] = useState(false)
  const [editOpened, setEditOpened] = useState(false)

  const [editTeam, setEditTeam] = useState<TeamInfoModel | null>(null)
  const [selectedTeamId, setSelectedTeamId] = useState<number | undefined>()

  const teamsOwned = teams?.filter((team) => team.members?.some((member) => member?.captain && member.id === user?.userId))
  const disallowCreate = (teamsOwned?.length ?? 0) >= 3
  const selectedTeam = useMemo(
    () => teams?.find((team) => team.id === selectedTeamId) ?? teams?.[0],
    [selectedTeamId, teams]
  )
  const selectedMembers = useMemo(
    () => [...(selectedTeam?.members ?? [])].sort((left, right) => Number(Boolean(right.captain)) - Number(Boolean(left.captain))),
    [selectedTeam?.members]
  )
  const selectedCaptain = selectedMembers.find((member) => member.captain)
  const currentMember = selectedMembers.find((member) => member.id === user?.userId)
  const selectedIsCaptain = Boolean(currentMember?.captain)
  const totalMembers = teams?.reduce((sum, team) => sum + (team.members?.length ?? 0), 0) ?? 0

  const isMobile = useIsMobile()

  const { t } = useTranslation()

  usePageTitle(t('team.title.index'))

  useEffect(() => {
    if (!teams?.length) {
      setSelectedTeamId(undefined)
      return
    }

    if (!selectedTeamId || !teams.some((team) => team.id === selectedTeamId)) {
      setSelectedTeamId(teams[0].id)
    }
  }, [selectedTeamId, teams])

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
              {selectedTeam && (
                <Group className="yy-team-current" gap="md" wrap="nowrap">
                  <Avatar src={selectedTeam.avatar} alt={selectedTeam.name ?? 'team'} radius="xl" size={76} className="yy-team-current-avatar">
                    {selectedTeam.name?.slice(0, 1) ?? 'T'}
                  </Avatar>
                  <div>
                    <Group gap="xs" wrap="wrap">
                      <Title order={2}>{selectedTeam.name ?? 'team'}</Title>
                      <Badge className="yy-team-role-badge" leftSection={<Icon path={selectedIsCaptain ? mdiCrown : mdiAccountGroup} size={0.8} />}>
                        {selectedIsCaptain ? captainLabel : memberLabel}
                      </Badge>
                    </Group>
                    <Text>{selectedTeam.bio || '暂无队伍简介'}</Text>
                  </div>
                </Group>
              )}
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
              <div>
                <span>{'当前队员'}</span>
                <strong>{selectedMembers.length || '-'}</strong>
              </div>
              <div>
                <span>{'队长'}</span>
                <strong>{selectedCaptain?.userName ?? '-'}</strong>
              </div>
            </div>
          </section>

          {teams && !teamsError && user && !userError ? (
            teams.length > 0 ? (
              <>
                <section className="yy-team-switcher" aria-label="team selector">
                  {teams.map((team) => {
                    const captain = team.members?.some((member) => member?.captain && member.id === user.userId) ?? false

                    return (
                      <button
                        key={team.id ?? team.name}
                        type="button"
                        className={`yy-team-switch-card ${team.id === selectedTeam?.id ? 'is-active' : ''}`}
                        onClick={() => setSelectedTeamId(team.id)}
                      >
                        <Avatar src={team.avatar} alt={team.name ?? 'team'} radius="xl" size={40}>
                          {team.name?.slice(0, 1) ?? 'T'}
                        </Avatar>
                        <span>
                          <strong>{team.name ?? 'team'}</strong>
                          <small>{captain ? captainLabel : memberLabel}</small>
                        </span>
                      </button>
                    )
                  })}
                </section>

                <section className="panel-card yy-team-management-panel">
                  <YinyuHexField cells={42} />
                  <Group justify="space-between" align="flex-start" className="yy-team-management-head">
                    <div>
                      <span className="yy-section-kicker">ROSTER</span>
                      <Title order={2}>{memberSectionTitle}</Title>
                      <Text>{'按队长和队员区分展示成员。队长可进入详情维护队伍信息与成员权限。'}</Text>
                    </div>
                    <Group className="yy-team-actions" justify={isMobile ? 'stretch' : 'right'} grow={isMobile}>
                      {btns}
                      {selectedTeam && (
                        <Button
                          leftSection={<Icon path={mdiPencil} size={1} />}
                          className="yy-team-action yy-team-action-create"
                          onClick={() => onEditTeam(selectedTeam)}
                        >
                          {selectedIsCaptain ? t('team.button.edit') : '查看详情'}
                        </Button>
                      )}
                    </Group>
                  </Group>

                  <div className="yy-team-member-summary">
                    <div>
                      <span>{'我的身份'}</span>
                      <strong>{selectedIsCaptain ? captainLabel : memberLabel}</strong>
                    </div>
                    <div>
                      <span>{'当前队伍'}</span>
                      <strong>{selectedTeam?.name ?? '-'}</strong>
                    </div>
                    <div>
                      <span>{'全部队员'}</span>
                      <strong>{selectedMembers.length}</strong>
                    </div>
                    <div>
                      <span>{'我的总队员档案'}</span>
                      <strong>{totalMembers}</strong>
                    </div>
                  </div>

                  <div className="yy-team-roster-list">
                    {selectedMembers.map((member) => (
                      <article key={member.id ?? member.userName} className={`yy-team-roster-row ${member.captain ? 'is-captain' : ''}`}>
                        <Avatar src={member.avatar} alt={member.userName ?? 'user'} radius="xl" size={52}>
                          {member.userName?.slice(0, 1) ?? 'U'}
                        </Avatar>
                        <div className="yy-team-roster-user">
                          <strong>{member.userName ?? 'user'}</strong>
                          <span>{member.bio || '暂无个人简介'}</span>
                        </div>
                        <Badge className="yy-team-role-badge" leftSection={<Icon path={member.captain ? mdiCrown : mdiAccountGroup} size={0.78} />}>
                          {member.captain ? captainLabel : memberLabel}
                        </Badge>
                      </article>
                    ))}
                  </div>
                </section>
              </>
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
