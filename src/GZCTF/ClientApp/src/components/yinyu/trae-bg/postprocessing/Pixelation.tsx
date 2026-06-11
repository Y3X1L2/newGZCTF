import { useFrame, useThree } from '@react-three/fiber'
import { BlendFunction } from 'postprocessing'
import { forwardRef, useEffect, useMemo, useRef } from 'react'
import * as THREE from 'three'
import { PixelationEffect } from './PixelationEffect'

interface PixelationProps {
  pixelSize?: number
  pixelGap?: number
  threshold?: number
  greenRatio?: number
  colorNoise?: number
  bgColor?: number[]
  greenColor?: number[]
  blendFunction?: BlendFunction
  mouseRadius?: number
  mouseStrength?: number
  color1?: number[]
  color2?: number[]
}

const Pixelation = forwardRef<PixelationEffect, PixelationProps>(
  (
    {
      pixelSize = 5,
      pixelGap = 2,
      threshold = 0.87,
      greenRatio = 0.49,
      colorNoise = 0.5,
      bgColor = [0, 0, 0],
      greenColor = [0.196, 0.941, 0.549],
      blendFunction,
      mouseRadius = 0.1,
      mouseStrength = 1,
      color1 = [0.196, 0.941, 0.549],
      color2 = [1, 1, 1],
    },
    ref
  ) => {
    const { size } = useThree()
    const lastResize = useRef(0)
    const effect = useMemo(
      () =>
        new PixelationEffect({
          pixelSize,
          pixelGap,
          threshold,
          greenRatio,
          colorNoise,
          bgColor,
          greenColor,
          resolution: new THREE.Vector2(size.width, size.height),
          blendFunction,
          mousePosition: new THREE.Vector2(0.5, 0.5),
          mouseRadius,
          mouseStrength,
          color1,
          color2,
        }),
      []
    )

    useEffect(() => {
      const handleMouseMove = (e: MouseEvent) => {
        effect.setMousePosition(e.clientX / window.innerWidth, 1 - e.clientY / window.innerHeight)
      }
      window.addEventListener('mousemove', handleMouseMove, { passive: true })
      return () => window.removeEventListener('mousemove', handleMouseMove)
    }, [effect])

    useFrame(() => {
      const now = Date.now()
      if (now - lastResize.current > 200) {
        effect.setResolution(size.width, size.height)
        lastResize.current = now
      }
    })

    useEffect(() => {
      ;[
        ['pixelSize', pixelSize],
        ['pixelGap', pixelGap],
        ['threshold', threshold],
        ['greenRatio', greenRatio],
        ['colorNoise', colorNoise],
      ].forEach(([name, value]) => {
        const u = effect.uniforms.get(String(name))
        if (u) u.value = value
      })
      const bg = effect.uniforms.get('bgColor')
      if (bg) bg.value = bgColor
      const gc = effect.uniforms.get('greenColor')
      if (gc) gc.value = greenColor
      effect.setMouseParams(mouseRadius, mouseStrength)
      effect.setColors(color1, color2)
    }, [
      bgColor,
      color1,
      color2,
      colorNoise,
      effect,
      greenColor,
      greenRatio,
      mouseRadius,
      mouseStrength,
      pixelGap,
      pixelSize,
      threshold,
    ])

    return <primitive ref={ref} object={effect} dispose={null} />
  }
)

Pixelation.displayName = 'Pixelation'

export default Pixelation
