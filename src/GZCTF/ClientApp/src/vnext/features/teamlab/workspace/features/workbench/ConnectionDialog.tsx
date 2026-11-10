import { Button, Modal, NumberInput, PasswordInput, SegmentedControl, Select, Textarea, TextInput } from '@mantine/core'
import { useMutation, useQuery } from '@tanstack/react-query'
import { useState } from 'react'
import { useWorkspaceApi, ApiError, type Asset, type Schema } from '../../api/client'
import { ErrorMessage, Loading } from '../../foundation/Feedback'
import styles from './ConnectionDialog.module.css'

type Settings = Schema<'SSHSettings'> | Schema<'RDPSettings'>

export function ConnectionDialog({
  environmentId,
  asset,
  kind,
  onClose,
  onSaved,
}: {
  environmentId: string
  asset: Asset
  kind: 'ssh' | 'rdp'
  onClose: () => void
  onSaved: () => void
}) {
  const api = useWorkspaceApi()
  const saved = useQuery({
    queryKey: [kind, environmentId, asset.id],
    retry: false,
    queryFn: async () => {
      try {
        return await (kind === 'ssh'
          ? api.sshSettings(environmentId, asset.id)
          : api.rdpSettings(environmentId, asset.id))
      } catch (error) {
        if (error instanceof ApiError && error.status === 404) return null
        throw error
      }
    },
  })
  const [draft, setDraft] = useState<Settings>()
  const settings: Settings =
    draft ??
    saved.data ??
    (kind === 'ssh'
      ? {
          username: asset.guest?.username ?? '',
          port: 22,
          authKind: 'password',
          hostKey: '',
        }
      : { username: asset.guest?.username ?? '', port: 3389, certificate: '' })
  const [password, setPassword] = useState('')
  const [privateKey, setPrivateKey] = useState('')
  const [passphrase, setPassphrase] = useState('')
  const ssh = 'authKind' in settings ? settings : undefined
  const rdp = 'certificate' in settings ? settings : undefined
  const configuredFingerprint = ssh?.hostKey ?? rdp?.certificate ?? ''
  const savedFingerprint = saved.data && ('hostKey' in saved.data ? saved.data.hostKey : saved.data.certificate)
  const change = (patch: Partial<Schema<'SSHSettings'> & Schema<'RDPSettings'>>) => setDraft({ ...settings, ...patch })
  const probe = useQuery({
    queryKey: [kind, 'fingerprint', environmentId, asset.id, settings.port, settings.interfaceId],
    enabled: saved.isSuccess && !configuredFingerprint && settings.port > 0 && settings.port <= 65535,
    retry: false,
    queryFn: () =>
      (kind === 'ssh' ? api.sshHostKey : api.rdpCertificate)(environmentId, asset.id, {
        port: settings.port,
        interfaceId: settings.interfaceId,
      }),
  })
  const fingerprint = probe.data?.fingerprint ?? configuredFingerprint
  const save = useMutation({
    mutationFn: () =>
      'authKind' in settings
        ? api.saveSSHSettings(environmentId, asset.id, {
            ...settings,
            hostKey: fingerprint,
            password: password || undefined,
            privateKey: privateKey || undefined,
            passphrase: passphrase || undefined,
          })
        : api.saveRDPSettings(environmentId, asset.id, {
            ...settings,
            certificate: fingerprint,
            password: password || undefined,
          }),
    onSuccess: onSaved,
  })
  return (
    <Modal
      opened
      title={`${asset.name} · ${kind === 'ssh' ? 'SSH' : '远程桌面'}`}
      onClose={onClose}
      centered
      size="md"
      portalProps={{
        target: (document.fullscreenElement ?? undefined) as HTMLElement | undefined,
      }}
    >
      {saved.isPending ? (
        <Loading />
      ) : (
        <form
          className={styles.form}
          onSubmit={(event) => {
            event.preventDefault()
            save.mutate()
          }}
        >
          <ErrorMessage error={saved.error ?? probe.error ?? save.error} />
          <TextInput
            label="用户名"
            value={settings.username}
            onChange={(event) => change({ username: event.currentTarget.value })}
            required
            autoFocus
          />
          {ssh && (
            <SegmentedControl
              aria-label="认证方式"
              value={ssh.authKind}
              data={[
                { value: 'password', label: '密码' },
                { value: 'key', label: '私钥' },
              ]}
              onChange={(value) => change({ authKind: value as Schema<'SSHSettings'>['authKind'] })}
            />
          )}
          {ssh?.authKind === 'key' ? (
            <>
              <Textarea
                label="私钥"
                value={privateKey}
                onChange={(event) => setPrivateKey(event.currentTarget.value)}
                minRows={4}
                maxRows={8}
                autosize
                placeholder={saved.data && 'authKind' in saved.data && saved.data.authKind === 'key' ? '已保存' : ''}
              />
              <PasswordInput
                label="私钥口令"
                value={passphrase}
                onChange={(event) => setPassphrase(event.currentTarget.value)}
              />
            </>
          ) : (
            <PasswordInput
              label="密码"
              value={password}
              placeholder={saved.data ? '已保存' : ''}
              onChange={(event) => setPassword(event.currentTarget.value)}
              autoComplete="new-password"
            />
          )}
          <details className={styles.options}>
            <summary>连接选项</summary>
            <div className={styles.optionFields}>
              <NumberInput
                label="端口"
                value={settings.port}
                min={1}
                max={65535}
                onChange={(value) => {
                  change({
                    port: Number(value),
                    ...('authKind' in settings ? { hostKey: '' } : { certificate: '' }),
                  })
                }}
              />
              {rdp && (
                <TextInput
                  label="域"
                  value={rdp.domain ?? ''}
                  onChange={(event) => change({ domain: event.currentTarget.value || undefined })}
                />
              )}
              {asset.interfaces.length > 1 && (
                <Select
                  label="网络接口"
                  value={settings.interfaceId ?? ''}
                  data={[
                    { value: '', label: '主接口' },
                    ...asset.interfaces.map((item) => ({
                      value: item.id,
                      label: item.address || '未分配地址',
                    })),
                  ]}
                  onChange={(value) => {
                    change({
                      interfaceId: value || undefined,
                      ...('authKind' in settings ? { hostKey: '' } : { certificate: '' }),
                    })
                  }}
                />
              )}
            </div>
          </details>
          <details className={styles.options}>
            <summary>{kind === 'ssh' ? '主机身份' : '服务器证书'}</summary>
            <div className={styles.identity}>
              <Button
                variant="default"
                size="compact-sm"
                onClick={() => void probe.refetch()}
                loading={probe.isFetching}
              >
                {kind === 'ssh' ? '读取主机密钥' : '读取服务器证书'}
              </Button>
              {fingerprint && <code>{fingerprint}</code>}
            </div>
          </details>
          <div className={styles.dialogActions}>
            <Button variant="default" onClick={onClose}>
              取消
            </Button>
            <Button type="submit" loading={save.isPending} disabled={!fingerprint || probe.isFetching}>
              {fingerprint === savedFingerprint ? '保存' : '信任并保存'}
            </Button>
          </div>
        </form>
      )}
    </Modal>
  )
}
