import { FC, useEffect, useRef } from 'react'
import * as THREE from 'three'
import type { Team } from './useCTFScreenData'

interface MetalScoreCityProps {
  teams: Team[]
  selectedTeamId?: number | null
  onSelectTeam?: (team: Team) => void
}

interface TeamLayout {
  id: number
  team: Team
  x: number
  z: number
  labelX: number
  labelZ: number
  labelLift: number
  height: number
  isPodium: boolean
}

interface CameraTarget {
  x: number
  zFocus: number
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
  targetLabelPosition: THREE.Vector3
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
const FOCUS_CENTER = new THREE.Vector3(0, 0, 0)
const FOCUS_LABEL_CENTER = new THREE.Vector3(0, 0, 0)
const BAR_GEOMETRY = new THREE.BoxGeometry(1, 1, 1, 3, 1, 3)
const EDGE_GEOMETRY = new THREE.EdgesGeometry(BAR_GEOMETRY)
const TARGET_COLOR = new THREE.Color()
const TARGET_EMISSIVE = new THREE.Color()
const FOCUS_GOLD_EDGE = new THREE.Color(0xffe28a)
const FOCUS_SILVER_EDGE = new THREE.Color(0xf5fbff)
const FOCUS_GOLD_EMISSIVE = new THREE.Color(0x7a5417)
const FOCUS_SILVER_EMISSIVE = new THREE.Color(0x2d383c)
const LABEL_SCALE_TARGET = new THREE.Vector3()

const clamp = (value: number, min: number, max: number) => Math.max(min, Math.min(max, value))
const ease = (current: number, target: number, factor: number) => current + (target - current) * factor
const isGoldColor = (color: THREE.Color) => color.r > 0.68 && color.g > 0.48 && color.b < 0.4

const createMetalEnvironment = () => {
  const faces = [
    ['#f2f5f2', '#777c7d'],
    ['#202426', '#d8dedc'],
    ['#ffffff', '#c2c6c2'],
    ['#2a2e30', '#080a0b'],
    ['#f0d894', '#2f3030'],
    ['#dfe4e2', '#111416'],
  ] as const
  const canvases = faces.map(([top, bottom]) => {
    const canvas = document.createElement('canvas')
    canvas.width = 64
    canvas.height = 64
    const ctx = canvas.getContext('2d')
    if (!ctx) return canvas
    const gradient = ctx.createLinearGradient(0, 0, 64, 64)
    gradient.addColorStop(0, top)
    gradient.addColorStop(0.46, '#8b9293')
    gradient.addColorStop(1, bottom)
    ctx.fillStyle = gradient
    ctx.fillRect(0, 0, 64, 64)
    ctx.fillStyle = 'rgba(255, 255, 255, 0.32)'
    ctx.fillRect(0, 11, 64, 7)
    ctx.fillStyle = 'rgba(244, 215, 137, 0.2)'
    ctx.fillRect(0, 42, 64, 5)
    return canvas
  })
  const texture = new THREE.CubeTexture(canvases)
  texture.colorSpace = THREE.SRGBColorSpace
  texture.needsUpdate = true
  return texture
}

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
  const stableTeams = [...teams]
    .map((team) => ({ team, seed: hashString(`${team.id}-${team.name}`) }))
    .sort((left, right) => left.seed - right.seed)

  if (stableTeams.length === 0) return []

  const count = stableTeams.length
  const maxScore = Math.max(1, ...stableTeams.map(({ team }) => team.score))
  const radius = clamp(4.6 + Math.sqrt(count) * 1.02, 6.2, 15.2)
  const pointLimit = radius * 1.08
  const minDistance = clamp(2.18 - count * 0.012, 1.58, 2.02)
  const labelMinDistance = clamp(3.35 - count * 0.018, 2.32, 3)
  const points = stableTeams.map(({ team, seed }, index) => {
    const rand = seededRandom(seed)
    const angle = rand() * Math.PI * 2
    const ring = 0.22 + Math.sqrt(rand()) * 0.86
    const radialNoise = 0.88 + rand() * 0.22
    const spiral = Math.sin(index * 1.618 + seed * 0.00001) * 0.38

    return {
      team,
      seed,
      x: Math.cos(angle + spiral) * radius * ring * radialNoise,
      z: Math.sin(angle + spiral) * radius * ring * radialNoise,
      labelX: 0,
      labelZ: 0,
      labelLift: 0,
    }
  })

