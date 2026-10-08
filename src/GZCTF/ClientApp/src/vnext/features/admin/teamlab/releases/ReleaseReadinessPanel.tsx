import { AlertTriangle, CheckCircle2, Download, PlayCircle } from 'lucide-react'
import { Link } from 'react-router'
import { ActionButton } from '../../../../shared/Interaction'
import type { TeamLabAdminReleaseReadiness } from '../api'
import type { TeamLabImageOption } from '../api/teamlabImageCatalog'
import { TeamLabRuntimeStatusBadge } from '../shared/TeamLabStatusBadge'
import styles from './TeamLabReleasesPage.module.css'

export function ReleaseReadinessPanel({ readiness, imageOptions, creatingTrial, preparingImages, onCreateTrial, onPrepareImages }: {
  readiness: TeamLabAdminReleaseReadiness
  imageOptions?: readonly TeamLabImageOption[]
  creatingTrial: boolean
  preparingImages: boolean
  onCreateTrial: () => void
  onPrepareImages: () => void
}) {
  const imagesToPrepare = readiness.images.filter(image => image.readyNodeCount < image.eligibleNodeCount || image.pendingNodeCount || image.failedNodeCount)
  const vmImages = [...new Set(readiness.plan?.assets.filter(asset => asset.kind === 'vm').map(asset => asset.imageTemplateId) ?? [])]
  return <section className={styles.readiness} aria-label="版本运行准备">
    <div className={styles.readinessStatus} data-ready={readiness.ready}>
      {readiness.ready ? <CheckCircle2 size={20} /> : <AlertTriangle size={20} />}
      <strong>{readiness.ready ? '可以启动运行环境' : '暂不可启动'}</strong>
    </div>
    {readiness.plan ? <div className={styles.releaseScale}>
      <span>{readiness.plan.networks.length} 个网段</span>
      <span>{readiness.plan.assets.length} 个资产</span>
    </div> : null}
    {readiness.blockingReasons.length ? <ul className={styles.blockers}>
      {readiness.blockingReasons.map(reason => <li key={reason}>{reason}</li>)}
    </ul> : null}
    <p className={styles.readinessNote}>可以启动表示制品与当前放置条件满足。缺少的缓存将在选定节点下载；来宾系统连接需在运行后验证。</p>
    <div className={styles.readinessActions}>
      {imagesToPrepare.length ? <ActionButton disabled={preparingImages} icon={<Download size={16} />} onClick={onPrepareImages} type="button">
        {preparingImages ? '预热中' : '预热合格节点'}
      </ActionButton> : null}
      <ActionButton disabled={!readiness.ready || creatingTrial} icon={<PlayCircle size={16} />} onClick={onCreateTrial} tone="primary" type="button">
        {creatingTrial ? '正在启动' : '启动环境'}
      </ActionButton>
    </div>
    {vmImages.length ? <section className={styles.optionalAccess} aria-label="可选运维入口">
      <h4>可选运维入口</h4>
      <p>SSH/RDP 不参与启动就绪判断。模板配置不代表登录已经实测可用。</p>
      <ul>{vmImages.map(id => {
        const image = imageOptions?.find(option => option.id === id)
        return <li key={id}>{image?.name ?? `模板 #${id}`}：{!imageOptions || !image ? '配置未知' : image.remoteAccessProtocol ? `已配置 ${image.remoteAccessProtocol.toUpperCase()}，尚未验证` : '未配置'} </li>
      })}</ul>
      <Link to="/admin/images">配置环境模板</Link>
    </section> : null}
    {readiness.latestTrialRuntime ? <Link className={styles.latestTrial} to={`/admin/teamlab/runtimes/${readiness.latestTrialRuntime.id}`}>
      最近运行环境 <TeamLabRuntimeStatusBadge status={readiness.latestTrialRuntime.status} />
    </Link> : null}
    {readiness.images.length ? <details className={styles.imageDetails}>
      <summary>镜像分发详情</summary>
      <p>已缓存节点 / 能力合格节点。其他节点未缓存或分发失败不单独阻止启动；普通启动只在选定节点准备镜像。手动预热会向所有合格节点分发。</p>
      <table><thead><tr><th>模板</th><th>已缓存 / 合格节点</th><th>待分发</th><th>失败</th></tr></thead><tbody>
        {readiness.images.map(image => <tr key={image.imageTemplateId}>
          <td>{image.name}</td><td>{image.readyNodeCount}/{image.eligibleNodeCount}</td>
          <td>{image.pendingNodeCount}</td><td>{image.failedNodeCount}</td>
        </tr>)}
      </tbody></table>
    </details> : null}
  </section>
}
