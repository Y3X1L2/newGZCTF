import { useEffect, useRef } from 'react'
import * as THREE from 'three'

interface DistortionProps {
  grid?: number
  mouse?: number
  strength?: number
  relaxation?: number
  imageSrc: string
  className?: string
}

export default function Distortion({
  grid = 18,
  mouse = 0.22,
  strength = 0.12,
  relaxation = 0.88,
  imageSrc,
  className = '',
}: DistortionProps) {
  const containerRef = useRef<HTMLDivElement>(null)

  useEffect(() => {
    const container = containerRef.current
    if (!container) return undefined

    const scene = new THREE.Scene()
    const renderer = new THREE.WebGLRenderer({ antialias: true, alpha: true, powerPreference: 'high-performance' })
    renderer.setClearColor(0x000000, 0)
    renderer.setPixelRatio(Math.min(window.devicePixelRatio || 1, 1.5))
    container.appendChild(renderer.domElement)

    const camera = new THREE.OrthographicCamera(0, 0, 0, 0, -1000, 1000)
    camera.position.z = 2

    const uniforms: {
      uTexture: { value: THREE.Texture | null }
      uDataTexture: { value: THREE.DataTexture | null }
    } = {
      uTexture: { value: null },
      uDataTexture: { value: null },
    }

    const data = new Float32Array(4 * grid * grid)
    const dataTexture = new THREE.DataTexture(data, grid, grid, THREE.RGBAFormat, THREE.FloatType)
    dataTexture.needsUpdate = true
    uniforms.uDataTexture.value = dataTexture

    const material = new THREE.ShaderMaterial({
      transparent: true,
      depthWrite: false,
      side: THREE.DoubleSide,
      uniforms,
      vertexShader: /* glsl */ `
        varying vec2 vUv;
        void main() {
          vUv = uv;
          gl_Position = projectionMatrix * modelViewMatrix * vec4(position, 1.0);
        }
      `,
      fragmentShader: /* glsl */ `
        uniform sampler2D uDataTexture;
        uniform sampler2D uTexture;
        varying vec2 vUv;

        void main() {
          vec4 offset = texture2D(uDataTexture, vUv);
          vec4 texel = texture2D(uTexture, vUv - 0.022 * offset.rg);
          gl_FragColor = texel;
        }
      `,
    })

    const geometry = new THREE.PlaneGeometry(1, 1, grid - 1, grid - 1)
    const mesh = new THREE.Mesh(geometry, material)
    scene.add(mesh)

    const resize = () => {
      const width = Math.max(container.offsetWidth, 1)
      const height = Math.max(container.offsetHeight, 1)
      const aspect = width / height
      renderer.setSize(width, height)
      mesh.scale.set(aspect, 1, 1)
      camera.left = -aspect / 2
      camera.right = aspect / 2
      camera.top = 0.5
      camera.bottom = -0.5
      camera.updateProjectionMatrix()
    }

    const textureLoader = new THREE.TextureLoader()
    textureLoader.load(imageSrc, (texture) => {
      texture.minFilter = THREE.LinearFilter
      texture.magFilter = THREE.LinearFilter
      texture.colorSpace = THREE.SRGBColorSpace
      uniforms.uTexture.value = texture
      resize()
    })

    const mouseState = { x: 0, y: 0, prevX: 0, prevY: 0, vX: 0, vY: 0 }
    let settleFrames = 0
    let idleFrame = 0
    let pointerReady = false
    let visible = true
    const clampOffset = (value: number) => Math.max(-7.5, Math.min(7.5, value))

    const move = (event: PointerEvent) => {
      const rect = container.getBoundingClientRect()
      const x = (event.clientX - rect.left) / rect.width
      const y = 1 - (event.clientY - rect.top) / rect.height

      if (!pointerReady) {
        mouseState.prevX = x
        mouseState.prevY = y
        pointerReady = true
      }

      mouseState.vX = x - mouseState.prevX
      mouseState.vY = y - mouseState.prevY
      mouseState.x = x
      mouseState.y = y
      mouseState.prevX = x
      mouseState.prevY = y
      settleFrames = 90
    }

    const enter = (event: PointerEvent) => {
      const rect = container.getBoundingClientRect()
      const x = (event.clientX - rect.left) / rect.width
      const y = 1 - (event.clientY - rect.top) / rect.height
      pointerReady = true
      mouseState.x = x
      mouseState.y = y
      mouseState.prevX = x
      mouseState.prevY = y
      mouseState.vX = 0.018
      mouseState.vY = -0.012
      settleFrames = 46
    }

    const leave = () => {
      mouseState.vX = 0
      mouseState.vY = 0
      pointerReady = false
      settleFrames = 45
    }

    const onVisibility = () => {
      visible = !document.hidden
    }

    const resizeObserver = new ResizeObserver(resize)
    resizeObserver.observe(container)
    container.addEventListener('pointerenter', enter)
    container.addEventListener('pointermove', move)
    container.addEventListener('pointerleave', leave)
    document.addEventListener('visibilitychange', onVisibility)
    resize()

    let animationId = 0
    const animate = () => {
      animationId = requestAnimationFrame(animate)
      if (!visible) return

      const isSettling = settleFrames > 0
      if (!isSettling) {
        idleFrame = (idleFrame + 1) % 6
        if (idleFrame !== 0) return
      }

      const imageData = dataTexture.image.data as Float32Array
      for (let i = 0; i < grid * grid; i += 1) {
        imageData[4 * i] *= relaxation
        imageData[4 * i + 1] *= relaxation
      }

      if (isSettling) {
        const mouseX = grid * mouseState.x
        const mouseY = grid * mouseState.y
        const radius = grid * mouse

        for (let x = 0; x < grid; x += 1) {
          for (let y = 0; y < grid; y += 1) {
            const distSq = (mouseX - x) ** 2 + (mouseY - y) ** 2
            if (distSq < radius * radius) {
              const index = 4 * (x + grid * y)
              const factor = Math.min(radius / Math.sqrt(Math.max(distSq, 0.001)), 10)
              imageData[index] = clampOffset(imageData[index] + 180 * strength * mouseState.vX * factor)
              imageData[index + 1] = clampOffset(imageData[index + 1] - 180 * strength * mouseState.vY * factor)
            }
          }
        }
        settleFrames -= 1
      }

      dataTexture.needsUpdate = true
      renderer.render(scene, camera)
    }

    animate()

    return () => {
      cancelAnimationFrame(animationId)
      resizeObserver.disconnect()
      container.removeEventListener('pointerenter', enter)
      container.removeEventListener('pointermove', move)
      container.removeEventListener('pointerleave', leave)
      document.removeEventListener('visibilitychange', onVisibility)
      renderer.dispose()
      geometry.dispose()
      material.dispose()
      dataTexture.dispose()
      uniforms.uTexture.value?.dispose()
      renderer.domElement.remove()
    }
  }, [grid, imageSrc, mouse, relaxation, strength])

  return (
    <div
      ref={containerRef}
      className={['grid-distortion-canvas', 'yy-grid-distortion-canvas', className].filter(Boolean).join(' ')}
    />
  )
}