  const clampPoint = (point: { x: number; z: number }, limit: number) => {
    const length = Math.hypot(point.x, point.z)
    if (length <= limit || length === 0) return
    const scale = limit / length
    point.x *= scale
    point.z *= scale
  }

  for (let iteration = 0; iteration < 9; iteration += 1) {
    for (let i = 0; i < points.length; i += 1) {
      for (let j = i + 1; j < points.length; j += 1) {
        const left = points[i]
        const right = points[j]
        const dx = right.x - left.x
        const dz = right.z - left.z
        const distance = Math.hypot(dx, dz) || 0.001
        const overlap = minDistance - distance

        if (overlap <= 0) continue

        const angle = distance < 0.002 ? seededRandom(left.seed ^ right.seed)() * Math.PI * 2 : Math.atan2(dz, dx)
        const pushX = Math.cos(angle) * overlap * 0.56
        const pushZ = Math.sin(angle) * overlap * 0.56
        left.x -= pushX
        left.z -= pushZ
        right.x += pushX
        right.z += pushZ
        clampPoint(left, pointLimit)
        clampPoint(right, pointLimit)
      }
    }
  }

  const labelPoints = points.map((point, index) => {
    const rand = seededRandom(point.seed ^ 0x9e3779b9)
    const length = Math.max(Math.hypot(point.x, point.z), 0.001)
    const outwardX = point.x / length
    const outwardZ = point.z / length
    const tangentX = -outwardZ
    const tangentZ = outwardX
    const side = (rand() - 0.5) * 1.35
    const labelOut = 1.08 + rand() * 0.52 + (point.team.rank <= 3 ? 0.22 : 0)

    return {
      x: point.x + outwardX * labelOut + tangentX * side,
      z: point.z + outwardZ * labelOut + tangentZ * side,
      lift: (index % 4) * 0.2 + (point.team.rank <= 3 ? 0.28 : 0),
    }
  })

  for (let iteration = 0; iteration < 7; iteration += 1) {
    for (let i = 0; i < labelPoints.length; i += 1) {
      for (let j = i + 1; j < labelPoints.length; j += 1) {
        const left = labelPoints[i]
        const right = labelPoints[j]
        const dx = right.x - left.x
        const dz = right.z - left.z
        const distance = Math.hypot(dx, dz) || 0.001
        const overlap = labelMinDistance - distance

        if (overlap <= 0) continue

        const angle = distance < 0.002 ? (i + j) * 1.618 : Math.atan2(dz, dx)
        const pushX = Math.cos(angle) * overlap * 0.42
        const pushZ = Math.sin(angle) * overlap * 0.42
        left.x -= pushX
        left.z -= pushZ
        right.x += pushX
        right.z += pushZ
        left.lift += 0.018
        right.lift += 0.018
        clampPoint(left, pointLimit + 2.2)
        clampPoint(right, pointLimit + 2.2)
      }
    }
  }

  return points.map((point, index) => {
    const { team } = point

    const scoreRatio = Math.pow(clamp(team.score / maxScore, 0, 1), 0.72)
    const podiumBoost = team.rank <= 3 ? 0.85 - team.rank * 0.12 : 0
    const labelPoint = labelPoints[index]

    return {
      id: team.id,
      team,
      x: point.x,
      z: point.z,
      labelX: labelPoint.x - point.x,
      labelZ: labelPoint.z - point.z,
      labelLift: labelPoint.lift,
      height: 0.55 + scoreRatio * 7.6 + podiumBoost,
      isPodium: team.rank <= 3,
    }
  })
}

