import yinyuIcon from '../../assets/yinyu-icon-transparent.png'

export function BrandMark({ className = '', mono = false, src }: { className?: string; mono?: boolean; src?: string | null }) {
  return (
    <span className={`brand-mark ${mono ? 'is-mono' : ''} ${className}`} role="img" aria-label="YINYU">
      <img src={src || yinyuIcon} alt="" draggable="false" />
    </span>
  )
}
