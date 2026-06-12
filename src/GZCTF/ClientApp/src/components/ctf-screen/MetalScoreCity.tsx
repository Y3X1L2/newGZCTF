import { FC, useEffect, useRef } from 'react'
import * as THREE from 'three'
import type { Team } from './useCTFScreenData'

interface MetalScoreCityProps {
  teams: Team[]
}

interface TeamLayout {
  id: number
  team: Team
  x: number
  z: number
  height: number
  isPodium: boolean
}

interface CameraTarget {
  x: number
  y: number
  z: number
  lookAtY: number
}

interface BarRecord {
  group: THREE.Group
  mesh: THREE.Mesh<THREE.BoxGeometry, THREE.MeshStandardMaterial>
  edgeMaterial: THREE.LineBasicMaterial
  label: THREE.Sprite
  labelMaterial: THREE.SpriteMaterial
  labelTexture: THREE.CanvasTexture
  targetPosition: THREE.Vector3
  targetHeight: number
  targetColor: THREE.Color
  targetEmissive: THREE.Color
  targetLabelScale: THREE.Vector3
  lastLabelKey: string
}

interface BeamRecord {
  mesh: THREE.Mesh<THREE.BoxGeometry, THREE.MeshBasicMaterial>
  speed: number
  span: number
}

const GOLD = new THREE.Color(0xd8b45c)
const GOLD_EMISSIVE = new THREE.Color(0x4d3510)
const SILVER = new THREE.Color(0xc9d0d2)
const SILVER_EMISSIVE = new THREE.Color(0x111820)
const DIM_SILVER = new THREE.Color(0x6d7479)
const SCENE_CENTER = new THREE.Vector3(0, 0, 0)
const BAR_GEOMETRY = new THREE.BoxGeometry(1, 1, 1, 3, 1, 3)
const EDGE_GEOMETRY = new THREE.EdgesGeometry(BAR_GEOMETRY)

const clamp = (value: number, min: number, max: number) => Math.max(min, Math.min(max, value))
const ease = (current: number, target: number, factor: number) => current + (target - current) * factor

const hashString = (value: string) => {
  let hash = 2166136261
  for (let i = 0; i < value.length; i += 1) {
    hash ^= value.charCodeAt(i)
    hash = Math.imul(hash, 16777619)
  }
  return hash >>> 0
}

const seededRandom = (seed: number) => {
  let current = seed || 1
  return () => {
    current = Math.imul(current ^ (current >>> 15), 1 | current)
    current ^= current + Math.imul(current ^ (current >>> 7), 61 | current)
    return ((current ^ (current >>> 14)) >>> 0) / 4294967296
  }
}

const makeLayouts = (teams: Team[]): TeamLayout[] => {
  const ranked = [...teams].sort((left, right) => left.rank - right.rank || right.score - left.score)
  if (ranked.length === 0) return []

  const count = ranked.length
  const columns = Math.max(3, Math.ceil(Math.sqrt(count * 1.42)))
  const rows = Math.max(2, Math.ceil(count / columns))
  const spacing = count > 42 ? 1.22 : count > 20 ? 1.38 : 1.58
  const coords: Array<{ x: number; z: number; radius: number }> = []

  for (let row = 0; row < rows; row += 1) {
    for (let col = 0; col < columns; col += 1) {
      const x = (col - (columns - 1) / 2) * spacing
      const z = (row - (rows - 1) / 2) * spacing
      coords.push({ x, z, radius: Math.hypot(x, z) + Math.abs(row - rows / 2) * 0.03 })
    }
  }

  coords.sort((left, right) => left.radius - right.radius)

  const maxScore = Math.max(1, ...ranked.map((team) => team.score))

  return ranked.map((team, index) => {
    const coord = coords[index] ?? coords[coords.length - 1]
    const rand = seededRandom(hashString(`${team.id}-${team.name}`))
    const jitter = count > 48 ? 0.08 : 0.16
    const scoreRatio = Math.pow(clamp(team.score / maxScore, 0, 1), 0.72)
    const podiumBoost = team.rank <= 3 ? 0.85 - team.rank * 0.12 : 0

    return {
      id: team.id,
      team,
      x: coord.x + (rand() - 0.5) * jitter,
      z: coord.z + (rand() - 0.5) * jitter,
      height: 0.55 + scoreRatio * 7.6 + podiumBoost,
      isPodium: team.rank <= 3,
    }
  })
}

