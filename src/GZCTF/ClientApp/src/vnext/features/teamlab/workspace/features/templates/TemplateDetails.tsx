import { Button } from '@mantine/core'
import { useMutation, useQuery } from '@tanstack/react-query'
import { Box, Monitor } from 'lucide-react'
import { useWorkspaceApi, type Template } from '../../api/client'
import { ErrorMessage } from '../../foundation/Feedback'
import { Status } from '../../foundation/Status'
import { memory } from '../../foundation/format'
import { randomUUID } from '../../foundation/id'
import { allowsProject } from '../access/permissions'
import { useNavigate } from './../../routing'
import styles from './Templates.module.css'

const initializationNames = {
  none: '保留镜像配置',
  'cloud-init': 'cloud-init',
  'cloudbase-init': 'Cloudbase-Init',
}

export function TemplateDetails({ template }: { template: Template }) {
  const api = useWorkspaceApi()
  const hardware = template.hardware
  const navigate = useNavigate()
  const identity = useQuery({ queryKey: ['identity'], queryFn: api.identity })
  const canInstall = allowsProject(identity.data, 'compose')
  const install = useMutation({
    mutationFn: () => {
      const networkId = randomUUID()
      return api.createEnvironment({
        name: `${template.name} · 安装`,
        run: true,
        spec: {
          networks: [{ id: networkId, name: '安装网络', cidr: '10.0.0.0/24' }],
          assets: [
            {
              id: randomUUID(),
              name: template.name,
              templateId: template.id,
              resources: template.resources,
              interfaces: [
                {
                  id: randomUUID(),
                  networkId,
                  mac: '',
                  address: '',
                  primary: true,
                },
              ],
            },
          ],
        },
      })
    },
    onSuccess: (environment) => navigate(`/environments/${environment.id}`),
  })
  const localSource = template.source.startsWith('/') || template.source.startsWith('file:')
  return (
    <section className={styles.detail} role="region" aria-label="资产模板">
      <div className={styles.detailHeading}>
        <div>
          <h2>{template.name}</h2>
          <span>
            {template.kind === 'vm' ? <Monitor size={16} /> : <Box size={16} />}
            {template.os} · v{template.version} <Status value={template.state ?? 'importing'} />
          </span>
        </div>
      </div>
      {template.error && (
        <div className="template-failure" role="alert">
          {template.error}
        </div>
      )}
      {canInstall && template.format === 'iso' && template.state === 'ready' && (
        <>
          <Button leftSection={<Monitor size={16} />} loading={install.isPending} onClick={() => install.mutate()}>
            安装系统
          </Button>
          <ErrorMessage error={install.error} />
        </>
      )}
      <div className={styles.specs}>
        <div>
          <strong>
            {template.resources.cpu}
            <span> 核</span>
          </strong>
          <span>CPU</span>
        </div>
        <div>
          <strong>{memory(template.resources.memoryMiB)}</strong>
          <span>内存</span>
        </div>
        <div>
          <strong>
            {template.resources.diskGiB}
            <span> GiB</span>
          </strong>
          <span>{template.kind === 'container' ? '磁盘预算' : '磁盘容量'}</span>
        </div>
      </div>
      <div className={styles.detailColumns}>
        {hardware && (
          <section className={styles.section}>
            <h3>虚拟硬件</h3>
            <dl className={styles.meta}>
              <dt>机器</dt>
              <dd className={styles.technical}>{hardware.machine}</dd>
              <dt>固件</dt>
              <dd className={styles.technical}>
                {hardware.firmware.toUpperCase()}
                {hardware.secureBoot ? ' · Secure Boot' : ''}
              </dd>
              <dt>磁盘</dt>
              <dd className={styles.technical}>
                {hardware.diskBus.toUpperCase()}
                {hardware.diskController ? ` · ${hardware.diskController}` : ''}
              </dd>
              <dt>网卡</dt>
              <dd className={styles.technical}>{template.nicModels?.join(' · ') || hardware.nicModel}</dd>
              {hardware.tpm && (
                <>
                  <dt>安全芯片</dt>
                  <dd className={styles.technical}>TPM 2.0</dd>
                </>
              )}
              <dt>初始化</dt>
              <dd>{initializationNames[template.initialization ?? 'none']}</dd>
            </dl>
          </section>
        )}
        {template.disks && template.disks.length > 1 && (
          <section className={styles.section}>
            <h3>系统磁盘</h3>
            <div className={styles.disks}>
              {template.disks.map((disk, index) => (
                <div key={disk.id}>
                  <strong>磁盘 {index + 1}</strong>
                  <span className={styles.technical}>
                    {disk.bus.toUpperCase()} · {disk.sizeGiB} GiB
                  </span>
                  {disk.bootOrder === 1 && <span className="asset-count">启动盘</span>}
                </div>
              ))}
            </div>
          </section>
        )}
        <section className={styles.section}>
          <h3>镜像来源</h3>
          <dl className={styles.meta}>
            <dt>格式</dt>
            <dd className={styles.technical}>
              {template.format?.toUpperCase() ?? (template.kind === 'container' ? 'OCI' : '—')}
            </dd>
            {template.source && (
              <>
                <dt>{localSource ? '文件' : '地址'}</dt>
                <dd className={`${styles.technical} template-source`}>
                  {localSource
                    ? template.source.split('/').at(-1)
                    : template.source.startsWith('asset:')
                      ? '运行资产'
                      : template.source}
                </dd>
              </>
            )}
          </dl>
        </section>
      </div>
    </section>
  )
}
