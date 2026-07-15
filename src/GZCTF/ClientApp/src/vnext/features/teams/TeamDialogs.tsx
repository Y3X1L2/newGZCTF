import { Check, Search } from 'lucide-react'
import { FormEvent, useState } from 'react'
import api from '@Api'
import { ActionButton, InlineFeedback, VNextDialog } from '../../shared/Interaction'
import { errorMessage } from '../../shared/errors'
import { TeamAvatar } from './TeamAvatar'
import styles from './TeamsPage.module.css'

export type TeamFeedback = { tone: 'success' | 'danger'; message: string }

interface TeamDialogsProps {
  createOpen: boolean
  joinOpen: boolean
  searchOpen: boolean
  onCreateClose: () => void
  onJoinClose: () => void
  onSearchClose: () => void
  onFeedback: (feedback: TeamFeedback) => void
  onTeamCreated: (teamId?: number) => void
  onTeamsChanged: () => Promise<unknown>
}

export function TeamDialogs({
  createOpen,
  joinOpen,
  searchOpen,
  onCreateClose,
  onJoinClose,
  onSearchClose,
  onFeedback,
  onTeamCreated,
  onTeamsChanged,
}: TeamDialogsProps) {
  const [createName, setCreateName] = useState('')
  const [createBio, setCreateBio] = useState('')
  const [inviteToken, setInviteToken] = useState('')
  const [searchHint, setSearchHint] = useState('')
  const [requestMessage, setRequestMessage] = useState('')
  const [targetTeamId, setTargetTeamId] = useState<number | null>(null)
  const [submitting, setSubmitting] = useState(false)

  const { data: searchResults, error: searchError } = api.team.useTeamSearch(
    { hint: searchHint.trim() },
    { revalidateOnFocus: false, keepPreviousData: true },
    searchOpen && searchHint.trim().length >= 2
  )

  const createTeam = async (event: FormEvent) => {
    event.preventDefault()
    setSubmitting(true)
    try {
      const response = await api.team.teamCreateTeam({ name: createName.trim(), bio: createBio.trim() })
      await onTeamsChanged()
      onTeamCreated(response.data.id)
      setCreateName('')
      setCreateBio('')
      onCreateClose()
      onFeedback({ tone: 'success', message: '战队已创建。' })
    } catch (error) {
      onFeedback({ tone: 'danger', message: errorMessage(error, '战队创建失败。') })
    } finally {
      setSubmitting(false)
    }
  }

  const joinByCode = async (event: FormEvent) => {
    event.preventDefault()
    setSubmitting(true)
    try {
      await api.team.teamAccept(inviteToken.trim())
      await onTeamsChanged()
      setInviteToken('')
      onJoinClose()
      onFeedback({ tone: 'success', message: '已通过邀请码加入战队。' })
    } catch (error) {
      onFeedback({ tone: 'danger', message: errorMessage(error, '邀请码无效或加入失败。') })
    } finally {
      setSubmitting(false)
    }
  }

  const sendJoinRequest = async () => {
    if (!targetTeamId) return
    setSubmitting(true)
    try {
      await api.team.teamCreateJoinRequest(targetTeamId, { message: requestMessage.trim() })
      setTargetTeamId(null)
      setRequestMessage('')
      onSearchClose()
      onFeedback({ tone: 'success', message: '加入申请已提交，等待队长审核。' })
    } catch (error) {
      onFeedback({ tone: 'danger', message: errorMessage(error, '加入申请提交失败。') })
    } finally {
      setSubmitting(false)
    }
  }

  return (
    <>
      <VNextDialog
        eyebrow="CREATE TEAM"
        footer={
          <>
            <ActionButton onClick={onCreateClose} type="button">
              取消
            </ActionButton>
            <ActionButton
              disabled={submitting || !createName.trim()}
              form="vnext-create-team-form"
              tone="primary"
              type="submit"
            >
              创建
            </ActionButton>
          </>
        }
        onClose={onCreateClose}
        open={createOpen}
        title="创建战队"
      >
        <form className={styles.dialogForm} id="vnext-create-team-form" onSubmit={createTeam}>
          <label>
            <span>战队名称</span>
            <input
              maxLength={20}
              onChange={(event) => setCreateName(event.currentTarget.value)}
              required
              value={createName}
            />
          </label>
          <label>
            <span>战队简介</span>
            <textarea
              maxLength={72}
              onChange={(event) => setCreateBio(event.currentTarget.value)}
              rows={4}
              value={createBio}
            />
          </label>
        </form>
      </VNextDialog>

      <VNextDialog
        eyebrow="INVITE CODE"
        footer={
          <>
            <ActionButton onClick={onJoinClose} type="button">
              取消
            </ActionButton>
            <ActionButton
              disabled={submitting || !inviteToken.trim()}
              form="vnext-join-team-form"
              tone="primary"
              type="submit"
            >
              加入
            </ActionButton>
          </>
        }
        onClose={onJoinClose}
        open={joinOpen}
        title="使用邀请码加入"
      >
        <form className={styles.dialogForm} id="vnext-join-team-form" onSubmit={joinByCode}>
          <label>
            <span>邀请码</span>
            <input
              autoComplete="off"
              onChange={(event) => setInviteToken(event.currentTarget.value)}
              placeholder="粘贴队长提供的邀请码"
              required
              value={inviteToken}
            />
          </label>
        </form>
      </VNextDialog>

      <VNextDialog
        description="输入战队名称或 ID，选择目标后填写申请说明。"
        eyebrow="FIND TEAM"
        footer={
          <>
            <ActionButton onClick={onSearchClose} type="button">
              取消
            </ActionButton>
            <ActionButton disabled={submitting || !targetTeamId} onClick={sendJoinRequest} tone="primary" type="button">
              提交申请
            </ActionButton>
          </>
        }
        onClose={onSearchClose}
        open={searchOpen}
        title="搜索公开战队"
        wide
      >
        <div className={styles.searchDialog}>
          <label className={styles.searchInput}>
            <Search size={16} />
            <input
              onChange={(event) => {
                setSearchHint(event.currentTarget.value)
                setTargetTeamId(null)
              }}
              placeholder="至少输入 2 个字符"
              value={searchHint}
            />
          </label>
          {searchError ? <InlineFeedback tone="danger">战队搜索失败。</InlineFeedback> : null}
          <div className={styles.searchResults}>
            {(searchResults ?? []).map((team) => (
              <button
                className={team.id === targetTeamId ? styles.searchResultActive : styles.searchResult}
                key={team.id}
                onClick={() => setTargetTeamId(team.id ?? null)}
                type="button"
              >
                <TeamAvatar team={team} />
                <span>
                  <strong>{team.name}</strong>
                  <small>
                    {team.members?.length ?? 0} 名成员 · #{team.id}
                  </small>
                </span>
                {team.id === targetTeamId ? <Check size={17} /> : null}
              </button>
            ))}
            {searchHint.trim().length >= 2 && searchResults && !searchResults.length ? (
              <span className={styles.noResults}>没有找到符合条件的战队。</span>
            ) : null}
          </div>
          {targetTeamId ? (
            <label className={styles.requestMessage}>
              <span>申请说明</span>
              <textarea
                maxLength={128}
                onChange={(event) => setRequestMessage(event.currentTarget.value)}
                rows={3}
                value={requestMessage}
              />
            </label>
          ) : null}
        </div>
      </VNextDialog>
    </>
  )
}
