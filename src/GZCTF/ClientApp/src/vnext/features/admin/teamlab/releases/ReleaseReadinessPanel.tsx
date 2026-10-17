import { AlertTriangle, ArrowRight, CheckCircle2, Download, PlayCircle, Settings2 } from 'lucide-react'
import { Link } from 'react-router'
import { ActionButton, InlineFeedback } from '../../../../shared/Interaction'
import type { TeamLabAdminReleaseReadiness } from '../api'
import type { TeamLabImageOption } from '../api/teamlabImageCatalog'
import { TeamLabRuntimeStatusBadge } from '../shared/TeamLabStatusBadge'
import styles from './TeamLabReleasesPage.module.css'

export function ReleaseReadinessPanel({ readiness, imageOptions, creatingTrial, preparingImages, preparationSubmitted = false, onCreateTrial, onPrepareImages }: {
  readiness: TeamLabAdminReleaseReadiness
  imageOptions?: readonly TeamLabImageOption[]
  creatingTrial: boolean
  preparingImages: boolean
  preparationSubmitted?: boolean
  onCreateTrial: () => void
  onPrepareImages: () => void | Promise<void>
}) {
  const imagesToPrepare = readiness.images.filter(image => image.readyNodeCount < image.eligibleNodeCount || image.pendingNodeCount || image.failedNodeCount)
  const vmImages = [...new Set(readiness.plan?.assets.filter(asset => asset.kind === 'vm').map(asset => asset.imageTemplateId) ?? [])]
  return <section className={styles.readiness} aria-label="版本运行准备">
    <div className={styles.startPanel}>
      <div className={styles.startSummary}>
        <div className={styles.readinessStatus} data-ready={readiness.ready}>
          {readiness.ready ? <CheckCircle2 size={20} /> : <AlertTriangle size={20} />}
          <strong>{readiness.ready ? '可以启动运行环境' : '暂不可启动'}</strong>
        </div>
        {readiness.plan ? <div className={styles.releaseScale}>
          <span>{readiness.plan.networks.length} 个网段</span>
          <span>{readiness.plan.assets.length} 个资产</span>
        </div> : null}
        <p className={styles.readinessNote}>启动后将创建一套独立环境。缺少的缓存将在选定节点下载，首次启动可能需要更长时间。</p>
        {readiness.blockingReasons.length ? <ul className={styles.blockers}>
          {readiness.blockingReasons.map(reason => <li key={reason}>{reason}</li>)}
        </ul> : null}
      </div>
      <div className={styles.startAction}>
        <ActionButton disabled={!readiness.ready} icon={<PlayCircle size={16} />} loading={creatingTrial} loadingLabel="正在启动" onClick={onCreateTrial} tone="primary" type="button">启动环境</ActionButton>
        <span>使用当前所选版本</span>
      </div>
    </div>
    {readiness.latestTrialRuntime ? <Link className={styles.latestTrial} to={`/admin/teamlab/runtimes/${readiness.latestTrialRuntime.id}`}>
      <span>查看最近运行环境</span><TeamLabRuntimeStatusBadge status={readiness.latestTrialRuntime.status} /><ArrowRight size={16} />
    </Link> : null}

    <div className={styles.preparationGrid}>
      {readiness.images.length ? <section className={styles.preparationCard} aria-label="镜像准备">
        <div className={styles.cardHeading}><Download size={18} /><h4>镜像准备</h4></div>
        <p>启动时会自动准备所需镜像。提前预热可减少等待，适合批量启动前操作。</p>
        {imagesToPrepare.length ? <ActionButton icon={<Download size={16} />} loading={preparingImages} loadingLabel="正在提交预热" onClick={onPrepareImages} type="button">预热合格节点</ActionButton> : <span className={styles.cacheReady}><CheckCircle2 size={16} />合格节点已缓存所需镜像</span>}
        {preparingImages ? <p role="status">正在提交镜像预热请求，请稍候。</p> : preparationSubmitted ? <InlineFeedback tone="success">镜像预热请求已提交，分发状态将自动更新。</InlineFeedback> : null}
        <details className={styles.imageDetails}>
          <summary>镜像分发详情</summary>
          <p>已缓存节点 / 能力合格节点。其他节点未缓存或分发失败不单独阻止启动；普通启动只在选定节点准备镜像。手动预热会向所有合格节点分发。</p>
          <div className={styles.tableScroll}><table><thead><tr><th>模板</th><th>已缓存 / 合格节点</th><th>待分发</th><th>失败</th></tr></thead><tbody>
            {readiness.images.map(image => <tr key={image.imageTemplateId}>
              <td>{image.name}</td><td>{image.readyNodeCount}/{image.eligibleNodeCount}</td>
              <td>{image.pendingNodeCount}</td><td>{image.failedNodeCount}</td>
            </tr>)}
          </tbody></table></div>
        </details>
      </section> : null}
      {vmImages.length ? <section className={styles.preparationCard} aria-label="可选运维入口">
        <div className={styles.cardHeading}><Settings2 size={18} /><h4>运维连接 <span>可选</span></h4></div>
        <p>SSH / RDP 用于运行后的远程维护，不影响环境启动。来宾系统连接需在运行后验证。</p>
        <Link className={styles.settingsLink} to="/admin/images">配置环境模板<ArrowRight size={16} /></Link>
        <details className={styles.imageDetails}>
          <summary>查看模板连接配置（{vmImages.length}）</summary>
          <ul className={styles.accessList}>{vmImages.map(id => {
            const image = imageOptions?.find(option => option.id === id)
            return <li key={id}><strong>{image?.name ?? `模板 #${id}`}</strong><span>{!imageOptions || !image ? '配置未知' : image.remoteAccessProtocol ? `已配置 ${image.remoteAccessProtocol.toUpperCase()}，尚未验证` : '未配置'}</span></li>
          })}</ul>
        </details>
      </section> : null}
    </div>
  </section>
}
