import { Check, LoaderCircle, Server, ShieldCheck } from 'lucide-react'
import { useEffect, useState } from 'react'
import { TextField } from '../../../shared/FormControls'
import { ActionButton, InlineFeedback, VNextDialog } from '../../../shared/Interaction'
import { errorMessage } from '../../../shared/errors'
import { nodeAdminApi, type NodeDeployResult } from '../api'
import styles from './NodeRegistrationDialog.module.css'

const steps = ['连接信息', '环境检测', '配置预览', '部署确认', '注册结果']

export function NodeRegistrationDialog({
  open,
  onClose,
  onCompleted,
}: {
  open: boolean
  onClose: () => void
  onCompleted: () => void | Promise<void>
}) {
  const [step, setStep] = useState(0)
  const [hostAddress, setHostAddress] = useState('')
  const [username, setUsername] = useState('')
  const [password, setPassword] = useState('')
  const [nodeName, setNodeName] = useState('')
  const [deploying, setDeploying] = useState(false)
  const [result, setResult] = useState<NodeDeployResult | null>(null)
  const [failure, setFailure] = useState<string | null>(null)

  useEffect(() => {
    if (open) return
    setStep(0)
    setHostAddress('')
    setUsername('')
    setPassword('')
    setNodeName('')
    setDeploying(false)
    setResult(null)
    setFailure(null)
  }, [open])

  const deploy = async () => {
    setStep(4)
    setDeploying(true)
    setFailure(null)
    setResult(null)
    try {
      const response = await nodeAdminApi.register({
        hostAddress: hostAddress.trim(),
        username: username.trim(),
        password,
        nodeName: nodeName.trim() || null,
      })
      setResult(response)
      await onCompleted()
    } catch (deployError) {
      setFailure(errorMessage(deployError, '节点部署失败。'))
    } finally {
      setDeploying(false)
    }
  }

  const next = () => {
    if (step === 3) void deploy()
    else setStep((current) => Math.min(4, current + 1))
  }

  const canContinue = step > 0 || Boolean(hostAddress.trim() && username.trim() && password)

  return (
    <VNextDialog
      description="通过 SSH 完成依赖检测、Agent 安装和节点注册。"
      eyebrow="NODE BOOTSTRAP"
      footer={
        <>
          {step > 0 && step < 4 ? (
            <ActionButton onClick={() => setStep((current) => current - 1)} type="button">
              上一步
            </ActionButton>
          ) : null}
          <ActionButton disabled={deploying} onClick={onClose} type="button">
            {result ? '关闭' : '取消'}
          </ActionButton>
          {step < 4 ? (
            <ActionButton disabled={!canContinue || deploying} onClick={next} tone="primary" type="button">
              {step === 3 ? '开始部署' : '下一步'}
            </ActionButton>
          ) : null}
        </>
      }
      onClose={() => {
        if (!deploying) onClose()
      }}
      open={open}
      title="添加运行节点"
      wide
    >
      <div className={styles.content}>
        <ol className={styles.stepper}>
          {steps.map((label, index) => (
            <li data-active={index === step || undefined} data-complete={index < step || undefined} key={label}>
              <span>{index < step ? <Check size={14} /> : index + 1}</span>
              <small>{label}</small>
            </li>
          ))}
        </ol>

        {step === 0 ? (
          <div className={styles.formGrid}>
            <TextField
              label="服务器地址"
              onValueChange={setHostAddress}
              placeholder="10.24.0.30"
              required
              value={hostAddress}
            />
            <TextField
              autoComplete="username"
              label="SSH 用户名"
              onValueChange={setUsername}
              required
              value={username}
            />
            <TextField
              autoComplete="current-password"
              label="SSH 密码"
              onValueChange={setPassword}
              required
              type="password"
              value={password}
            />
            <TextField hint="留空时使用服务器主机名。" label="节点名称" onValueChange={setNodeName} value={nodeName} />
          </div>
        ) : null}

        {step === 1 ? (
          <section className={styles.checkList}>
            <h3>自动检测项</h3>
            <ul>
              <li>
                <ShieldCheck size={17} />
                <span>SSH 连接与 sudo 权限</span>
              </li>
              <li>
                <ShieldCheck size={17} />
                <span>Docker、KVM 与硬件虚拟化能力</span>
              </li>
              <li>
                <ShieldCheck size={17} />
                <span>软件源、Agent 端口与镜像 Registry 连通性</span>
              </li>
              <li>
                <ShieldCheck size={17} />
                <span>公网端口池与 TeamLab 网络依赖</span>
              </li>
            </ul>
          </section>
        ) : null}

        {step === 2 ? (
          <dl className={styles.previewGrid}>
            <div>
              <dt>目标服务器</dt>
              <dd>{hostAddress}</dd>
            </div>
            <div>
              <dt>登录用户</dt>
              <dd>{username}</dd>
            </div>
            <div>
              <dt>节点名称</dt>
              <dd>{nodeName.trim() || '由服务器主机名确定'}</dd>
            </div>
            <div>
              <dt>凭据</dt>
              <dd>仅用于本次 SSH 部署</dd>
            </div>
          </dl>
        ) : null}

        {step === 3 ? (
          <section className={styles.confirmation}>
            <Server size={28} />
            <div>
              <h3>{nodeName.trim() || hostAddress}</h3>
              <p>部署请求将安装或更新节点依赖、启动 Agent，并等待节点向平台完成心跳注册。</p>
            </div>
          </section>
        ) : null}

        {step === 4 ? (
          <section className={styles.result}>
            {deploying ? (
              <>
                <LoaderCircle className={styles.spinner} size={30} />
                <h3>节点部署进行中</h3>
                <p>连接、检测、安装和首次心跳可能需要数分钟。</p>
              </>
            ) : result ? (
              <>
                <Check size={30} />
                <h3>{result.nodeName || hostAddress} 已注册</h3>
                <p>{result.message || `节点标识 ${result.nodeId}`}</p>
              </>
            ) : (
              <>
                <Server size={30} />
                <h3>节点部署未完成</h3>
              </>
            )}
          </section>
        ) : null}

        {failure ? <InlineFeedback tone="danger">{failure}</InlineFeedback> : null}
      </div>
    </VNextDialog>
  )
}
