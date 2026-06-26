import { useEffect, useMemo, useRef, useState } from 'react';

const WEEKDAY_LABELS = ['Dom', 'Seg', 'Ter', 'Qua', 'Qui', 'Sex', 'Sáb'];

const MONTH_LABELS_FULL = [
  'Janeiro', 'Fevereiro', 'Março', 'Abril', 'Maio', 'Junho',
  'Julho', 'Agosto', 'Setembro', 'Outubro', 'Novembro', 'Dezembro',
];

export type DatePickerProps = {
  value: string;
  onChange: (value: string) => void;
  placeholder?: string;
  size?: 'md' | 'sm';
  invalid?: boolean;
  disabled?: boolean;
  ariaLabel?: string;
};

function parseDate(value: string): { year: number; month: number; day: number } | null {
  const match = /^(\d{4})-(\d{2})-(\d{2})$/.exec(value);
  if (!match) return null;
  const year = Number(match[1]);
  const month = Number(match[2]);
  const day = Number(match[3]);
  if (month < 1 || month > 12 || day < 1 || day > 31) return null;
  return { year, month, day };
}

function formatIso(year: number, month: number, day: number): string {
  return `${year}-${String(month).padStart(2, '0')}-${String(day).padStart(2, '0')}`;
}

function formatTriggerLabel(value: string): string {
  const parsed = parseDate(value);
  if (!parsed) return '';
  const dt = new Date(parsed.year, parsed.month - 1, parsed.day);
  return dt.toLocaleDateString('pt-BR', {
    weekday: 'short',
    day: '2-digit',
    month: 'long',
    year: 'numeric',
  });
}

function daysInMonth(year: number, month: number): number {
  return new Date(year, month, 0).getDate();
}

function firstWeekday(year: number, month: number): number {
  return new Date(year, month - 1, 1).getDay();
}