const resolveCameraTarget = (layouts: TeamLayout[], aspect: number): CameraTarget => {
  if (layouts.length === 0) return { x: 0, zFocus: 0, y: 9, z: 19, lookAtY: 1.8 }

  const highest = layouts.reduce((current, item) => (item.height > current.height ? item : current), layouts[0])
  let minX = Infinity
  let maxX = -Infinity
  let minZ = Infinity
  let maxZ = -Infinity
  let maxHeight = 0

  for (const layout of layouts) {
    const labelWorldX = layout.x + layout.labelX
    const labelWorldZ = layout.z + layout.labelZ
    minX = Math.min(minX, layout.x - 0.95, labelWorldX - 1.35)
    maxX = Math.max(maxX, layout.x + 0.95, labelWorldX + 1.35)
    minZ = Math.min(minZ, layout.z - 0.95, labelWorldZ - 0.75)
    maxZ = Math.max(maxZ, layout.z + 0.95, labelWorldZ + 0.75)
    maxHeight = Math.max(maxHeight, layout.height)
  }

  const width = Math.max(4.8, maxX - minX)
  const depth = Math.max(4.8, maxZ - minZ)
  const spread = Math.max(width / Math.max(aspect, 0.75), depth)
  const focusX = clamp(highest.x * 0.42, -3.2, 3.2)
  const focusZ = clamp(highest.z * 0.28, -2.4, 2.4)

  return {
    x: focusX,
    zFocus: focusZ,
    y: clamp(7.4 + spread * 0.22 + maxHeight * 0.42, 9.2, 17.8),
    z: clamp(11.6 + spread * 1.18 + maxHeight * 0.62, 17, 38),
    lookAtY: clamp(1.7 + maxHeight * 0.25, 2.1, 5.2),
  }
}

const makeLabelTexture = (team: Team, isPodium: boolean) => {
  const canvas = document.createElement('canvas')
  canvas.width = 768
  canvas.height = 232
  const ctx = canvas.getContext('2d')
  if (!ctx) return new THREE.CanvasTexture(canvas)

  ctx.clearRect(0, 0, canvas.width, canvas.height)
  const accent = isPodium ? '#f1ce79' : '#d7dde0'
  const sub = isPodium ? 'rgba(255, 230, 164, 0.9)' : 'rgba(232, 243, 245, 0.82)'

  const gradient = ctx.createLinearGradient(0, 0, canvas.width, canvas.height)
  gradient.addColorStop(0, isPodium ? 'rgba(116, 80, 20, 0.68)' : 'rgba(48, 58, 62, 0.72)')
  gradient.addColorStop(1, 'rgba(8, 10, 11, 0.84)')
  ctx.fillStyle = gradient
  ctx.strokeStyle = isPodium ? 'rgba(247, 207, 104, 0.92)' : 'rgba(229, 238, 240, 0.6)'
  ctx.lineWidth = 4
  ctx.beginPath()
  ctx.roundRect(20, 24, 728, 164, 24)
  ctx.fill()
  ctx.stroke()

  ctx.fillStyle = accent
  ctx.font = '900 60px Orbitron, Share Tech Mono, Microsoft YaHei, sans-serif'
  ctx.textAlign = 'center'
  ctx.textBaseline = 'middle'
  const displayName = team.name.length > 16 ? `${team.name.slice(0, 15)}...` : team.name
  ctx.fillText(displayName, 384, 84)

  ctx.fillStyle = sub
  ctx.font = '900 42px Share Tech Mono, Microsoft YaHei, sans-serif'
  ctx.fillText(`#${team.rank.toString().padStart(2, '0')}   ${team.score.toLocaleString('zh-CN')} 分`, 384, 142)

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
  aspect: number,
  envMap?: THREE.CubeTexture
) => {
  const layouts = makeLayouts(teams)
  const seen = new Set<number>()
  targetCamera.current = resolveCameraTarget(layouts, aspect)

  for (const layout of layouts) {
    seen.add(layout.id)
    const scoreBucket = Math.round(layout.team.score / 50)
    const labelKey = `${layout.team.rank}-${layout.team.name}-${scoreBucket}-${layout.isPodium}`
    const targetColor = TARGET_COLOR.copy(layout.isPodium ? GOLD : SILVER)
    const targetEmissive = TARGET_EMISSIVE.copy(layout.isPodium ? GOLD_EMISSIVE : SILVER_EMISSIVE)
    let record = records.get(layout.id)

    if (!record) {
      const group = new THREE.Group()
      group.position.set(layout.x, 0, layout.z)

      const material = new THREE.MeshStandardMaterial({
        color: DIM_SILVER.clone(),
        emissive: new THREE.Color(0x090b0c),
        envMap,
        envMapIntensity: layout.isPodium ? 1.45 : 1.15,
        metalness: 0.96,
        roughness: 0.16,
        transparent: true,
        opacity: 1,
      })
      const mesh = new THREE.Mesh(BAR_GEOMETRY, material)
      mesh.userData.teamId = layout.id
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
        opacity: 1,
        depthTest: false,
        depthWrite: false,
      })
      const label = new THREE.Sprite(labelMaterial)
      label.renderOrder = 20
      label.scale.set(3.42, 1.03, 1)
      label.position.set(layout.labelX, 1.2 + layout.labelLift, layout.labelZ)

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
        targetColor: targetColor.clone(),
        targetEmissive: targetEmissive.clone(),
        targetLabelPosition: new THREE.Vector3(layout.labelX, layout.height + 0.72 + layout.labelLift, layout.labelZ),
        targetLabelScale: new THREE.Vector3(layout.isPodium ? 3.92 : 3.42, layout.isPodium ? 1.18 : 1.03, 1),
        lastLabelKey: labelKey,
      }
      records.set(layout.id, record)
    }

    record.targetPosition.set(layout.x, 0, layout.z)
    record.targetHeight = layout.height
    record.targetColor.copy(targetColor)
    record.targetEmissive.copy(targetEmissive)
    record.targetLabelPosition.set(layout.labelX, layout.height + 0.72 + layout.labelLift, layout.labelZ)
    record.targetLabelScale.set(layout.isPodium ? 3.92 : 3.42, layout.isPodium ? 1.18 : 1.03, 1)
    record.mesh.material.envMapIntensity = layout.isPodium ? 1.68 : 1.26
    record.mesh.material.roughness = layout.isPodium ? 0.12 : 0.15
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