const resolveCameraTarget = (layouts: TeamLayout[], aspect: number): CameraTarget => {
  if (layouts.length === 0) return { x: 0, y: 9, z: 19, lookAtY: 1.8 }

  const highest = layouts.reduce((current, item) => (item.height > current.height ? item : current), layouts[0])
  let minX = Infinity
  let maxX = -Infinity
  let minZ = Infinity
  let maxZ = -Infinity
  let maxHeight = 0

  for (const layout of layouts) {
    minX = Math.min(minX, layout.x - 0.95)
    maxX = Math.max(maxX, layout.x + 0.95)
    minZ = Math.min(minZ, layout.z - 0.95)
    maxZ = Math.max(maxZ, layout.z + 0.95)
    maxHeight = Math.max(maxHeight, layout.height)
  }

  const width = Math.max(4.8, maxX - minX)
  const depth = Math.max(4.8, maxZ - minZ)
  const spread = Math.max(width / Math.max(aspect, 0.75), depth)
  const focusX = clamp(highest.x * 0.18, -1.4, 1.4)

  return {
    x: focusX,
    y: clamp(7.8 + spread * 0.28 + maxHeight * 0.52, 9.8, 19.5),
    z: clamp(12.5 + spread * 1.45 + maxHeight * 0.84, 18, 46),
    lookAtY: clamp(1.7 + maxHeight * 0.25, 2.1, 5.2),
  }
}

const makeLabelTexture = (team: Team, isPodium: boolean) => {
  const canvas = document.createElement('canvas')
  canvas.width = 512
  canvas.height = 176
  const ctx = canvas.getContext('2d')
  if (!ctx) return new THREE.CanvasTexture(canvas)

  ctx.clearRect(0, 0, canvas.width, canvas.height)
  const accent = isPodium ? '#f1ce79' : '#d7dde0'
  const sub = isPodium ? 'rgba(255, 224, 151, 0.82)' : 'rgba(224, 235, 238, 0.72)'

  const gradient = ctx.createLinearGradient(0, 0, canvas.width, canvas.height)
  gradient.addColorStop(0, isPodium ? 'rgba(116, 80, 20, 0.55)' : 'rgba(40, 48, 52, 0.58)')
  gradient.addColorStop(1, 'rgba(10, 12, 14, 0.72)')
  ctx.fillStyle = gradient
  ctx.strokeStyle = isPodium ? 'rgba(247, 207, 104, 0.92)' : 'rgba(229, 238, 240, 0.6)'
  ctx.lineWidth = 4
  ctx.beginPath()
  ctx.roundRect(16, 20, 480, 122, 22)
  ctx.fill()
  ctx.stroke()

  ctx.fillStyle = accent
  ctx.font = '700 42px Orbitron, Share Tech Mono, Microsoft YaHei, sans-serif'
  ctx.textAlign = 'center'
  ctx.textBaseline = 'middle'
  const displayName = team.name.length > 16 ? `${team.name.slice(0, 15)}...` : team.name
  ctx.fillText(displayName, 256, 68)

  ctx.fillStyle = sub
  ctx.font = '600 28px Share Tech Mono, Microsoft YaHei, sans-serif'
  ctx.fillText(`#${team.rank.toString().padStart(2, '0')}  ${team.score} 分`, 256, 112)

  const texture = new THREE.CanvasTexture(canvas)
  texture.colorSpace = THREE.SRGBColorSpace
  texture.anisotropy = 4
  return texture
}

const disposeRecord = (record: BarRecord) => {
  record.mesh.material.dispose()
  record.edgeMaterial.dispose()
  record.labelMaterial.dispose()
  record.labelTexture.dispose()
}

