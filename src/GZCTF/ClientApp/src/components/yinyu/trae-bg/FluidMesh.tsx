import { useFrame, useThree } from '@react-three/fiber'
import { useEffect, useMemo, useRef } from 'react'
import * as THREE from 'three'
import { FluidShader } from './shaders/fluidShader'

interface FluidMeshProps {
  speed?: number
  density?: number
  strength?: number
  frequency?: number
  color1?: number[]
  color2?: number[]
  mouseRadius?: number
  mouseStrength?: number
  mouseEase?: number
}

export default function FluidMesh({
  speed = 0.15,
  density = 0.56,
  strength = 4.3,
  frequency = 8,
  color1 = [0.196, 0.941, 0.549],
  color2 = [1, 1, 1],
  mouseRadius = 0.3,
  mouseStrength = 1.3,
  mouseEase = 0.08,
}: FluidMeshProps) {
  const materialRef = useRef<THREE.ShaderMaterial>(null)
  const { size, viewport } = useThree()
  const mousePos = useRef(new THREE.Vector2(0.5, 0.5))
  const smoothMouse = useRef(new THREE.Vector2(0.5, 0.5))
  const lastResolution = useRef({ width: size.width, height: size.height })

  const uniforms = useMemo(
    () => ({
      uTime: { value: 0 },
      uResolution: { value: new THREE.Vector2(size.width, size.height) },
      uSpeed: { value: speed },
      uDensity: { value: density },
      uStrength: { value: strength },
      uFrequency: { value: frequency },
      uColor1: { value: new THREE.Color(...(color1 as [number, number, number])) },
      uColor2: { value: new THREE.Color(...(color2 as [number, number, number])) },
      uMouse: { value: new THREE.Vector2(0.5, 0.5) },
      uMouseRadius: { value: mouseRadius },
      uMouseStrength: { value: mouseStrength },
    }),
    []
  )

  useEffect(() => {
    const handleMouseMove = (e: MouseEvent) => {
      mousePos.current.set(e.clientX / window.innerWidth, 1 - e.clientY / window.innerHeight)
    }
    window.addEventListener('mousemove', handleMouseMove, { passive: true })
    return () => window.removeEventListener('mousemove', handleMouseMove)
  }, [])

  useFrame((state) => {
    const material = materialRef.current
    if (!material) return

    if (lastResolution.current.width !== size.width || lastResolution.current.height !== size.height) {
      material.uniforms.uResolution.value.set(size.width, size.height)
      lastResolution.current = { width: size.width, height: size.height }
    }
    material.uniforms.uTime.value = state.clock.elapsedTime

    smoothMouse.current.x += (mousePos.current.x - smoothMouse.current.x) * mouseEase
    smoothMouse.current.y += (mousePos.current.y - smoothMouse.current.y) * mouseEase
    material.uniforms.uMouse.value.copy(smoothMouse.current)
  })

  return (
    <mesh position={[0, 0, 0]} scale={[viewport.width, viewport.height, 1]} frustumCulled={false}>
      <planeGeometry args={[1, 1]} />
      <shaderMaterial
        ref={materialRef}
        vertexShader={FluidShader.vertexShader}
        fragmentShader={FluidShader.fragmentShader}
        uniforms={uniforms}
      />
    </mesh>
  )
}
