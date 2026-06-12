import yinyuIcon from '../../assets/yinyu-icon-transparent.png'

export function BrandMark({ className = '', mono = false }: { className?: string; mono?: boolean }) {
  return (
    <span className={`brand-mark ${mono ? 'is-mono' : ''} ${className}`} role="img" aria-label="YINYU">
      <img src={yinyuIcon} alt="" draggable="false" />
    </span>
  )
}