export const MetalScoreCity: FC<MetalScoreCityProps> = ({ teams, selectedTeamId = null, onSelectTeam }) => {
  const hostRef = useRef<HTMLDivElement | null>(null)
  const teamsRef = useRef<Team[]>(teams)
  const selectedTeamIdRef = useRef<number | null>(selectedTeamId)
  const hoveredTeamIdRef = useRef<number | null>(null)
  const onSelectTeamRef = useRef(onSelectTeam)
  const rendererRef = useRef<THREE.WebGLRenderer | null>(null)
  const cameraRef = useRef<THREE.PerspectiveCamera | null>(null)
  const cityRef = useRef<THREE.Group | null>(null)
  const barsRef = useRef<THREE.Group | null>(null)
  const recordsRef = useRef(new Map<number, BarRecord>())
  const beamsRef = useRef<BeamRecord[]>([])
  const animationRef = useRef<number | null>(null)
  const mouseRef = useRef({ x: 0, y: 0 })
  const viewRef = useRef({ pitchOffset: 0, zoom: 1 })
  const rotationSpeedRef = useRef(0.08)
  const targetCameraRef = useRef<CameraTarget>({ x: 0, zFocus: 0, y: 9, z: 19, lookAtY: 1.8 })
  const envMapRef = useRef<THREE.CubeTexture | null>(null)

  useEffect(() => {
    selectedTeamIdRef.current = selectedTeamId
  }, [selectedTeamId])

  useEffect(() => {
    onSelectTeamRef.current = onSelectTeam
  }, [onSelectTeam])

  useEffect(() => {
    teamsRef.current = teams
    const bars = barsRef.current
    const camera = cameraRef.current
    if (!bars) return
    syncTeamRecords(teams, bars, recordsRef.current, targetCameraRef, camera?.aspect ?? 1.6, envMapRef.current ?? undefined)
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
    renderer.toneMapping = THREE.ACESFilmicToneMapping
    renderer.toneMappingExposure = 1.16
    renderer.shadowMap.enabled = true
    renderer.shadowMap.type = THREE.PCFSoftShadowMap
    host.appendChild(renderer.domElement)
    rendererRef.current = renderer

    const scene = new THREE.Scene()
    scene.background = new THREE.Color(0x1b1e20)
    scene.fog = new THREE.Fog(0x24282b, 18, 58)
    const envMap = createMetalEnvironment()
    scene.environment = envMap
    envMapRef.current = envMap

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
    const raycaster = new THREE.Raycaster()
    const pointer = new THREE.Vector2()
    const dragState = {
      active: false,
      moved: false,
      pointerId: -1,
      startX: 0,
      startY: 0,
      lastX: 0,
      lastY: 0,
    }

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

    const updatePointer = (event: PointerEvent) => {
      const rect = host.getBoundingClientRect()
      mouseRef.current.x = ((event.clientX - rect.left) / Math.max(rect.width, 1)) * 2 - 1
      mouseRef.current.y = -(((event.clientY - rect.top) / Math.max(rect.height, 1)) * 2 - 1)

      pointer.x = mouseRef.current.x
      pointer.y = mouseRef.current.y
    }

    const pickTeam = () => {
      bars.updateMatrixWorld(true)
      raycaster.setFromCamera(pointer, camera)
      const meshes = [...recordsRef.current.values()].map((record) => record.mesh)
      const hit = raycaster.intersectObjects(meshes, false)[0]
      return (hit?.object.userData.teamId as number | undefined) ?? null
    }

    const onPointerMove = (event: PointerEvent) => {
      updatePointer(event)

      if (dragState.active && event.pointerId === dragState.pointerId) {
        const dx = event.clientX - dragState.lastX
        const dy = event.clientY - dragState.lastY
        const totalMove = Math.hypot(event.clientX - dragState.startX, event.clientY - dragState.startY)

        if (totalMove > 4) dragState.moved = true
        if (dragState.moved) {
          city.rotation.y += dx * 0.007
          viewRef.current.pitchOffset = clamp(viewRef.current.pitchOffset + dy * 0.0024, -0.16, 0.2)
          hoveredTeamIdRef.current = null
        }

        dragState.lastX = event.clientX
        dragState.lastY = event.clientY
        return
      }

      hoveredTeamIdRef.current = pickTeam()
    }

    const onPointerDown = (event: PointerEvent) => {
      if (event.button !== 0) return
      updatePointer(event)
      dragState.active = true
      dragState.moved = false
      dragState.pointerId = event.pointerId
      dragState.startX = event.clientX
      dragState.startY = event.clientY
      dragState.lastX = event.clientX
      dragState.lastY = event.clientY
      host.setPointerCapture?.(event.pointerId)
    }

    const onPointerUp = (event: PointerEvent) => {
      if (!dragState.active || event.pointerId !== dragState.pointerId) return
      updatePointer(event)
      const selectTeam = !dragState.moved ? onSelectTeamRef.current : undefined
      dragState.active = false
      dragState.moved = false
      dragState.pointerId = -1
      host.releasePointerCapture?.(event.pointerId)

      if (!selectTeam) return
      const teamId = pickTeam()
      if (!teamId) return

      const team = teamsRef.current.find((item) => item.id === teamId)
      if (team) selectTeam(team)
    }

    const onPointerCancel = (event: PointerEvent) => {
      if (!dragState.active || event.pointerId !== dragState.pointerId) return
      dragState.active = false
      dragState.moved = false
      dragState.pointerId = -1
      host.releasePointerCapture?.(event.pointerId)
    }

    const onWheel = (event: WheelEvent) => {
      event.preventDefault()
      const delta = clamp(event.deltaY, -180, 180)
      viewRef.current.zoom = clamp(viewRef.current.zoom + delta * 0.0012, 0.68, 1.62)
    }

    const resize = () => {
      const width = host.clientWidth || 1200
      const height = host.clientHeight || 720
      camera.aspect = width / height
      camera.updateProjectionMatrix()
      renderer.setSize(width, height, false)
      syncTeamRecords(teamsRef.current, bars, recordsRef.current, targetCameraRef, camera.aspect, envMap)
    }

    host.addEventListener('pointermove', onPointerMove, { passive: true })
    host.addEventListener('pointerdown', onPointerDown, { passive: true })
    host.addEventListener('pointerup', onPointerUp, { passive: true })
    host.addEventListener('pointercancel', onPointerCancel, { passive: true })
    host.addEventListener('wheel', onWheel, { passive: false })
    const resizeObserver = new ResizeObserver(resize)
    resizeObserver.observe(host)
    resize()

    const clock = new THREE.Clock()
    const animate = () => {
      const delta = Math.min(clock.getDelta(), 0.05)
      const elapsed = clock.elapsedTime
      const mouse = mouseRef.current

      const selectedTeamId = selectedTeamIdRef.current
      const view = viewRef.current
      const targetSpeed = selectedTeamId === null
        ? 0.075 + Math.sin(elapsed * 0.29) * 0.026 + mouse.x * 0.012
        : 0.018 + Math.sin(elapsed * 0.23) * 0.006
      rotationSpeedRef.current = ease(rotationSpeedRef.current, targetSpeed, 0.018)
      city.rotation.y += rotationSpeedRef.current * delta
      const targetTilt = selectedTeamId === null
        ? clamp(0.32 + Math.sin(elapsed * 0.22) * 0.055 + mouse.y * 0.085 + view.pitchOffset, 0.16, 0.62)
        : clamp(0.24 + Math.sin(elapsed * 0.2) * 0.028 + view.pitchOffset * 0.42, 0.18, 0.42)
      city.rotation.x = ease(city.rotation.x, targetTilt, 0.035)

      particles.rotation.y += delta * 0.035
      particles.rotation.x = Math.sin(elapsed * 0.08) * 0.05

      for (const beam of beamsRef.current) {
        beam.mesh.position.z += beam.speed * delta
        if (beam.mesh.position.z > beam.span) beam.mesh.position.z = -beam.span
      }

      rimLight.position.x = Math.sin(elapsed * 0.33) * 13
      rimLight.position.z = Math.cos(elapsed * 0.27) * 13
      rimLight.intensity = 12.5 + Math.sin(elapsed * 0.9) * 1.8
      coolFill.position.x = Math.cos(elapsed * 0.21) * -12
      coolFill.position.z = Math.sin(elapsed * 0.24) * 10

      for (const record of recordsRef.current.values()) {
        const selectedTeamId = selectedTeamIdRef.current
        const isSelected = selectedTeamId === record.mesh.userData.teamId
        const isHovered = hoveredTeamIdRef.current === record.mesh.userData.teamId
        const isDimmed = selectedTeamId !== null && !isSelected
        record.group.position.lerp(record.targetPosition, 0.045)
        const nextHeight = ease(record.mesh.scale.y, record.targetHeight, 0.055)
        record.mesh.scale.y = nextHeight
        record.mesh.position.y = nextHeight / 2
        record.mesh.material.color.lerp(record.targetColor, 0.045)
        const isGold = isGoldColor(record.targetColor)
        const focusEmissive = isGold ? FOCUS_GOLD_EMISSIVE : FOCUS_SILVER_EMISSIVE
        record.mesh.material.emissive.lerp(isSelected ? focusEmissive : record.targetEmissive, 0.045)
        record.mesh.material.opacity = isDimmed ? ease(record.mesh.material.opacity, 0.58, 0.06) : ease(record.mesh.material.opacity, 1, 0.06)
        record.mesh.material.transparent = record.mesh.material.opacity < 0.995
        record.label.position.lerp(record.targetLabelPosition, 0.055)
        record.label.position.y += Math.sin(elapsed * 1.35 + record.group.position.x) * 0.018
        const labelScaleBoost = isSelected ? 1.2 : isDimmed ? 0.92 : 1
        record.label.scale.lerp(
          LABEL_SCALE_TARGET.set(
            record.targetLabelScale.x * labelScaleBoost,
            record.targetLabelScale.y * labelScaleBoost,
            record.targetLabelScale.z
          ),
          0.05
        )
        record.labelMaterial.opacity = isDimmed ? ease(record.labelMaterial.opacity, 0.45, 0.06) : ease(record.labelMaterial.opacity, 1, 0.06)
        const edgePulse = isSelected
          ? 0.72 + Math.sin(elapsed * 3.2) * 0.2
          : isHovered
            ? 0.58 + Math.sin(elapsed * 4.1) * 0.16
            : 0
        const focusEdgeColor = isGold ? FOCUS_GOLD_EDGE : FOCUS_SILVER_EDGE
        record.edgeMaterial.color.lerp(isSelected || isHovered ? focusEdgeColor : record.targetColor, 0.12)
        record.edgeMaterial.opacity = isSelected || isHovered
          ? edgePulse
          : isDimmed
            ? ease(record.edgeMaterial.opacity, 0.16, 0.05)
            : ease(record.edgeMaterial.opacity, isGold ? 0.54 : 0.32, 0.05)
      }

      const selectedRecord = selectedTeamIdRef.current === null ? undefined : recordsRef.current.get(selectedTeamIdRef.current)
      if (selectedRecord) {
        selectedRecord.group.getWorldPosition(FOCUS_CENTER)
        selectedRecord.label.getWorldPosition(FOCUS_LABEL_CENTER)
        const focusX = FOCUS_CENTER.x
        const focusZ = FOCUS_CENTER.z
        const focusHeight = Math.max(1.6, selectedRecord.mesh.scale.y)
        const labelX = FOCUS_LABEL_CENTER.x
        const labelY = FOCUS_LABEL_CENTER.y
        const labelZ = FOCUS_LABEL_CENTER.z
        const orbit = elapsed * 0.26
        const radius = clamp(8.8 + focusHeight * 0.5 + Math.sin(elapsed * 0.17) * 0.55, 9.2, 13.4) * view.zoom
        const cameraX = labelX + Math.sin(orbit) * radius
        const cameraZ = labelZ + Math.cos(orbit) * radius
        const cameraY = labelY + clamp(1.35 + Math.sin(elapsed * 0.21) * 0.52, 0.9, 2.35) * clamp(view.zoom, 0.82, 1.34)

        camera.position.x = ease(camera.position.x, cameraX, 0.035)
        camera.position.y = ease(camera.position.y, cameraY, 0.035)
        camera.position.z = ease(camera.position.z, cameraZ, 0.035)
        FOCUS_CENTER.set(
          ease(focusX, labelX, 0.72),
          labelY - 0.12 + Math.sin(elapsed * 0.18) * 0.2,
          ease(focusZ, labelZ, 0.72)
        )
        camera.lookAt(FOCUS_CENTER)
      } else {
        const dolly = Math.sin(elapsed * 0.18) * 2.1 + Math.sin(elapsed * 0.07) * 1.15
        camera.position.y = ease(camera.position.y, targetCameraRef.current.y * clamp(view.zoom, 0.78, 1.34) + Math.sin(elapsed * 0.12) * 0.8, 0.025)
        camera.position.z = ease(camera.position.z, (targetCameraRef.current.z + dolly) * view.zoom, 0.025)
        camera.position.x = ease(camera.position.x, targetCameraRef.current.x + mouse.x * 0.5, 0.025)
        SCENE_CENTER.set(
          targetCameraRef.current.x * 0.28 + Math.sin(elapsed * 0.16) * 0.42,
          targetCameraRef.current.lookAtY + Math.sin(elapsed * 0.11) * 0.26,
          targetCameraRef.current.zFocus * 0.35
        )
        camera.lookAt(SCENE_CENTER)
      }
      renderer.render(scene, camera)
      animationRef.current = window.requestAnimationFrame(animate)
    }

    syncTeamRecords(teamsRef.current, bars, recordsRef.current, targetCameraRef, camera.aspect, envMap)

    animationRef.current = window.requestAnimationFrame(animate)

    return () => {
      if (animationRef.current) window.cancelAnimationFrame(animationRef.current)
      resizeObserver.disconnect()
      host.removeEventListener('pointermove', onPointerMove)
      host.removeEventListener('pointerdown', onPointerDown)
      host.removeEventListener('pointerup', onPointerUp)
      host.removeEventListener('pointercancel', onPointerCancel)
      host.removeEventListener('wheel', onWheel)
      recordsRef.current.forEach(disposeRecord)
      recordsRef.current.clear()
      beamsRef.current.forEach((beam) => beam.mesh.material.dispose())
      beamsRef.current = []
      beamGeometry.dispose()
      particleGeometry.dispose()
      particleMaterial.dispose()
      envMap.dispose()
      envMapRef.current = null
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
