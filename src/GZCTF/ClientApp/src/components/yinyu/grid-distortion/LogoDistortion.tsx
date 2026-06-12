import { useEffect, useState } from 'react'
import yinyuIcon from '../../../assets/yinyu-icon-transparent.png'
import Distortion from './Distortion'

function supportsGridDistortion() {
  const canvas = document.createElement('canvas')
  const webgl2 = canvas.getContext('webgl2')
  if (webgl2) return true

  const webgl = canvas.getContext('webgl') || canvas.getContext('experimental-webgl')
  return Boolean(webgl && 'getExtension' in webgl && webgl.getExtension('OES_texture_float'))
}

export function LogoDistortion({ className = '' }: { className?: string }) {
  const [supported, setSupported] = useState(false)

  useEffect(() => {
    setSupported(supportsGridDistortion())
  }, [])

  return (
    <div className={['logo-distortion', 'yy-logo-distortion', className].filter(Boolean).join(' ')}>
      {supported ? (
        <Distortion imageSrc={yinyuIcon} grid={26} mouse={0.44} strength={0.24} relaxation={0.86} />
      ) : (
        <img
          className="logo-distortion-fallback yy-logo-distortion-fallback"
          src={yinyuIcon}
          alt=""
          draggable="false"
        />
      )}
    </div>
  )
}
