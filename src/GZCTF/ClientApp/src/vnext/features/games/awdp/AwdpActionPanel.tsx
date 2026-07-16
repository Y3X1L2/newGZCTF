import { FileUp, Flag, ShieldCheck } from 'lucide-react'
import { FormEvent, useEffect, useState } from 'react'
import { ActionButton } from '../../../shared/Interaction'
import { AwdpPatchState, AwdpPhase, defenseMeta, patchMeta } from '../../awdp/awdpDomain'
import styles from './AwdpWorkspace.module.css'

export function AwdpActionPanel({
  phase,
  patchStatus,
  operation,
  onSubmitFlag,
  onSubmitPatch,
}: {
  phase: AwdpPhase
  patchStatus: AwdpPatchState[]
  operation: string | null
  onSubmitFlag: (flag: string) => Promise<boolean>
  onSubmitPatch: (serviceId: number, file: File) => Promise<boolean>
}) {
  const [flag, setFlag] = useState('')
  const [serviceId, setServiceId] = useState(0)
  const [file, setFile] = useState<File | null>(null)
  const [fileInputKey, setFileInputKey] = useState(0)

  useEffect(() => {
    if (!patchStatus.some((item) => item.serviceId === serviceId)) setServiceId(patchStatus[0]?.serviceId ?? 0)
  }, [patchStatus, serviceId])

  const submitFlag = async (event: FormEvent) => {
    event.preventDefault()
    if (await onSubmitFlag(flag)) setFlag('')
  }
  const submitPatch = async (event: FormEvent) => {
    event.preventDefault()
    if (file && (await onSubmitPatch(serviceId, file))) {
      setFile(null)
      setFileInputKey((current) => current + 1)
    }
  }
  const currentPatch = patchStatus.find((item) => item.serviceId === serviceId)
  const defense = defenseMeta(currentPatch?.defenseStatus ?? null)
  const result = patchMeta(currentPatch?.lastPatchResult ?? null)

  return (
    <section className={styles.actionPanel}>
      <header>
        <span>PHASE ACTION</span>
        <h2>{phase === 'attack' ? '提交攻击 Flag' : phase === 'patch' ? '上传服务补丁' : '阶段操作不可用'}</h2>
      </header>
      {phase === 'attack' ? (
        <form className={styles.actionForm} onSubmit={submitFlag}>
          <label>
            <span>攻击 Flag</span>
            <input
              autoComplete="off"
              onChange={(event) => setFlag(event.currentTarget.value)}
              placeholder="flag{...}"
              type="password"
              value={flag}
            />
          </label>
          <ActionButton
            disabled={!flag.trim() || operation === 'flag'}
            icon={<Flag size={16} />}
            tone="primary"
            type="submit"
          >
            {operation === 'flag' ? '正在判定' : '提交 Flag'}
          </ActionButton>
        </form>
      ) : phase === 'patch' ? (
        <form className={styles.patchForm} onSubmit={submitPatch}>
          <label>
            <span>本队服务</span>
            <select onChange={(event) => setServiceId(Number(event.currentTarget.value))} value={serviceId}>
              {patchStatus.map((item) => (
                <option key={item.serviceId} value={item.serviceId}>
                  {item.serviceName}
                </option>
              ))}
            </select>
          </label>
          <label className={styles.fileField}>
            <span>补丁包（.tgz / .tar.gz）</span>
            <input
              accept=".tgz,.tar.gz,application/gzip"
              key={fileInputKey}
              onChange={(event) => setFile(event.currentTarget.files?.[0] ?? null)}
              type="file"
            />
          </label>
          <ActionButton
            disabled={!serviceId || !file || operation === `patch:${serviceId}`}
            icon={<FileUp size={16} />}
            tone="primary"
            type="submit"
          >
            {operation === `patch:${serviceId}` ? '正在验证' : '上传并验证'}
          </ActionButton>
          <div className={styles.patchSummary}>
            <ShieldCheck size={16} />
            <span>
              <strong data-tone={defense.tone}>{defense.label}</strong>
              <small data-tone={result.tone}>{result.label}</small>
            </span>
          </div>
        </form>
      ) : (
        <p className={styles.readOnlyNotice}>比赛尚未进入可操作阶段，当前页面只展示最近一次服务器快照。</p>
      )}
    </section>
  )
}
