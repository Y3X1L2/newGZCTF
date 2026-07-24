import { Turnstile, TurnstileInstance } from '@marsidev/react-turnstile'
import { Check, Cpu, LoaderCircle, RefreshCw, TriangleAlert } from 'lucide-react'
import { forwardRef, useCallback, useEffect, useImperativeHandle, useRef, useState } from 'react'
import { CaptchaProvider, HashPowChallenge } from '@Api'
import { useVNextTheme } from '../../app/VNextThemeProvider'
import { authApi } from './api/authApi'
import styles from './CaptchaField.module.css'

export interface CaptchaHandle {
  getToken: () => Promise<{ valid: boolean; token: string | null }>
  reset: () => Promise<void>
}

interface PowResult {
  nonce: string | null
  time: number
  rate: number
}

export const CaptchaField = forwardRef<CaptchaHandle, { action: string; disabled?: boolean }>(
  ({ action, disabled }, ref) => {
    const { theme } = useVNextTheme()
    const [provider, setProvider] = useState<CaptchaProvider | null>(null)
    const [siteKey, setSiteKey] = useState<string | null>(null)
    const [challenge, setChallenge] = useState<HashPowChallenge | null>(null)
    const [powResult, setPowResult] = useState<PowResult | null>(null)
    const [error, setError] = useState<string | null>(null)
    const [loading, setLoading] = useState(true)
    const workerRef = useRef<Worker | null>(null)
    const turnstileRef = useRef<TurnstileInstance>(null)

    const startPow = useCallback(async () => {
      setError(null)
      setPowResult(null)
      try {
        const next = await authApi.powChallenge()
        setChallenge(next)
        workerRef.current?.postMessage({ challenge: next.challenge ?? '', difficulty: next.difficulty ?? 18 })
      } catch {
        setError('安全计算初始化失败，请重试。')
      }
    }, [])

    const loadCaptchaInfo = useCallback(async () => {
      setLoading(true)
      setError(null)
      setProvider(null)
      setSiteKey(null)
      try {
        const info = await authApi.captchaInfo()
        const nextProvider = info.type ?? CaptchaProvider.None
        setProvider(nextProvider)
        setSiteKey(info.siteKey || null)
        if (nextProvider === CaptchaProvider.CloudflareTurnstile && !info.siteKey) {
          setError('安全验证配置缺少站点密钥。')
        }
      } catch {
        setError('无法读取安全验证配置。')
      } finally {
        setLoading(false)
      }
    }, [])

    useEffect(() => {
      void loadCaptchaInfo()
    }, [loadCaptchaInfo])

    useEffect(() => {
      if (provider !== CaptchaProvider.HashPow) return
      const worker = new Worker(new URL('./hashPow.worker.ts', import.meta.url), { type: 'module' })
      worker.onmessage = (event: MessageEvent<PowResult>) => {
        if (event.data.nonce) setPowResult(event.data)
        else setError('当前浏览器无法完成安全计算。')
      }
      worker.onerror = () => setError('安全计算执行失败，请重试。')
      workerRef.current = worker
      void startPow()
      return () => {
        workerRef.current = null
        worker.terminate()
      }
    }, [provider, startPow])

    useImperativeHandle(
      ref,
      () => ({
        getToken: async () => {
          if (provider === CaptchaProvider.None) return { valid: true, token: null }
          if (provider === CaptchaProvider.HashPow) {
            const token = challenge?.id && powResult?.nonce ? `${challenge.id}:${powResult.nonce}` : null
            return { valid: Boolean(token), token }
          }
          const token = turnstileRef.current?.getResponse() ?? null
          return { valid: Boolean(token), token }
        },
        reset: async () => {
          if (provider === CaptchaProvider.HashPow) await startPow()
          else turnstileRef.current?.reset()
        },
      }),
      [challenge?.id, powResult?.nonce, provider, startPow]
    )

    if (loading) {
      return (
        <div className={styles.pow} role="status">
          <span className={styles.powIcon} data-loading>
            <LoaderCircle size={17} />
          </span>
          <span>
            <strong>正在读取安全验证配置</strong>
            <small>请稍候</small>
          </span>
        </div>
      )
    }

    if (provider === CaptchaProvider.None) return <div aria-hidden="true" className={styles.empty} />

    if (provider === CaptchaProvider.CloudflareTurnstile && siteKey) {
      const nonce = document.getElementById('nonce-container')?.getAttribute('data-nonce') ?? undefined
      return (
        <div className={styles.turnstile} data-disabled={disabled || undefined}>
          <Turnstile
            options={{ action, theme }}
            ref={turnstileRef}
            scriptOptions={{ nonce }}
            siteKey={siteKey}
          />
        </div>
      )
    }

    if (provider !== CaptchaProvider.HashPow) {
      return (
        <div className={styles.pow} role="alert">
          <span className={styles.powIcon}>
            <TriangleAlert size={17} />
          </span>
          <span>
            <strong>安全验证配置异常</strong>
            <small>{error ?? '当前验证方式暂不可用。'}</small>
          </span>
          <button aria-label="重新读取安全验证配置" disabled={disabled} onClick={() => void loadCaptchaInfo()} type="button">
            <RefreshCw size={16} />
          </button>
        </div>
      )
    }

    return (
      <div className={styles.pow} role="status">
        <span
          className={styles.powIcon}
          data-complete={Boolean(powResult?.nonce) || undefined}
          data-loading={!error && !powResult?.nonce ? true : undefined}
        >
          {error ? <TriangleAlert size={17} /> : powResult?.nonce ? <Check size={17} /> : <Cpu size={17} />}
        </span>
        <span>
          <strong>{error ? '安全验证异常' : powResult?.nonce ? '安全验证已就绪' : '正在完成安全计算'}</strong>
          <small>
            {error
              ? error
              : powResult
                ? `${(powResult.time / 1000).toFixed(2)}s · ${powResult.rate.toFixed(2)} kH/s`
                : '计算完成后即可提交表单'}
          </small>
        </span>
        {error ? (
          <button aria-label="重试安全验证" disabled={disabled} onClick={() => void startPow()} type="button">
            <RefreshCw size={16} />
          </button>
        ) : null}
      </div>
    )
  }
)
