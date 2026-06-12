import { Canvas } from '@react-three/fiber'
import { BrightnessContrast, EffectComposer } from '@react-three/postprocessing'
import { Suspense, useEffect, useMemo, useState } from 'react'
import FluidMesh from './FluidMesh'
import Pixelation from './postprocessing/Pixelation'
import { hexToRgbNormalized } from './utils/color'

interface FluidSceneProps {
  className?: string
  style?: React.CSSProperties
}

function useWebgl2Supported() {
  const [supported, setSupported] = useState(false)

  useEffect(() => {
    const canvas = document.createElement('canvas')
    setSupported(Boolean(canvas.getContext('webgl2')))
  }, [])

  return supported
}

export default function FluidScene({ className, style }: FluidSceneProps) {
  const webgl2Supported = useWebgl2Supported()
  const [hidden, setHidden] = useState(() => typeof document !== 'undefined' && document.hidden)

  useEffect(() => {
    const onVisibility = () => setHidden(document.hidden)
    document.addEventListener('visibilitychange', onVisibility)
    return () => document.removeEventListener('visibilitychange', onVisibility)
  }, [])

  const fluidParams = useMemo(
    () => ({
      speed: 0.18,
      density: 0.5,
      strength: 1.8,
      frequency: 4,
      color1: hexToRgbNormalized('#32F08C'),
      color2: hexToRgbNormalized('#FFFFFF'),
      mouseRadius: 0.3,
      mouseStrength: 1.3,
      mouseEase: 0.08,
      opacity: 0.4,
    }),
    []
  )

  const pixelationParams = useMemo(
    () => ({
      pixelSize: 5,
      pixelGap: 2,
      threshold: 0.87,
      greenRatio: 0.49,
      colorNoise: 0.5,
      bgColor: [0, 0, 0],
    }),
    []
  )

  if (!webgl2Supported) {
    return <div className={className} style={style} data-fallback />
  }

  return (
    <div className={className} style={style} aria-hidden>
      <div
        className="yy-signal-field-mask"
        style={{
          background: `linear-gradient(to bottom, rgba(5,5,5,1) 0%, rgba(5,5,5,${
            1 - fluidParams.opacity
          }) 30%, rgba(5,5,5,${1 - fluidParams.opacity}) 60%, rgba(5,5,5,1) 100%)`,
        }}
      />
      {!hidden && (
        <Canvas
          style={{
            width: '100%',
            height: '100%',
            background: 'black',
            position: 'absolute',
            inset: 0,
            zIndex: 0,
          }}
          dpr={[0.75, 1.1]}
          performance={{ min: 0.35 }}
          resize={{ scroll: false }}
          frameloop="always"
          gl={{
            antialias: false,
            powerPreference: 'high-performance',
            depth: false,
            stencil: false,
          }}
        >
          <FluidMesh {...fluidParams} />
          <Suspense fallback={null}>
            <EffectComposer multisampling={0}>
              <BrightnessContrast brightness={-0.2} />
              <Pixelation
                {...pixelationParams}
                greenColor={fluidParams.color1}
                mouseRadius={fluidParams.mouseRadius}
                mouseStrength={fluidParams.mouseStrength}
                color1={fluidParams.color1}
                color2={fluidParams.color2}
              />
            </EffectComposer>
          </Suspense>
        </Canvas>
      )}
    </div>
  )
}
