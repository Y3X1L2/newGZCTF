import { ExternalLink, PlayCircle } from 'lucide-react'
import { TrainingCourseChapterModel, TrainingCourseVideoProvider } from '@Api'
import { InlineFeedback } from '../../../shared/Interaction'
import { safeResourceHref } from '../../../shared/urls'
import styles from './TrainingChapterPage.module.css'

function isHost(hostname: string, domain: string) {
  return hostname === domain || hostname.endsWith(`.${domain}`)
}

function videoEmbedUrl(url?: string | null) {
  if (!url) return null
  try {
    const parsed = new URL(url, window.location.origin)
    if (/\.(mp4|webm|ogg)(?:$|\?)/i.test(parsed.pathname)) return null
    if (parsed.hostname === 'youtu.be') {
      const id = parsed.pathname.split('/').filter(Boolean)[0]
      return id ? `https://www.youtube-nocookie.com/embed/${id}` : null
    }
    if (isHost(parsed.hostname, 'youtube.com')) {
      const id = parsed.searchParams.get('v')
      return id ? `https://www.youtube-nocookie.com/embed/${id}` : null
    }
    if (parsed.hostname === 'player.bilibili.com') return parsed.toString()
    if (isHost(parsed.hostname, 'bilibili.com')) {
      const bvid = parsed.pathname.match(/\/video\/(BV[\w]+)/i)?.[1]
      return bvid ? `https://player.bilibili.com/player.html?bvid=${bvid}` : null
    }
  } catch {
    return null
  }
  return null
}

function directVideoUrl(chapter: TrainingCourseChapterModel) {
  if (chapter.videoProvider === TrainingCourseVideoProvider.LocalFile) return safeResourceHref(chapter.videoFileUrl)
  if (
    chapter.videoProvider === TrainingCourseVideoProvider.ExternalUrl &&
    /\.(mp4|webm|ogg)(?:$|\?)/i.test(chapter.videoUrl ?? '')
  ) {
    return safeResourceHref(chapter.videoUrl)
  }
  return null
}

export function TrainingChapterVideo({ chapter }: { chapter: TrainingCourseChapterModel }) {
  if (!chapter.videoProvider || chapter.videoProvider === TrainingCourseVideoProvider.None) return null
  const source = directVideoUrl(chapter)
  const embed =
    chapter.videoProvider === TrainingCourseVideoProvider.ExternalUrl ? videoEmbedUrl(chapter.videoUrl) : null
  const externalVideoHref = safeResourceHref(chapter.videoUrl)

  return (
    <section className={styles.videoSection} id="chapter-video">
      <header className={styles.sectionHeader}>
        <div>
          <span>LESSON VIDEO</span>
          <h2>章节视频</h2>
        </div>
        <PlayCircle size={20} />
      </header>
      {source ? (
        <video className={styles.videoPlayer} controls preload="metadata" src={source} />
      ) : embed ? (
        <div className={styles.videoFrame}>
          <iframe
            allow="accelerometer; autoplay; clipboard-write; encrypted-media; gyroscope; picture-in-picture; web-share"
            allowFullScreen
            referrerPolicy="strict-origin-when-cross-origin"
            src={embed}
            title={`${chapter.title || '章节'}视频`}
          />
        </div>
      ) : externalVideoHref ? (
        <a className={styles.externalVideo} href={externalVideoHref} rel="noreferrer noopener" target="_blank">
          <PlayCircle size={22} />
          <span>
            <strong>在新窗口打开章节视频</strong>
            <small>{chapter.videoUrl}</small>
          </span>
          <ExternalLink size={18} />
        </a>
      ) : (
        <InlineFeedback>视频资源尚未就绪，请联系课程教师。</InlineFeedback>
      )}
    </section>
  )
}
