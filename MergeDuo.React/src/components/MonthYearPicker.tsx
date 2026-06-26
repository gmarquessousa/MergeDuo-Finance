import { useEffect, useMemo, useRef, useState } from 'react';

const MONTH_LABELS = [
  'Jan', 'Fev', 'Mar', 'Abr', 'Mai', 'Jun',
  'Jul', 'Ago', 'Set', 'Out', 'Nov', 'Dez',
];

const MONTH_LABELS_FULL = [
  'Janeiro', 'Fevereiro', 'Março', 'Abril', 'Maio', 'Junho',
  'Julho', 'Agosto', 'Setembro', 'Outubro', 'Novembro', 'Dezembro',
];

export type MonthYearPickerProps = {
  value: string;
  onChange: (value: string) => void;
  min?: string;
  max?: string;
  placeholder?: string;
  clearable?: boolean;
  size?: 'md' | 'sm';
  invalid?: boolean;
  disabled?: boolean;
  ariaLabel?: string;
};

function parse(value: string): { year: number; month: number } | null {
  const match = /^(\d{4})-(\d{2})$/.exec(value);
  if (!match) return null;
  const year = Number(match[1]);
  const month = Number(match[2]);
  if (!Number.isInteger(year) || month < 1 || month > 12) return null;
  return { year, month };
}

function format(year: number, month: number): string {
  return `${year}-${String(month).padStart(2, '0')}`;
}

function compare(a: string, b: string): number {
  if (a === b) return 0;
  return a < b ? -1 : 1;
}

function formatLabel(value: string): string {
  const parsed = parse(value);
  if (!parsed) return '';
  return `${MONTH_LABELS_FULL[parsed.month - 1]} de ${parsed.year}`;
}

