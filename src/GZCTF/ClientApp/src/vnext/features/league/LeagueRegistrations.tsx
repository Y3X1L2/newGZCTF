import { useState } from 'react'
import { Link } from 'react-router'
import { LeagueRegistrationState } from '@Api'
import type { LeagueMatchDetail, TeamInfoModel } from '@Api'
import { SelectField } from '../../shared/FormControls'
import { ActionButton, InlineFeedback } from '../../shared/Interaction'
import { StatusPill } from '../../shared/Primitives'
import styles from './League.module.css'
import { registrationLabels } from './leaguePresentation'

export function LeagueRegistrations({
  detail,
  admin,
  userId,
  teams,
  busy,
  register,
  review,
  select,
}: {
  detail: LeagueMatchDetail
  admin: boolean
  userId: string
  teams: TeamInfoModel[]
  busy: boolean
  register: (teamId: number) => void
  review: (teamId: number, approved: boolean) => void
  select: (revision: number, first: number, second: number) => Promise<LeagueMatchDetail | undefined>
}) {
  const rows = detail.registrations ?? []
  const actions = detail.allowedActions ?? []
  const [teamId, setTeamId] = useState('')
  const [seats, setSeats] = useState<{ revision: number; first: string; second: string } | null>(null)
  const current = {
    revision: detail.match?.revision ?? 0,
    first: String(rows.find((r) => r.seat === 1)?.teamId ?? ''),
    second: String(rows.find((r) => r.seat === 2)?.teamId ?? ''),
  }
  const chosen = seats ?? current
  const stale = chosen.revision !== current.revision
  const approved = rows.filter((row) => row.state === LeagueRegistrationState.Approved)
  const eligible = teams.filter(
    (team) =>
      team.id &&
      !team.locked &&
      team.members?.some((member) => member.id === userId && member.captain) &&
      !rows.some((row) => row.teamId === team.id)
  )
  const canSelect =
    chosen.first &&
    chosen.second &&
    chosen.first !== chosen.second &&
    [chosen.first, chosen.second].every((id) => approved.some((row) => String(row.teamId) === id))

  return (
    <section className={styles.panel} aria-label="报名与参赛队">
      <h2>{admin ? '报名审核与双队席位' : '报名与参赛队'}</h2>
      <p>
        {admin
          ? '先审核报名，再从已通过的战队中选定两队。席位将在准备环境时固定。'
          : '队长代表未锁定的战队报名。审核通过后，由管理员确定两支参赛队。'}
      </p>
      {!rows.length ? (
        <p>暂无可显示的报名记录。</p>
      ) : (
        <ul className={styles.registrations}>
          {rows.map((row) => (
            <li key={row.teamId}>
              <div>
                <strong>{row.teamName}</strong>
                <small>
                  战队 #{row.teamId}
                  {row.selected ? ` · 席位 ${row.seat}` : ' · 未选入席位'}
                </small>
              </div>
              <StatusPill tone={row.state === LeagueRegistrationState.Approved ? 'success' : 'neutral'}>
                {registrationLabels[row.state ?? -1] ?? '状态待确认'}
              </StatusPill>
              {admin && actions.includes('review') ? (
                <div className={styles.actions}>
                  <ActionButton
                    disabled={busy || row.state === LeagueRegistrationState.Approved}
                    onClick={() => review(row.teamId!, true)}
                    type="button"
                    aria-label={`通过 ${row.teamName}`}
                  >
                    通过
                  </ActionButton>
                  <ActionButton
                    disabled={busy || row.state === LeagueRegistrationState.Rejected}
                    onClick={() => review(row.teamId!, false)}
                    type="button"
                    aria-label={`拒绝 ${row.teamName}`}
                  >
                    拒绝
                  </ActionButton>
                </div>
              ) : null}
            </li>
          ))}
        </ul>
      )}
      {actions.includes('register') ? (
        eligible.length ? (
          <form
            className={styles.registrationForm}
            onSubmit={(event) => {
              event.preventDefault()
              if (!busy && eligible.some((team) => String(team.id) === teamId)) register(Number(teamId))
            }}
          >
            <SelectField label="报名战队" required disabled={busy} value={teamId} onValueChange={setTeamId}>
              <option value="">选择我担任队长的战队</option>
              {eligible.map((team) => (
                <option key={team.id} value={team.id}>
                  {team.name}
                </option>
              ))}
            </SelectField>
            <ActionButton type="submit" disabled={busy || !eligible.some((team) => String(team.id) === teamId)}>
              提交报名
            </ActionButton>
          </form>
        ) : (
          <p>
            没有可新报名的战队：请确认你是队长、战队未锁定且尚未报名。<Link to="/teams">查看我的战队</Link>
          </p>
        )
      ) : null}
      {admin && actions.includes('selectTeams') ? (
        <form
          onSubmit={async (event) => {
            event.preventDefault()
            if (
              canSelect &&
              !busy &&
              !stale &&
              (await select(chosen.revision, Number(chosen.first), Number(chosen.second)))
            )
              setSeats(null)
          }}
        >
          <h3>参赛席位</h3>
          {stale ? (
            <InlineFeedback tone="danger">
              报名或配置已更新，所选队伍保留。请核对最新审核结果后重新选择席位。
            </InlineFeedback>
          ) : null}
          <fieldset className={styles.fields} disabled={busy}>
            <SelectField
              label="席位 1"
              required
              value={chosen.first}
              onValueChange={(first) => setSeats({ ...chosen, first })}
            >
              <option value="">请选择已通过的战队</option>
              {approved.map((row) => (
                <option key={row.teamId} value={row.teamId}>
                  {row.teamName}
                </option>
              ))}
            </SelectField>
            <SelectField
              label="席位 2"
              required
              value={chosen.second}
              onValueChange={(second) => setSeats({ ...chosen, second })}
            >
              <option value="">请选择已通过的战队</option>
              {approved.map((row) => (
                <option key={row.teamId} value={row.teamId}>
                  {row.teamName}
                </option>
              ))}
            </SelectField>
          </fieldset>
          {chosen.first && chosen.first === chosen.second ? (
            <InlineFeedback tone="danger">两个席位必须选择不同战队。</InlineFeedback>
          ) : null}
          <div className={styles.actions}>
            <ActionButton type="submit" disabled={busy || stale || !canSelect}>
              保存双队席位
            </ActionButton>
            {seats ? (
              <ActionButton type="button" disabled={busy} onClick={() => setSeats(null)}>
                载入最新席位
              </ActionButton>
            ) : null}
          </div>
        </form>
      ) : null}
    </section>
  )
}
