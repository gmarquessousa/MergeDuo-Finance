import { useRef } from 'react';
import { monthLabel } from '../utils';

interface Props {
  year: number;
  monthIdx: number;
  onPrev: () => void;
  onNext: () => void;
  canGoPrev?: boolean;
  canGoNext?: boolean;
}

export function MonthNavigator({
  year,
  monthIdx,
  onPrev,
  onNext,
  canGoPrev = true,
  canGoNext = true,
}: Props) {
  const startXRef = useRef<number | null>(null);

  function handlePointerDown(e: React.PointerEvent<HTMLDivElement>) {
    startXRef.current = e.clientX;
  }

  function handlePointerUp(e: React.PointerEvent<HTMLDivElement>) {
    if (startXRef.current === null) return;
    const delta = startXRef.current - e.clientX;
    startXRef.current = null;
    if (Math.abs(delta) < 40) return;
    if (delta > 0 && canGoNext) onNext();
    else if (delta < 0 && canGoPrev) onPrev();
  }

  function handlePointerCancel() {
    startXRef.current = null;
  }

  return (
    <div
      className="flex items-center justify-between px-4 py-3 sm:px-5 md:px-8 lg:px-10"
      onPointerDown={handlePointerDown}
      onPointerUp={handlePointerUp}
      onPointerCancel={handlePointerCancel}
    >
      <button
        onClick={onPrev}
        disabled={!canGoPrev}
        aria-label="Mês anterior"
        className="w-10 h-10 rounded-full grid place-items-center text-ink-muted hover:bg-paper-card border border-transparent hover:border-paper-line active:scale-95 transition-all tap-surface disabled:opacity-25 disabled:hover:bg-transparent disabled:hover:border-transparent disabled:active:scale-100"
      >
        <svg aria-hidden="true" width="17" height="17" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2.2" strokeLinecap="round" strokeLinejoin="round"><polyline points="15 18 9 12 15 6"/></svg>
      </button>
      <div className="text-center">
        <div className="text-[10px] uppercase tracking-[0.2em] text-ink-muted font-medium">
          Período
        </div>
        <div className="text-base font-semibold tracking-tight text-ink mt-0.5">
          {monthLabel(year, monthIdx)}
        </div>
      </div>
      <button
        onClick={onNext}
        disabled={!canGoNext}
        aria-label="Próximo mês"
        className="w-10 h-10 rounded-full grid place-items-center text-ink-muted hover:bg-paper-card border border-transparent hover:border-paper-line active:scale-95 transition-all tap-surface disabled:opacity-25 disabled:hover:bg-transparent disabled:hover:border-transparent disabled:active:scale-100"
      >
        <svg aria-hidden="true" width="17" height="17" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2.2" strokeLinecap="round" strokeLinejoin="round"><polyline points="9 18 15 12 9 6"/></svg>
      </button>
    </div>
  );
}
