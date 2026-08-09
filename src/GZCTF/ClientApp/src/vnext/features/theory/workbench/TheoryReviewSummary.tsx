import { ListFilter } from 'lucide-react'
import styles from './TheoryExamWorkbench.module.css'
import { TheoryReviewSummary as ReviewSummary } from './theoryReview'

export type TheoryReviewFilter = 'all' | 'review'

export function TheoryReviewSummary({
  questionCount,
  review,
  filter,
  onFilterChange,
}: {
  questionCount: number
  review: ReviewSummary
  filter: TheoryReviewFilter
  onFilterChange: (filter: TheoryReviewFilter) => void
}) {
  return (
    <section aria-labelledby="theory-review-title" className={styles.reviewSummary}>
      <header>
        <div>
          <span>ANSWER REVIEW</span>
          <h2 id="theory-review-title">答卷复盘</h2>
          <p>
            本次共 {questionCount} 题，正确 {review.correctCount} 题，错误 {review.incorrectCount} 题，未作答{' '}
            {review.unansweredCount} 题。
          </p>
        </div>
        <div className={styles.reviewFilters} aria-label="复盘题目范围">
          <button aria-pressed={filter === 'all'} onClick={() => onFilterChange('all')} type="button">
            全部题目
          </button>
          <button
            aria-pressed={filter === 'review'}
            disabled={review.reviewCount === 0}
            onClick={() => onFilterChange('review')}
            type="button"
          >
            <ListFilter size={15} />
            仅看错题 ({review.reviewCount})
          </button>
        </div>
      </header>
      <dl>
        <div>
          <dt>正确</dt>
          <dd>{review.correctCount}</dd>
        </div>
        <div>
          <dt>错误</dt>
          <dd>{review.incorrectCount}</dd>
        </div>
        <div>
          <dt>未作答</dt>
          <dd>{review.unansweredCount}</dd>
        </div>
        <div>
          <dt>正确率</dt>
          <dd>{review.accuracy}%</dd>
        </div>
      </dl>
    </section>
  )
}