export function DatePicker({
  value,
  onChange,
  placeholder = 'Selecionar data',
  size = 'md',
  invalid = false,
  disabled = false,
  ariaLabel,
}: DatePickerProps) {
  const [open, setOpen] = useState(false);
  const containerRef = useRef<HTMLDivElement>(null);
  const popoverRef = useRef<HTMLDivElement>(null);

  const parsedValue = useMemo(() => parseDate(value), [value]);
  const today = useMemo(() => {
    const now = new Date();
    return { year: now.getFullYear(), month: now.getMonth() + 1, day: now.getDate() };
  }, []);

  const [viewYear, setViewYear] = useState<number>(parsedValue?.year ?? today.year);
  const [viewMonth, setViewMonth] = useState<number>(parsedValue?.month ?? today.month);

  useEffect(() => {
    if (open) return undefined;
    const timeout = window.setTimeout(() => {
      setViewYear(parsedValue?.year ?? today.year);
      setViewMonth(parsedValue?.month ?? today.month);
    }, 0);
    return () => window.clearTimeout(timeout);
  }, [open, parsedValue?.month, parsedValue?.year, today.month, today.year]);

  useEffect(() => {
    if (!open) return;

    function handlePointer(e: MouseEvent) {
      const target = e.target as Node;
      if (
        containerRef.current && !containerRef.current.contains(target) &&
        popoverRef.current && !popoverRef.current.contains(target)
      ) {
        setOpen(false);
      }
    }
    function handleKey(e: KeyboardEvent) {
      if (e.key === 'Escape') setOpen(false);
    }

    document.addEventListener('mousedown', handlePointer);
    document.addEventListener('keydown', handleKey);
    return () => {
      document.removeEventListener('mousedown', handlePointer);
      document.removeEventListener('keydown', handleKey);
    };
  }, [open]);

  const heightClass = size === 'sm' ? 'h-10' : 'h-11';
  const triggerLabel = parsedValue ? formatTriggerLabel(value) : placeholder;
  const hasValue = !!parsedValue;

  function navigateMonth(delta: number) {
    let m = viewMonth + delta;
    let y = viewYear;
    if (m > 12) { m = 1; y += 1; }
    if (m < 1) { m = 12; y -= 1; }
    setViewMonth(m);
    setViewYear(y);
  }

  function selectDay(day: number, offsetMonth = 0) {
    let m = viewMonth + offsetMonth;
    let y = viewYear;
    if (m > 12) { m = 1; y += 1; }
    if (m < 1) { m = 12; y -= 1; }
    onChange(formatIso(y, m, day));
    setOpen(false);
  }

  function goToToday() {
    onChange(formatIso(today.year, today.month, today.day));
    setViewYear(today.year);
    setViewMonth(today.month);
    setOpen(false);
  }

  const totalDays = daysInMonth(viewYear, viewMonth);
  const startOffset = firstWeekday(viewYear, viewMonth);
  const prevMonthDays = daysInMonth(
    viewMonth === 1 ? viewYear - 1 : viewYear,
    viewMonth === 1 ? 12 : viewMonth - 1,
  );

  const leadingDays = Array.from({ length: startOffset }, (_, i) => ({
    day: prevMonthDays - startOffset + 1 + i,
    offset: -1,
  }));
  const currentDays = Array.from({ length: totalDays }, (_, i) => ({
    day: i + 1,
    offset: 0,
  }));
  const trailingCount = (7 - ((leadingDays.length + currentDays.length) % 7)) % 7;
  const trailingDays = Array.from({ length: trailingCount }, (_, i) => ({
    day: i + 1,
    offset: 1,
  }));

  const cells = [...leadingDays, ...currentDays, ...trailingDays];

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
          <span className={`truncate ${hasValue ? 'text-ink capitalize' : 'text-ink-muted'}`}>
            {triggerLabel}
          </span>
        </span>
        <ChevronIcon className={`w-4 h-4 shrink-0 text-ink-muted transition ${open ? 'rotate-180' : ''}`} />
      </button>

      {open && (
        <div
          ref={popoverRef}
          role="dialog"
          aria-label="Selecionar data"
          className="absolute right-0 z-30 mt-2 w-[17rem] rounded-2xl border border-paper-line bg-paper-card shadow-lg shadow-black/5 p-3 animate-[fadeIn_120ms_ease-out]"
        >
          <div className="flex items-center justify-between mb-3">
            <button
              type="button"
              onClick={() => navigateMonth(-1)}
              className="w-8 h-8 rounded-lg flex items-center justify-center text-ink-muted hover:text-ink hover:bg-ink/5 transition"
              aria-label="Mês anterior"
            >
              <ChevronIcon className="w-4 h-4 rotate-90" />
            </button>
            <div className="text-sm font-semibold text-ink tabular-nums select-none">
              {MONTH_LABELS_FULL[viewMonth - 1]} <span className="text-ink-muted font-normal">{viewYear}</span>
            </div>
            <button
              type="button"
              onClick={() => navigateMonth(1)}
              className="w-8 h-8 rounded-lg flex items-center justify-center text-ink-muted hover:text-ink hover:bg-ink/5 transition"
              aria-label="Próximo mês"
            >
              <ChevronIcon className="w-4 h-4 -rotate-90" />
            </button>
          </div>

          <div className="grid grid-cols-7 mb-1">
            {WEEKDAY_LABELS.map((wd) => (
              <div key={wd} className="h-7 flex items-center justify-center text-[10px] font-medium text-ink-muted/60 select-none">
                {wd}
              </div>
            ))}
          </div>

          <div className="grid grid-cols-7 gap-y-0.5">
            {cells.map(({ day, offset }, idx) => {
              const cellYear = offset === -1
                ? (viewMonth === 1 ? viewYear - 1 : viewYear)
                : offset === 1
                  ? (viewMonth === 12 ? viewYear + 1 : viewYear)
                  : viewYear;
              const cellMonth = offset === -1
                ? (viewMonth === 1 ? 12 : viewMonth - 1)
                : offset === 1
                  ? (viewMonth === 12 ? 1 : viewMonth + 1)
                  : viewMonth;

              const isSelected =
                parsedValue?.year === cellYear &&
                parsedValue.month === cellMonth &&
                parsedValue.day === day;

              const isToday =
                today.year === cellYear &&
                today.month === cellMonth &&
                today.day === day;

              const isOtherMonth = offset !== 0;

              return (
                <button
                  key={idx}
                  type="button"
                  onClick={() => selectDay(day, offset)}
                  className={`h-8 w-full rounded-lg text-xs font-medium transition
                    ${isSelected
                      ? 'bold-surface'
                      : isOtherMonth
                        ? 'text-ink-muted/30 hover:text-ink-muted hover:bg-ink/5'
                        : isToday
                          ? 'text-ink font-semibold ring-1 ring-ink/25 hover:bg-ink/5'
                          : 'text-ink-soft hover:bg-ink/5 hover:text-ink'}`}
                >
                  {day}
                </button>
              );
            })}
          </div>

          <div className="mt-2 pt-2 border-t border-paper-line flex items-center justify-between">
            <button
              type="button"
              onClick={goToToday}
              className="text-[11px] font-medium text-ink-muted hover:text-ink transition"
            >
              Hoje
            </button>
          </div>
        </div>
      )}
    </div>
  );
}

function CalendarIcon({ className = '' }: { className?: string }) {
  return (
    <svg aria-hidden="true" className={className} viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth={1.8} strokeLinecap="round" strokeLinejoin="round">
      <rect x="3" y="5" width="18" height="16" rx="2" />
      <path d="M16 3v4M8 3v4M3 10h18" />
    </svg>
  );
}

function ChevronIcon({ className = '' }: { className?: string }) {
  return (
    <svg aria-hidden="true" className={className} viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth={2} strokeLinecap="round" strokeLinejoin="round">
      <path d="M6 9l6 6 6-6" />
    </svg>
  );
}
