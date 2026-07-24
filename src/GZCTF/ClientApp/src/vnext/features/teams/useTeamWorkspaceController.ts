import { useEffect, useState } from 'react'
import { useSearchParams } from 'react-router'
import { errorMessage } from '../../shared/errors'
import { useCurrentAccount } from '../account/useCurrentAccount'
import { TeamConfirmation } from './TeamConfirmationDialog'
import { teamApi, useCurrentTeams, useTeamDetails, useTeamInviteCode, useTeamJoinRequests } from './teamApi'
import { parseTeamId, TeamFeedback, TeamTab, validTeamTabs } from './teamTypes'

export function useTeamWorkspaceController() {
  const account = useCurrentAccount()
  const [searchParams, setSearchParams] = useSearchParams()
  const selectedId = parseTeamId(searchParams.get('team'))
  const requestedTab = searchParams.get('tab') as TeamTab | null
  const activeTab = requestedTab && validTeamTabs.has(requestedTab) ? requestedTab : 'overview'
  const teamsRequest = useCurrentTeams(account.isAuthenticated)
  const detailRequest = useTeamDetails(selectedId ?? 0, Boolean(selectedId))
  const selectedTeam = detailRequest.data ?? teamsRequest.data?.find((team) => team.id === selectedId)
  const isCaptain = Boolean(
    selectedTeam?.members?.some((member) => member.id === account.user?.userId && member.captain)
  )
  const requestsRequest = useTeamJoinRequests(
    selectedId ?? 0,
    Boolean(selectedId && isCaptain && activeTab === 'requests')
  )
  const inviteRequest = useTeamInviteCode(selectedId ?? 0, Boolean(selectedId && isCaptain && activeTab === 'overview'))

  const [createOpen, setCreateOpen] = useState(false)
  const [joinOpen, setJoinOpen] = useState(false)
  const [searchOpen, setSearchOpen] = useState(false)
  const [editName, setEditName] = useState('')
  const [editBio, setEditBio] = useState('')
  const [avatarFile, setAvatarFile] = useState<File | null>(null)
  const [submitting, setSubmitting] = useState(false)
  const [mobileDetailOpen, setMobileDetailOpen] = useState(Boolean(selectedId))
  const [confirmation, setConfirmation] = useState<TeamConfirmation | null>(null)
  const [feedback, setFeedback] = useState<TeamFeedback | null>(null)

  useEffect(() => {
    const teams = teamsRequest.data
    if (!teams?.length) return
    if (selectedId && teams.some((team) => team.id === selectedId)) return
    const next = new URLSearchParams(searchParams)
    next.set('team', String(teams[0].id))
    next.delete('tab')
    setSearchParams(next, { replace: true })
  }, [searchParams, selectedId, setSearchParams, teamsRequest.data])

  useEffect(() => {
    if (!selectedTeam) return
    setEditName(selectedTeam.name ?? '')
    setEditBio(selectedTeam.bio ?? '')
  }, [selectedTeam])

  const selectTeam = (id: number) => {
    const next = new URLSearchParams(searchParams)
    next.set('team', String(id))
    next.delete('tab')
    setSearchParams(next)
    setMobileDetailOpen(true)
  }

  const setTab = (tab: TeamTab) => {
    const next = new URLSearchParams(searchParams)
    if (tab === 'overview') next.delete('tab')
    else next.set('tab', tab)
    setSearchParams(next)
  }

  const refreshTeamData = async () => {
    await Promise.all([
      teamsRequest.mutate(),
      detailRequest.mutate(),
      isCaptain ? requestsRequest.mutate() : Promise.resolve(),
    ])
  }

  const runMutation = async (operation: () => Promise<void>, fallback: string, success?: string) => {
    setSubmitting(true)
    setFeedback(null)
    try {
      await operation()
      if (success) setFeedback({ tone: 'success', message: success })
      return true
    } catch (error) {
      setFeedback({ tone: 'danger', message: errorMessage(error, fallback) })
      return false
    } finally {
      setSubmitting(false)
    }
  }

  const saveTeam = async () => {
    if (!selectedTeam?.id) return
    await runMutation(
      async () => {
        await teamApi.update(selectedTeam.id!, { name: editName.trim(), bio: editBio.trim() })
        await refreshTeamData()
      },
      '战队资料保存失败。',
      '战队资料已保存。'
    )
  }

  const uploadAvatar = async () => {
    if (!selectedTeam?.id || !avatarFile) return
    await runMutation(
      async () => {
        await teamApi.uploadAvatar(selectedTeam.id!, avatarFile)
        setAvatarFile(null)
        await refreshTeamData()
      },
      '战队头像上传失败。',
      '战队头像已更新。'
    )
  }

  const reviewRequest = async (requestId: number | undefined, accepted: boolean) => {
    if (!selectedTeam?.id || !requestId) return
    await runMutation(async () => {
      await teamApi.reviewJoinRequest(selectedTeam.id!, requestId, accepted)
      await refreshTeamData()
    }, '申请处理失败。')
  }

  const runMemberAction = async (kind: 'kick' | 'transfer', userId?: string | null) => {
    if (!selectedTeam?.id || !userId) return false
    return runMutation(
      async () => {
        if (kind === 'kick') await teamApi.kickMember(selectedTeam.id!, userId)
        else await teamApi.transferCaptain(selectedTeam.id!, userId)
        await refreshTeamData()
      },
      kind === 'kick' ? '移除成员失败。' : '队长转让失败。'
    )
  }

  const clearSelection = () => {
    const next = new URLSearchParams(searchParams)
    next.delete('team')
    next.delete('tab')
    setSearchParams(next)
    setMobileDetailOpen(false)
  }

  const leaveOrDeleteTeam = async (kind: 'leave' | 'delete') => {
    if (!selectedTeam?.id) return false
    return runMutation(
      async () => {
        if (kind === 'leave') await teamApi.leave(selectedTeam.id!)
        else await teamApi.delete(selectedTeam.id!)
        clearSelection()
        await teamsRequest.mutate()
      },
      kind === 'leave' ? '退出战队失败。' : '删除战队失败。'
    )
  }

  const runConfirmedAction = async () => {
    if (!confirmation) return false
    if (confirmation.kind === 'kick' || confirmation.kind === 'transfer') {
      return runMemberAction(confirmation.kind, confirmation.userId)
    }
    return leaveOrDeleteTeam(confirmation.kind)
  }

  const refreshInviteCode = async () => {
    if (!selectedTeam?.id) return
    await runMutation(async () => {
      await teamApi.refreshInviteCode(selectedTeam.id!)
      await inviteRequest.mutate()
    }, '邀请码刷新失败。')
  }

  return {
    account,
    teams: teamsRequest.data,
    teamsError: teamsRequest.error,
    mutateTeams: teamsRequest.mutate,
    selectedId,
    selectedTeam,
    isCaptain,
    activeTab,
    requests: requestsRequest.data,
    inviteCode: inviteRequest.data,
    createOpen,
    joinOpen,
    searchOpen,
    editName,
    editBio,
    avatarFile,
    submitting,
    mobileDetailOpen,
    confirmation,
    feedback,
    setCreateOpen,
    setJoinOpen,
    setSearchOpen,
    setEditName,
    setEditBio,
    setAvatarFile,
    setMobileDetailOpen,
    setConfirmation,
    setFeedback,
    selectTeam,
    setTab,
    saveTeam,
    uploadAvatar,
    reviewRequest,
    refreshInviteCode,
    runConfirmedAction,
  }
}
