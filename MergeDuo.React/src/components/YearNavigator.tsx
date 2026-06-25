interface Props {
  year: number;
  onPrev: () => void;
  onNext: () => void;
  canGoPrev?: boolean;
  canGoNext?: boolean;
}

export function YearNavigator({
  year,
  onPrev,
  onNext,
  canGoPrev = true,
  canGoNext = true,
}: Props) {
  return (
    <div className="flex items-center justify-between px-4 py-4 sm:px-5 md:px-8 lg:px-10">
      <button
        onClick={onPrev}
        disabled={!canGoPrev}
        aria-label="Ano anterior"
        className="w-10 h-10 rounded-full grid place-items-center text-ink-muted hover:bg-paper-line active:scale-95 transition disabled:opacity-30 disabled:hover:bg-transparent disabled:active:scale-100"
      >
        <svg aria-hidden="true" width="18" height="18" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round" strokeLinejoin="round"><polyline points="15 18 9 12 15 6"/></svg>
      </button>
      <div className="text-center">
        <div className="text-[11px] uppercase tracking-[0.18em] text-ink-muted">
          Visão anual
        </div>
        <div className="text-lg font-semibold text-ink">{year}</div>
      </div>
      <button
        onClick={onNext}
        disabled={!canGoNext}
        aria-label="Próximo ano"
        className="w-10 h-10 rounded-full grid place-items-center text-ink-muted hover:bg-paper-line active:scale-95 transition disabled:opacity-30 disabled:hover:bg-transparent disabled:active:scale-100"
      >
        <svg aria-hidden="true" width="18" height="18" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round" strokeLinejoin="round"><polyline points="9 18 15 12 9 6"/></svg>
      </button>
    </div>
  );
}