const syncTeamRecords = (
  teams: Team[],
  bars: THREE.Group,
  records: Map<number, BarRecord>,
  targetCamera: { current: CameraTarget },
  aspect: number
) => {
  const layouts = makeLayouts(teams)
  const seen = new Set<number>()
  targetCamera.current = resolveCameraTarget(layouts, aspect)

  for (const layout of layouts) {
    seen.add(layout.id)
    const labelKey = `${layout.team.rank}-${layout.team.name}-${layout.team.score}-${layout.isPodium}`
    const targetColor = layout.isPodium ? GOLD.clone() : SILVER.clone()
    const targetEmissive = layout.isPodium ? GOLD_EMISSIVE.clone() : SILVER_EMISSIVE.clone()
    let record = records.get(layout.id)

    if (!record) {
      const group = new THREE.Group()
      group.position.set(layout.x, 0, layout.z)

      const material = new THREE.MeshStandardMaterial({
        color: DIM_SILVER.clone(),
        emissive: new THREE.Color(0x090b0c),
        metalness: 0.92,
        roughness: 0.22,
      })
      const mesh = new THREE.Mesh(BAR_GEOMETRY, material)
      mesh.castShadow = true
      mesh.receiveShadow = true
      mesh.scale.set(0.92, 0.2, 0.92)
      mesh.position.y = 0.1

      const edgeMaterial = new THREE.LineBasicMaterial({
        color: layout.isPodium ? 0xffe19b : 0xe7eff0,
        transparent: true,
        opacity: 0.34,
      })
      const edge = new THREE.LineSegments(EDGE_GEOMETRY, edgeMaterial)
      mesh.add(edge)

      const labelTexture = makeLabelTexture(layout.team, layout.isPodium)
      const labelMaterial = new THREE.SpriteMaterial({
        map: labelTexture,
        transparent: true,
        opacity: 0.92,
        depthTest: false,
        depthWrite: false,
      })
      const label = new THREE.Sprite(labelMaterial)
      label.renderOrder = 20
      label.scale.set(2.35, 0.82, 1)
      label.position.y = 1.2

      group.add(mesh, label)
      bars.add(group)

      record = {
        group,
        mesh,
        edgeMaterial,
        label,
        labelMaterial,
        labelTexture,
        targetPosition: new THREE.Vector3(layout.x, 0, layout.z),
        targetHeight: layout.height,
        targetColor,
        targetEmissive,
        targetLabelScale: new THREE.Vector3(layout.isPodium ? 2.8 : 2.35, layout.isPodium ? 0.94 : 0.82, 1),
        lastLabelKey: labelKey,
      }
      records.set(layout.id, record)
    }

    record.targetPosition.set(layout.x, 0, layout.z)
    record.targetHeight = layout.height
    record.targetColor.copy(targetColor)
    record.targetEmissive.copy(targetEmissive)
    record.targetLabelScale.set(layout.isPodium ? 2.8 : 2.35, layout.isPodium ? 0.94 : 0.82, 1)
    record.edgeMaterial.color.lerp(layout.isPodium ? GOLD : SILVER, 0.7)
    record.edgeMaterial.opacity = layout.isPodium ? 0.54 : 0.32

    if (record.lastLabelKey !== labelKey) {
      record.labelTexture.dispose()
      const texture = makeLabelTexture(layout.team, layout.isPodium)
      record.labelTexture = texture
      record.labelMaterial.map = texture
      record.labelMaterial.needsUpdate = true
      record.lastLabelKey = labelKey
    }
  }

  for (const [id, record] of records) {
    if (seen.has(id)) continue
    bars.remove(record.group)
    disposeRecord(record)
    records.delete(id)
  }
}