export function MonthYearPicker({
  value,
  onChange,
  min,
  max,
  placeholder = 'Selecionar mês',
  clearable = false,
  size = 'md',
  invalid = false,
  disabled = false,
  ariaLabel,
}: MonthYearPickerProps) {
  const [open, setOpen] = useState(false);
  const containerRef = useRef<HTMLDivElement>(null);
  const popoverRef = useRef<HTMLDivElement>(null);

  const parsedValue = useMemo(() => parse(value), [value]);
  const today = useMemo(() => {
    const now = new Date();
    return { year: now.getFullYear(), month: now.getMonth() + 1 };
  }, []);

  const [viewYear, setViewYear] = useState<number>(
    parsedValue?.year ?? today.year,
  );

  useEffect(() => {
    if (open) return undefined;
    const timeout = window.setTimeout(() => {
      setViewYear(parsedValue?.year ?? today.year);
    }, 0);
    return () => window.clearTimeout(timeout);
  }, [open, parsedValue?.year, today.year]);

  useEffect(() => {
    if (!open) return;

    function handlePointer(event: MouseEvent) {
      const target = event.target as Node;
      if (
        containerRef.current && !containerRef.current.contains(target) &&
        popoverRef.current && !popoverRef.current.contains(target)
      ) {
        setOpen(false);
      }
    }
    function handleKey(event: KeyboardEvent) {
      if (event.key === 'Escape') setOpen(false);
    }

    document.addEventListener('mousedown', handlePointer);
    document.addEventListener('keydown', handleKey);
    return () => {
      document.removeEventListener('mousedown', handlePointer);
      document.removeEventListener('keydown', handleKey);
    };
  }, [open]);

  const heightClass = size === 'sm' ? 'h-10' : 'h-11';
  const triggerLabel = parsedValue ? formatLabel(value) : placeholder;
  const hasValue = !!parsedValue;

  function isOptionDisabled(year: number, month: number): boolean {
    const candidate = format(year, month);
    if (min && compare(candidate, min) < 0) return true;
    if (max && compare(candidate, max) > 0) return true;
    return false;
  }

  function selectMonth(month: number) {
    const next = format(viewYear, month);
    if (isOptionDisabled(viewYear, month)) return;
    onChange(next);
    setOpen(false);
  }

  function changeYear(delta: number) {
    setViewYear((current) => current + delta);
  }

  return (
    <div ref={containerRef} className="relative">
      <button
        type="button"
        disabled={disabled}
        onClick={() => setOpen((prev) => !prev)}
        aria-label={ariaLabel}
        aria-haspopup="dialog"
        aria-expanded={open}
        className={`group w-full ${heightClass} px-3 rounded-xl bg-paper border text-sm text-left flex items-center justify-between gap-2 transition outline-none
          ${invalid
            ? 'border-accent-neg/60 focus:border-accent-neg'
            : 'border-paper-line hover:border-ink/30 focus:border-ink/50'}
          ${disabled ? 'opacity-50 cursor-not-allowed' : 'cursor-pointer'}
          ${open ? 'border-ink/50' : ''}`}
      >
        <span className="flex items-center gap-2 min-w-0">
          <CalendarIcon className="w-4 h-4 shrink-0 text-ink-muted" />
          <span className={`truncate ${hasValue ? 'text-ink' : 'text-ink-muted'}`}>
            {triggerLabel}
          </span>
        </span>
        <span className="flex items-center gap-1 shrink-0">
          {clearable && hasValue && !disabled && (
            <span
              role="button"
              tabIndex={0}
              aria-label="Limpar mês selecionado"
              onClick={(event) => {
                event.stopPropagation();
                onChange('');
              }}
              onKeyDown={(event) => {
                if (event.key === 'Enter' || event.key === ' ') {
                  event.preventDefault();
                  event.stopPropagation();
                  onChange('');
                }
              }}
              className="p-1 -mr-1 rounded-md text-ink-muted hover:text-ink hover:bg-ink/5 transition"
            >
              <ClearIcon className="w-3.5 h-3.5" />
            </span>
          )}
          <ChevronIcon className={`w-4 h-4 text-ink-muted transition ${open ? 'rotate-180' : ''}`} />
        </span>
      </button>

      {open && (
        <div
          ref={popoverRef}
          role="dialog"
          className="absolute right-0 z-30 mt-2 w-[16rem] rounded-2xl border border-paper-line bg-paper-card shadow-lg shadow-black/5 p-3 animate-[fadeIn_120ms_ease-out]"
        >
          <div className="flex items-center justify-between mb-2">
            <button
              type="button"
              onClick={() => changeYear(-1)}
              className="w-8 h-8 rounded-lg flex items-center justify-center text-ink-muted hover:text-ink hover:bg-ink/5 transition"
              aria-label="Ano anterior"
            >
              <ChevronIcon className="w-4 h-4 rotate-90" />
            </button>
            <div className="text-sm font-semibold text-ink tabular-nums">{viewYear}</div>
            <button
              type="button"
              onClick={() => changeYear(1)}
              className="w-8 h-8 rounded-lg flex items-center justify-center text-ink-muted hover:text-ink hover:bg-ink/5 transition"
              aria-label="Próximo ano"
            >
              <ChevronIcon className="w-4 h-4 -rotate-90" />
            </button>
          </div>

          <div className="grid grid-cols-3 gap-1.5">
            {MONTH_LABELS.map((label, index) => {
              const month = index + 1;
              const isSelected =
                parsedValue?.year === viewYear && parsedValue.month === month;
              const isToday = today.year === viewYear && today.month === month;
              const monthDisabled = isOptionDisabled(viewYear, month);

              return (
                <button
                  key={label}
                  type="button"
                  disabled={monthDisabled}
                  onClick={() => selectMonth(month)}
                  className={`h-9 rounded-lg text-xs font-medium border transition
                    ${isSelected
                      ? 'bold-surface border-transparent'
                      : monthDisabled
                        ? 'bg-paper border-paper-line text-ink-muted/40 cursor-not-allowed'
                        : isToday
                          ? 'bg-paper border-ink/30 text-ink hover:border-ink/60'
                          : 'bg-paper border-paper-line text-ink-soft hover:border-ink/40 hover:text-ink'}`}
                >
                  {label}
                </button>
              );
            })}
          </div>

          <div className="mt-3 pt-2 border-t border-paper-line flex items-center justify-between">
            <button
              type="button"
              onClick={() => {
                if (!isOptionDisabled(today.year, today.month)) {
                  onChange(format(today.year, today.month));
                  setViewYear(today.year);
                  setOpen(false);
                }
              }}
              className="text-[11px] font-medium text-ink-muted hover:text-ink transition"
            >
              Mês atual
            </button>
            {clearable && hasValue && (
              <button
                type="button"
                onClick={() => {
                  onChange('');
                  setOpen(false);
                }}
                className="text-[11px] font-medium text-ink-muted hover:text-accent-neg transition"
              >
                Limpar
              </button>
            )}
          </div>
        </div>
      )}
    </div>
  );
}

function CalendarIcon({ className = '' }: { className?: string }) {
  return (
    <svg className={className} viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth={1.8} strokeLinecap="round" strokeLinejoin="round">
      <rect x="3" y="5" width="18" height="16" rx="2" />
      <path d="M16 3v4M8 3v4M3 10h18" />
    </svg>
  );
}

function ChevronIcon({ className = '' }: { className?: string }) {
  return (
    <svg className={className} viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth={2} strokeLinecap="round" strokeLinejoin="round">
      <path d="M6 9l6 6 6-6" />
    </svg>
  );
}

function ClearIcon({ className = '' }: { className?: string }) {
  return (
    <svg className={className} viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth={2.2} strokeLinecap="round" strokeLinejoin="round">
      <path d="M6 6l12 12M18 6L6 18" />
    </svg>
  );
}