export const MetalScoreCity: FC<MetalScoreCityProps> = ({ teams }) => {
  const hostRef = useRef<HTMLDivElement | null>(null)
  const teamsRef = useRef<Team[]>(teams)
  const rendererRef = useRef<THREE.WebGLRenderer | null>(null)
  const cameraRef = useRef<THREE.PerspectiveCamera | null>(null)
  const cityRef = useRef<THREE.Group | null>(null)
  const barsRef = useRef<THREE.Group | null>(null)
  const recordsRef = useRef(new Map<number, BarRecord>())
  const beamsRef = useRef<BeamRecord[]>([])
  const animationRef = useRef<number | null>(null)
  const mouseRef = useRef({ x: 0, y: 0 })
  const rotationSpeedRef = useRef(0.08)
  const targetCameraRef = useRef<CameraTarget>({ x: 0, y: 9, z: 19, lookAtY: 1.8 })

  useEffect(() => {
    teamsRef.current = teams
    const bars = barsRef.current
    const camera = cameraRef.current
    if (!bars) return
    syncTeamRecords(teams, bars, recordsRef.current, targetCameraRef, camera?.aspect ?? 1.6)
  }, [teams])

  useEffect(() => {
    const host = hostRef.current
    if (!host) return undefined

    const renderer = new THREE.WebGLRenderer({
      antialias: true,
      alpha: false,
      powerPreference: 'high-performance',
    })
    renderer.setPixelRatio(Math.min(window.devicePixelRatio || 1, 1.6))
    renderer.setSize(host.clientWidth || 1200, host.clientHeight || 720)
    renderer.outputColorSpace = THREE.SRGBColorSpace
    renderer.shadowMap.enabled = true
    renderer.shadowMap.type = THREE.PCFSoftShadowMap
    host.appendChild(renderer.domElement)
    rendererRef.current = renderer

    const scene = new THREE.Scene()
    scene.background = new THREE.Color(0x1b1e20)
    scene.fog = new THREE.Fog(0x24282b, 18, 58)

    const camera = new THREE.PerspectiveCamera(25, (host.clientWidth || 1200) / (host.clientHeight || 720), 0.1, 160)
    camera.position.set(0, 8.5, 19)
    cameraRef.current = camera

    const city = new THREE.Group()
    city.rotation.x = 0.34
    scene.add(city)
    cityRef.current = city

    const bars = new THREE.Group()
    city.add(bars)
    barsRef.current = bars

    const ambient = new THREE.HemisphereLight(0xffffff, 0x2a2d30, 4.1)
    scene.add(ambient)

    const keyLight = new THREE.DirectionalLight(0xffffff, 6.2)
    keyLight.position.set(-8, 15, 10)
    keyLight.castShadow = true
    keyLight.shadow.mapSize.set(1536, 1536)
    keyLight.shadow.camera.near = 0.5
    keyLight.shadow.camera.far = 64
    keyLight.shadow.camera.left = -18
    keyLight.shadow.camera.right = 18
    keyLight.shadow.camera.top = 18
    keyLight.shadow.camera.bottom = -18
    scene.add(keyLight)

    const rimLight = new THREE.PointLight(0xffdf8e, 13, 54)
    rimLight.position.set(10, 9, -10)
    scene.add(rimLight)

    const coolFill = new THREE.PointLight(0xf5fbff, 5.4, 48)
    coolFill.position.set(-12, 5, 9)
    scene.add(coolFill)

    const highBackLight = new THREE.DirectionalLight(0xf8f3e4, 3.8)
    highBackLight.position.set(7, 18, -14)
    scene.add(highBackLight)

    const groundMaterial = new THREE.MeshStandardMaterial({
      color: 0x3a3d3f,
      metalness: 0.72,
      roughness: 0.34,
      transparent: true,
      opacity: 0.84,
    })
    const ground = new THREE.Mesh(new THREE.PlaneGeometry(54, 54), groundMaterial)
    ground.rotation.x = -Math.PI / 2
    ground.position.y = -0.02
    ground.receiveShadow = true
    city.add(ground)

    const grid = new THREE.GridHelper(54, 54, 0xd5dadd, 0x555c60)
    grid.position.y = 0.01
    const gridMaterial = grid.material as THREE.Material | THREE.Material[]
    if (Array.isArray(gridMaterial)) {
      gridMaterial.forEach((material) => {
        material.transparent = true
        material.opacity = 0.22
      })
    } else {
      gridMaterial.transparent = true
      gridMaterial.opacity = 0.22
    }
    city.add(grid)

    const particleGeometry = new THREE.BufferGeometry()
    const particleCount = 260
    const particlePositions = new Float32Array(particleCount * 3)
    const particleRand = seededRandom(9147)
    for (let i = 0; i < particleCount; i += 1) {
      particlePositions[i * 3] = (particleRand() - 0.5) * 24
      particlePositions[i * 3 + 1] = 0.6 + particleRand() * 8.4
      particlePositions[i * 3 + 2] = (particleRand() - 0.5) * 24
    }
    particleGeometry.setAttribute('position', new THREE.BufferAttribute(particlePositions, 3))
    const particleMaterial = new THREE.PointsMaterial({
      color: 0xf0e2bd,
      size: 0.035,
      transparent: true,
      opacity: 0.42,
      depthWrite: false,
    })
    const particles = new THREE.Points(particleGeometry, particleMaterial)
    city.add(particles)

    const beamMaterial = new THREE.MeshBasicMaterial({
      color: 0xf2d68b,
      transparent: true,
      opacity: 0.35,
    })
    const beamGeometry = new THREE.BoxGeometry(1.8, 0.018, 0.018)
    const beamRand = seededRandom(38192)
    for (let i = 0; i < 38; i += 1) {
      const beam = new THREE.Mesh(beamGeometry, beamMaterial.clone())
      const lane = Math.round((beamRand() - 0.5) * 14)
      beam.position.set(lane * 0.72, 0.08 + beamRand() * 0.12, -15 + beamRand() * 30)
      beam.rotation.y = Math.PI / 2
      city.add(beam)
      beamsRef.current.push({
        mesh: beam,
        speed: 1.1 + beamRand() * 1.8,
        span: 15 + beamRand() * 6,
      })
    }

    const onPointerMove = (event: PointerEvent) => {
      const rect = host.getBoundingClientRect()
      mouseRef.current.x = ((event.clientX - rect.left) / Math.max(rect.width, 1)) * 2 - 1
      mouseRef.current.y = -(((event.clientY - rect.top) / Math.max(rect.height, 1)) * 2 - 1)
    }

    const resize = () => {
      const width = host.clientWidth || 1200
      const height = host.clientHeight || 720
      camera.aspect = width / height
      camera.updateProjectionMatrix()
      renderer.setSize(width, height, false)
      syncTeamRecords(teamsRef.current, bars, recordsRef.current, targetCameraRef, camera.aspect)
    }

    host.addEventListener('pointermove', onPointerMove, { passive: true })
    const resizeObserver = new ResizeObserver(resize)
    resizeObserver.observe(host)
    resize()

    const clock = new THREE.Clock()
    const animate = () => {
      const delta = Math.min(clock.getDelta(), 0.05)
      const elapsed = clock.elapsedTime
      const mouse = mouseRef.current

      const targetSpeed = 0.075 + Math.sin(elapsed * 0.29) * 0.026 + mouse.x * 0.012
      rotationSpeedRef.current = ease(rotationSpeedRef.current, targetSpeed, 0.018)
      city.rotation.y += rotationSpeedRef.current * delta
      const targetTilt = clamp(0.32 + Math.sin(elapsed * 0.22) * 0.055 + mouse.y * 0.085, 0.2, 0.56)
      city.rotation.x = ease(city.rotation.x, targetTilt, 0.035)

      particles.rotation.y += delta * 0.035
      particles.rotation.x = Math.sin(elapsed * 0.08) * 0.05

      for (const beam of beamsRef.current) {
        beam.mesh.position.z += beam.speed * delta
        if (beam.mesh.position.z > beam.span) beam.mesh.position.z = -beam.span
      }

      for (const record of recordsRef.current.values()) {
        record.group.position.lerp(record.targetPosition, 0.045)
        const nextHeight = ease(record.mesh.scale.y, record.targetHeight, 0.055)
        record.mesh.scale.y = nextHeight
        record.mesh.position.y = nextHeight / 2
        record.mesh.material.color.lerp(record.targetColor, 0.045)
        record.mesh.material.emissive.lerp(record.targetEmissive, 0.045)
        record.label.position.y = nextHeight + 0.66 + Math.sin(elapsed * 1.7 + record.group.position.x) * 0.055
        record.label.scale.lerp(record.targetLabelScale, 0.05)
      }

      camera.position.y = ease(camera.position.y, targetCameraRef.current.y, 0.025)
      camera.position.z = ease(camera.position.z, targetCameraRef.current.z, 0.025)
      camera.position.x = ease(camera.position.x, targetCameraRef.current.x + mouse.x * 0.5, 0.025)
      SCENE_CENTER.set(targetCameraRef.current.x * 0.25, targetCameraRef.current.lookAtY, 0)
      camera.lookAt(SCENE_CENTER)
      renderer.render(scene, camera)
      animationRef.current = window.requestAnimationFrame(animate)
    }

    syncTeamRecords(teamsRef.current, bars, recordsRef.current, targetCameraRef, camera.aspect)

    animationRef.current = window.requestAnimationFrame(animate)

    return () => {
      if (animationRef.current) window.cancelAnimationFrame(animationRef.current)
      resizeObserver.disconnect()
      host.removeEventListener('pointermove', onPointerMove)
      recordsRef.current.forEach(disposeRecord)
      recordsRef.current.clear()
      beamsRef.current.forEach((beam) => beam.mesh.material.dispose())
      beamsRef.current = []
      beamGeometry.dispose()
      particleGeometry.dispose()
      particleMaterial.dispose()
      ground.geometry.dispose()
      groundMaterial.dispose()
      renderer.dispose()
      renderer.domElement.remove()
      rendererRef.current = null
      cameraRef.current = null
      cityRef.current = null
      barsRef.current = null
    }
  }, [])

  return <div ref={hostRef} className="metal-city-canvas" />
}
