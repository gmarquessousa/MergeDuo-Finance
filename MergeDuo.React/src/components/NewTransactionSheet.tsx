import { useEffect, useMemo, useRef, useState } from 'react';
import { useFocusTrap } from '../useFocusTrap';
import { CATEGORY_META, type Card, type Transaction, type TransactionCategory } from '../types';
import type { CardsStatus } from '../store';
import { CategoryIcon } from './CategoryIcon';
import { DatePicker } from './DatePicker';
import { MonthYearPicker } from './MonthYearPicker';
import {
  computeInvoiceDueDate,
  formatInvoiceYM,
  invoiceMonthForPurchase,
  synthesizePurchaseDateForInvoice,
} from '../cardInvoice';
import { TagInput } from './TagInput';

interface Props {
  open: boolean;
  date: string;
  cards: Card[];
  cardsStatus: CardsStatus;
  cardsError: string | null;
  onClose: () => void;
  onNavigateToCards: () => void;
  onCreateCard?: (data: { title: string; closingDay: number; dueDay: number }) => Promise<Card> | Card;
  onSubmit: (data: {
    date: string;
    category: TransactionCategory;
    description: string;
    amount: number;
    cardId?: string;
    installments?: number;
    invoiceYearMonth?: string;
    tags?: string[];
    notes?: string | null;
  }) => Promise<void> | void;
  initialCategory?: TransactionCategory;
  initialCardId?: string | null;
  initialInvoiceYearMonth?: string | null;
  title?: string;
  tagSuggestions?: string[];
  transactionSuggestions?: Transaction[];
}

const GROUPS: {
  title: string;
  options: TransactionCategory[];
}[] = [
  { title: 'Entrada', options: ['income'] },
  {
    title: 'Saídas',
    options: ['credit_card', 'loan', 'fixed_expense', 'variable_expense'],
  },
  { title: 'Aporte', options: ['investment'] },
];

function formatCurrencyInput(raw: string): { display: string; value: number } {
  const digits = raw.replace(/\D/g, '');
  if (!digits) return { display: '', value: 0 };
  const numeric = parseInt(digits, 10) / 100;
  const display = numeric.toLocaleString('pt-BR', {
    minimumFractionDigits: 2,
    maximumFractionDigits: 2,
  });
  return { display, value: numeric };
}

function formatCurrencyValue(value: number): string {
  if (!Number.isFinite(value) || value <= 0) return '';
  return value.toLocaleString('pt-BR', {
    minimumFractionDigits: 2,
    maximumFractionDigits: 2,
  });
}

function prettyDate(iso: string): string {
  if (!iso) return '';
  const [y, m, d] = iso.split('-').map(Number);
  const dt = new Date(y, m - 1, d);
  return dt.toLocaleDateString('pt-BR', {
    weekday: 'short',
    day: '2-digit',
    month: 'long',
  });
}

export function NewTransactionSheet({
  open,
  date,
  cards,
  cardsStatus,
  cardsError,
  onClose,
  onNavigateToCards,
  onCreateCard,
  onSubmit,
  initialCategory,
  initialCardId,
  initialInvoiceYearMonth,
  title,
  tagSuggestions = [],
  transactionSuggestions = [],
}: Props) {
  const [txDate, setTxDate] = useState(date);
  const [category, setCategory] = useState<TransactionCategory>(initialCategory ?? 'variable_expense');
  const [description, setDescription] = useState('');
  const [amountStr, setAmountStr] = useState('');
  const [amountValue, setAmountValue] = useState(0);
  const [cardId, setCardId] = useState<string | null>(initialCardId ?? null);
  const [installments, setInstallments] = useState(1);
  const [invoiceOverride, setInvoiceOverride] = useState<string | null>(initialInvoiceYearMonth ?? null);
  const [tags, setTags] = useState<string[]>([]);
  const [notes, setNotes] = useState('');
  const [invoicePickerOpen, setInvoicePickerOpen] = useState(false);
  const [showErrors, setShowErrors] = useState(false);
  const [submittingMode, setSubmittingMode] = useState<'close' | 'new' | null>(null);
  const [submitError, setSubmitError] = useState<string | null>(null);
  const [quickCardOpen, setQuickCardOpen] = useState(false);
  const descriptionRef = useRef<HTMLInputElement>(null);
  const panelRef = useRef<HTMLDivElement>(null);

  useEffect(() => {
    if (open) {
      const reset = window.setTimeout(() => {
        setTxDate(date);
        setCategory(initialCategory ?? 'variable_expense');
        setDescription('');
        setAmountStr('');
        setAmountValue(0);
        setCardId(initialCardId ?? null);
        setInstallments(1);
        setInvoiceOverride(initialInvoiceYearMonth ?? null);
        setTags([]);
        setNotes('');
        setInvoicePickerOpen(false);
        setShowErrors(false);
        setSubmittingMode(null);
        setSubmitError(null);
        setQuickCardOpen(false);
      }, 0);
      const focus = window.setTimeout(() => {
        const activeElement = document.activeElement;
        const hasInteractiveFocus =
          activeElement instanceof HTMLElement &&
          activeElement !== document.body &&
          activeElement !== document.documentElement;

        if (!hasInteractiveFocus) {
          descriptionRef.current?.focus();
        }
      }, 220);
      return () => {
        window.clearTimeout(reset);
        window.clearTimeout(focus);
      };
    }
  }, [date, initialCardId, initialCategory, initialInvoiceYearMonth, open]);

  useEffect(() => {
    if (!open) return;
    const prev = document.body.style.overflow;
    document.body.style.overflow = 'hidden';
    return () => {
      document.body.style.overflow = prev;
    };
  }, [open]);

  useEffect(() => {
    if (open && category === 'credit_card' && cardsStatus === 'ready' && cards.length === 1 && !cardId) {
      const timeout = window.setTimeout(() => setCardId(cards[0].id), 0);
      return () => window.clearTimeout(timeout);
    }
  }, [open, category, cards, cardsStatus, cardId]);

  const isCreditCard = category === 'credit_card';
  const cardsLoading = cardsStatus === 'idle' || cardsStatus === 'loading';
  const cardsUnavailable = cardsStatus === 'error';
  const needsCardRegistration = isCreditCard && cardsStatus === 'ready' && cards.length === 0;
  const cardMissing = isCreditCard && cardsStatus === 'ready' && cards.length > 0 && !cardId;
  const cardsBlocked = isCreditCard && (cardsLoading || cardsUnavailable);

  const descriptionValid = description.trim().length > 0;
  const amountValid = amountValue > 0;
  const dateValid = /^\d{4}-\d{2}-\d{2}$/.test(txDate);
  const submitting = submittingMode !== null;

  const canSubmit =
    descriptionValid &&
    amountValid &&
    dateValid &&
    !needsCardRegistration &&
    !cardMissing &&
    !cardsBlocked &&
    !submitting;

  const selectedCard = useMemo(
    () => (cardId ? cards.find((c) => c.id === cardId) ?? null : null),
    [cardId, cards],
  );

  const suggestedInvoiceYM = useMemo(() => {
    if (!selectedCard || !txDate) return null;
    return invoiceMonthForPurchase(txDate, selectedCard);
  }, [selectedCard, txDate]);

  useEffect(() => {
    const timeout = window.setTimeout(() => {
      setInvoiceOverride(null);
      setInvoicePickerOpen(false);
    }, 0);
    return () => window.clearTimeout(timeout);
  }, [cardId, txDate, category]);

  const activeInvoiceYM = invoiceOverride ?? suggestedInvoiceYM;

  const activeInvoiceDueDate = useMemo(() => {
    if (!activeInvoiceYM || !selectedCard) return null;
    const synth = synthesizePurchaseDateForInvoice(activeInvoiceYM, selectedCard);
    return computeInvoiceDueDate(synth, selectedCard);
  }, [activeInvoiceYM, selectedCard]);

  const quickSuggestions = useMemo(
    () => buildQuickSuggestions(transactionSuggestions, category, description),
    [category, description, transactionSuggestions],
  );

  useFocusTrap(panelRef, open);

  if (!open) return null;

  async function submit(mode: 'close' | 'new') {
    if (!canSubmit) {
      setShowErrors(true);
      return;
    }
    setSubmitError(null);
    setSubmittingMode(mode);
    try {
      await onSubmit({
        date: txDate,
        category,
        description: description.trim(),
        amount: amountValue,
        cardId: isCreditCard && cardId ? cardId : undefined,
        installments: isCreditCard && cardId ? installments : undefined,
        invoiceYearMonth: isCreditCard && invoiceOverride ? invoiceOverride : undefined,
        tags,
        notes: notes.trim() ? notes.trim() : null,
      });

      if (mode === 'new') {
        setDescription('');
        setAmountStr('');
        setAmountValue(0);
        setTags([]);
        setNotes('');
        setShowErrors(false);
        window.setTimeout(() => descriptionRef.current?.focus(), 0);
        return;
      }

      onClose();
    } catch (err) {
      setSubmitError(err instanceof Error ? err.message : 'Não foi possível salvar o lançamento.');
    } finally {
      setSubmittingMode(null);
    }
  }

  function handleAmountChange(raw: string) {
    const { display, value } = formatCurrencyInput(raw);
    setAmountStr(display);
    setAmountValue(value);
  }

  function applySuggestion(suggestion: QuickSuggestion) {
    setCategory(suggestion.category);
    setDescription(suggestion.description);
    setAmountValue(suggestion.amount);
    setAmountStr(formatCurrencyValue(suggestion.amount));
    setTags(suggestion.tags);
    setNotes('');
    setCardId(suggestion.category === 'credit_card' ? suggestion.cardId ?? cardId : null);
    window.setTimeout(() => descriptionRef.current?.focus(), 0);
  }

  function handleNavigateToCards() {
    onClose();
    onNavigateToCards();
  }

  return (
    <div
      className="fixed inset-0 z-50 flex items-end justify-center"
      role="dialog"
      aria-modal="true"
      aria-labelledby="new-tx-title"
    >
      <div
        className="absolute inset-0 sheet-backdrop"
        onClick={onClose}
      />
      <div ref={panelRef} className="relative w-full max-w-md bg-paper-card rounded-t-[28px] shadow-hero animate-sheet-up flex flex-col max-h-[92vh]">
        <div className="pt-2.5 px-5 shrink-0">
          <div className="w-9 h-1.5 bg-paper-line rounded-full mx-auto mb-3" />
          <div className="flex items-start justify-between gap-3 pb-3">
            <div>
              <h2 id="new-tx-title" className="text-base font-semibold text-ink leading-tight">
                {title ?? 'Novo lançamento'}
              </h2>
              {txDate && (
                <p className="text-[11px] text-ink-muted mt-0.5 capitalize">
                  {prettyDate(txDate)}
                </p>
              )}
            </div>
            <button
              onClick={onClose}
              className="w-11 h-11 -mt-2 -mr-2 rounded-full grid place-items-center text-ink-muted hover:bg-paper-line transition"
              aria-label="Fechar"
            >
              <svg width="18" height="18" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round" strokeLinejoin="round"><line x1="18" y1="6" x2="6" y2="18"/><line x1="6" y1="6" x2="18" y2="18"/></svg>
            </button>
          </div>
        </div>

        <div className="px-5 pb-3 space-y-5 overflow-y-auto flex-1">
          <section>
            <SectionLabel>Tipo</SectionLabel>
            <div className="space-y-2.5 mt-2">
              {GROUPS.map((g) => (
                <div key={g.title}>
                  <div className="text-[10px] uppercase tracking-wider text-ink-muted/80 mb-1.5">
                    {g.title}
                  </div>
                  <div className="flex flex-wrap gap-1.5">
                    {g.options.map((opt) => {
                      const meta = CATEGORY_META[opt];
                      const selected = category === opt;
                      return (
                        <button
                          key={opt}
                          type="button"
                          onClick={() => {
                            setCategory(opt);
                            if (opt !== 'credit_card') setCardId(null);
                          }}
                          className={`px-3.5 h-10 rounded-full text-[13px] font-medium border transition-all inline-flex items-center gap-1.5 active:scale-[0.95] tap-surface ${
                            selected
                              ? 'bold-surface border-transparent shadow-elevated scale-[1.02]'
                              : 'bg-paper border-paper-line text-ink hover:border-ink/40'
                          }`}
                        >
                          <span className={selected ? 'text-white' : meta.color}>
                            <CategoryIcon category={opt} size={14} />
                          </span>
                          {meta.label}
                        </button>
                      );
                    })}
                  </div>
                </div>
              ))}
            </div>
          </section>

          <section>
            <SectionLabel>{isCreditCard ? 'Data da compra' : 'Data'}</SectionLabel>
            <div className="mt-1.5">
              <DatePicker
                value={txDate}
                onChange={setTxDate}
                invalid={showErrors && !dateValid}
                ariaLabel={isCreditCard ? 'Data da compra' : 'Data do lançamento'}
              />
            </div>
            {showErrors && !dateValid && (
              <p role="alert" className="mt-1 text-[11px] text-accent-neg">Informe uma data válida.</p>
            )}
          </section>

          {quickSuggestions.length > 0 && (
            <section>
              <SectionLabel>Sugestões rápidas</SectionLabel>
              <div className="mt-2 flex gap-2 overflow-x-auto pb-1">
                {quickSuggestions.map((suggestion) => (
                  <button
                    key={suggestion.key}
                    type="button"
                    onClick={() => applySuggestion(suggestion)}
                    className="shrink-0 max-w-[12rem] rounded-2xl border border-paper-line bg-paper px-3 py-2 text-left hover:border-ink/30 transition"
                  >
                    <div className="truncate text-[12px] font-semibold text-ink">{suggestion.description}</div>
                    <div className="mt-0.5 text-[11px] text-ink-muted">
                      {CATEGORY_META[suggestion.category].label} · {formatCurrencyValue(suggestion.amount)}
                    </div>
                  </button>
                ))}
              </div>
            </section>
          )}

          {isCreditCard && (
            <section className="animate-[fadeIn_180ms_ease]">
              <div className="flex items-center justify-between mb-2">
                <SectionLabel>Cartão</SectionLabel>
                {cardsStatus === 'ready' && cards.length > 0 && (
                  <span className="text-[10px] text-ink-muted">
                    {cards.length} cadastrado{cards.length > 1 ? 's' : ''}
                  </span>
                )}
              </div>

              {cardsLoading ? (
                <CardsStatusCallout message="Carregando cartões..." />
              ) : cardsUnavailable ? (
                <CardsStatusCallout message={cardsError ?? 'Não foi possível carregar os cartões.'} />
              ) : needsCardRegistration ? (
                quickCardOpen || onCreateCard ? (
                  <QuickCardForm
                    onCreate={onCreateCard}
                    onCreated={(card) => {
                      setCardId(card.id);
                      setQuickCardOpen(false);
                    }}
                    onNavigate={handleNavigateToCards}
                  />
                ) : (
                  <EmptyCardsCallout onNavigate={handleNavigateToCards} />
                )
              ) : (
                <>
                  <div className="grid grid-cols-2 gap-2">
                    {cards.map((card) => {
                      const selected = cardId === card.id;
                      return (
                        <button
                          key={card.id}
                          type="button"
                          onClick={() => setCardId(card.id)}
                          aria-pressed={selected}
                          className={`relative text-left p-3 rounded-2xl border transition active:scale-[0.98] ${
                            selected
                              ? 'border-ink bg-ink/[0.04] shadow-soft-sm'
                              : 'border-paper-line bg-paper hover:border-ink/30'
                          }`}
                        >
                          <div className="flex items-start gap-2">
                            <span
                              className={`grid place-items-center w-8 h-8 rounded-lg shrink-0 ${
                                selected ? 'bg-ink text-white' : 'bg-paper-line text-ink-muted'
                              }`}
                            >
                              <svg width="14" height="14" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round" strokeLinejoin="round"><rect x="2" y="5" width="20" height="14" rx="2"/><line x1="2" y1="10" x2="22" y2="10"/></svg>
                            </span>
                            <div className="min-w-0 flex-1">
                              <div className="text-[13px] font-medium text-ink truncate">
                                {card.title}
                              </div>
                              <div className="text-[10px] text-ink-muted mt-0.5">
                                Vence dia {card.dueDay}
                              </div>
                            </div>
                            {selected && (
                              <span className="absolute top-2 right-2 w-4 h-4 rounded-full bg-ink text-white grid place-items-center">
                                <svg width="10" height="10" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="3" strokeLinecap="round" strokeLinejoin="round"><polyline points="20 6 9 17 4 12"/></svg>
                              </span>
                            )}
                          </div>
                        </button>
                      );
                    })}
                    <button
                      type="button"
                      onClick={() => onCreateCard ? setQuickCardOpen((value) => !value) : handleNavigateToCards()}
                      className="text-left p-3 rounded-2xl border border-dashed border-paper-line text-ink-muted hover:border-ink/40 hover:text-ink transition active:scale-[0.98] flex items-center gap-2"
                    >
                      <span className="grid place-items-center w-8 h-8 rounded-lg bg-paper-line shrink-0">
                        <svg width="14" height="14" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round" strokeLinejoin="round"><line x1="12" y1="5" x2="12" y2="19"/><line x1="5" y1="12" x2="19" y2="12"/></svg>
                      </span>
                      <div className="min-w-0">
                        <div className="text-[13px] font-medium leading-tight">Novo cartão</div>
                        <div className="text-[10px] mt-0.5">Cadastrar agora</div>
                      </div>
                    </button>
                  </div>

                  {quickCardOpen && onCreateCard && (
                    <div className="mt-3">
                      <QuickCardForm
                        onCreate={onCreateCard}
                        onCreated={(card) => {
                          setCardId(card.id);
                          setQuickCardOpen(false);
                        }}
                        onNavigate={handleNavigateToCards}
                      />
                    </div>
                  )}

                  {selectedCard && suggestedInvoiceYM && (
                    <InvoicePicker
                      activeInvoiceYM={activeInvoiceYM!}
                      invoiceOverride={invoiceOverride}
                      invoiceDueDate={activeInvoiceDueDate}
                      pickerOpen={invoicePickerOpen}
                      installments={installments}
                      onPickerOpen={() => setInvoicePickerOpen(true)}
                      onPickerClose={() => setInvoicePickerOpen(false)}
                      onInvoiceChange={(ym) => {
                        setInvoiceOverride(ym === suggestedInvoiceYM ? null : ym);
                        setInvoicePickerOpen(false);
                      }}
                      onRevert={() => setInvoiceOverride(null)}
                    />
                  )}

                  {selectedCard && (
                    <div className="mt-3">
                      <div className="flex items-center justify-between mb-1.5">
                        <SectionLabel>Parcelas</SectionLabel>
                        <span className="text-[11px] text-ink-muted">
                          {installments === 1
                            ? 'À vista'
                            : `${installments}x de ${(amountValue / installments).toLocaleString('pt-BR', { style: 'currency', currency: 'BRL' })}`}
                        </span>
                      </div>
                      <div className="rounded-xl bg-paper border border-paper-line p-2 flex items-center gap-2">
                        <div className="flex gap-1">
                          {[1, 2, 3, 4, 6, 12].map((n) => {
                            const selected = installments === n;
                            return (
                              <button
                                key={n}
                                type="button"
                                onClick={() => setInstallments(n)}
                                className={`h-9 min-w-[2.25rem] px-1.5 rounded-lg text-xs font-medium transition ${
                                  selected
                                    ? 'bold-surface'
                                    : 'text-ink-muted hover:bg-paper-line hover:text-ink'
                                }`}
                              >
                                {n}x
                              </button>
                            );
                          })}
                        </div>
                        <div className="h-5 w-px bg-paper-line shrink-0" />
                        <input
                          type="number"
                          inputMode="numeric"
                          min={1}
                          max={999}
                          value={installments}
                          onChange={(e) => {
                            const v = Math.max(1, Math.min(999, parseInt(e.target.value, 10) || 1));
                            setInstallments(v);
                          }}
                          className="w-14 h-8 rounded-lg border border-paper-line bg-paper-card text-center text-xs font-medium text-ink focus:outline-none focus:border-ink/30"
                          aria-label="Número de parcelas"
                        />
                      </div>
                    </div>
                  )}

                  {showErrors && cardMissing && (
                    <p role="alert" className="mt-2 text-[11px] text-accent-neg">
                      Selecione um cartão para continuar.
                    </p>
                  )}
                </>
              )}
            </section>
          )}

          <section>
            <SectionLabel htmlFor="tx-description">Descrição</SectionLabel>
            <input
              id="tx-description"
              ref={descriptionRef}
              value={description}
              onChange={(e) => setDescription(e.target.value)}
              placeholder="Ex.: Mercado, Salário, Uber"
              className={`mt-1.5 w-full h-12 px-3.5 rounded-2xl bg-paper border text-[15px] text-ink outline-none transition-colors placeholder:text-ink-muted/60 focus:border-accent-invest/60 focus:bg-accent-invest/[0.03] ${
                showErrors && !descriptionValid
                  ? 'border-accent-neg/60'
                  : 'border-paper-line'
              }`}
            />
            {showErrors && !descriptionValid && (
              <p role="alert" className="mt-1 text-[11px] text-accent-neg">Informe uma descrição.</p>
            )}
          </section>

          <section>
            <TagInput tags={tags} onChange={setTags} suggestions={tagSuggestions} />
          </section>

          <section>
            <SectionLabel htmlFor="tx-notes">Observações</SectionLabel>
            <textarea
              id="tx-notes"
              value={notes}
              onChange={(e) => setNotes(e.target.value)}
              maxLength={1000}
              placeholder="Opcional"
              className="mt-1.5 w-full min-h-[4.5rem] resize-y rounded-2xl bg-paper border border-paper-line px-3.5 py-2.5 text-[15px] text-ink outline-none transition-colors placeholder:text-ink-muted/60 focus:border-accent-invest/60 focus:bg-accent-invest/[0.03]"
            />
          </section>

          <section>
            <SectionLabel htmlFor="tx-amount">Valor</SectionLabel>
            <div
              className={`mt-1.5 flex items-baseline justify-center gap-1.5 h-20 px-4 rounded-2xl border transition-colors ${
                showErrors && !amountValid
                  ? 'border-accent-neg/60 bg-accent-neg/[0.03]'
                  : 'border-paper-line bg-paper focus-within:border-accent-invest/60 focus-within:bg-accent-invest/[0.03]'
              }`}
            >
              <span className="text-ink-muted text-2xl font-medium self-center">R$</span>
              <input
                id="tx-amount"
                inputMode="numeric"
                value={amountStr}
                onChange={(e) => handleAmountChange(e.target.value)}
                placeholder="0,00"
                className="w-full max-w-[14rem] bg-transparent text-center text-[40px] leading-none font-bold tracking-tight tabular-nums text-ink outline-none placeholder:text-ink-muted/30 placeholder:font-semibold"
              />
            </div>
            {showErrors && !amountValid && (
              <p role="alert" className="mt-1.5 text-center text-[11px] text-accent-neg">Informe um valor maior que zero.</p>
            )}
          </section>
        </div>

        <div className="px-5 pt-3 pb-6 border-t border-paper-line/70 shrink-0">
          <div className="grid grid-cols-2 gap-2.5">
            <button
              type="button"
              onClick={() => void submit('new')}
              disabled={needsCardRegistration || cardsBlocked || submitting}
              onMouseEnter={() => {
                if (!canSubmit && !needsCardRegistration && !cardsBlocked) setShowErrors(true);
              }}
              className="h-12 rounded-2xl border border-paper-line text-sm font-semibold text-ink disabled:opacity-40 transition active:scale-[0.97] hover:bg-paper-line tap-surface"
            >
              {submittingMode === 'new' ? 'Salvando...' : 'Salvar e novo'}
            </button>
            <button
              type="button"
              onClick={() => void submit('close')}
              disabled={needsCardRegistration || cardsBlocked || submitting}
              onMouseEnter={() => {
                if (!canSubmit && !needsCardRegistration && !cardsBlocked) setShowErrors(true);
              }}
              className="h-12 rounded-2xl bg-accent-invest text-white text-sm font-semibold disabled:opacity-40 transition active:scale-[0.97] shadow-elevated tap-surface"
            >
              {submittingMode === 'close'
                ? 'Salvando...'
                : needsCardRegistration
                ? 'Cadastre um cartão'
                : cardsBlocked
                  ? 'Aguardando cartões'
                  : 'Salvar'}
            </button>
          </div>
          {submitError && (
            <div role="alert" className="mt-3 rounded-xl border border-accent-neg/30 bg-accent-neg/5 px-3 py-2 text-[12px] text-accent-neg">
              {submitError}
            </div>
          )}
        </div>
      </div>
    </div>
  );
}

function SectionLabel({
  children,
  htmlFor,
}: {
  children: React.ReactNode;
  htmlFor?: string;
}) {
  return (
    <label
      htmlFor={htmlFor}
      className="text-[11px] uppercase tracking-wider text-ink-muted font-medium"
    >
      {children}
    </label>
  );
}

function CardsStatusCallout({ message }: { message: string }) {
  return (
    <div className="rounded-2xl border border-dashed border-paper-line p-4 text-[12px] text-ink-muted">
      {message}
    </div>
  );
}

function EmptyCardsCallout({ onNavigate }: { onNavigate: () => void }) {
  return (
    <div className="rounded-2xl border border-dashed border-accent-neg/40 bg-accent-neg/[0.04] p-4">
      <div className="flex items-start gap-3">
        <span className="grid place-items-center w-9 h-9 rounded-xl bg-accent-neg/10 text-accent-neg shrink-0">
          <svg width="18" height="18" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round" strokeLinejoin="round"><rect x="2" y="5" width="20" height="14" rx="2"/><line x1="2" y1="10" x2="22" y2="10"/></svg>
        </span>
        <div className="min-w-0 flex-1">
          <div className="text-sm font-semibold text-ink">Nenhum cartão cadastrado</div>
          <p className="text-[12px] text-ink-muted mt-0.5 leading-snug">
            Cadastre um cartão para registrar compras no crédito.
          </p>
        </div>
      </div>
      <button
        type="button"
        onClick={onNavigate}
        className="mt-3 w-full h-10 rounded-xl bold-surface text-xs font-semibold inline-flex items-center justify-center gap-1.5 active:scale-[0.99] transition"
      >
        <svg width="14" height="14" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round" strokeLinejoin="round"><line x1="12" y1="5" x2="12" y2="19"/><line x1="5" y1="12" x2="19" y2="12"/></svg>
        Cadastrar cartão
      </button>
    </div>
  );
}

function QuickCardForm({
  onCreate,
  onCreated,
  onNavigate,
}: {
  onCreate?: (data: { title: string; closingDay: number; dueDay: number }) => Promise<Card> | Card;
  onCreated: (card: Card) => void;
  onNavigate: () => void;
}) {
  const [title, setTitle] = useState('');
  const [closingDay, setClosingDay] = useState(27);
  const [dueDay, setDueDay] = useState(5);
  const [saving, setSaving] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const canCreate = !!title.trim() && closingDay >= 1 && closingDay <= 31 && dueDay >= 1 && dueDay <= 31 && !saving;

  async function submit() {
    if (!onCreate) {
      onNavigate();
      return;
    }
    if (!canCreate) return;
    setSaving(true);
    setError(null);
    try {
      const card = await onCreate({ title: title.trim(), closingDay, dueDay });
      onCreated(card);
      setTitle('');
    } catch (err) {
      setError(err instanceof Error ? err.message : 'Não foi possível cadastrar o cartão.');
    } finally {
      setSaving(false);
    }
  }

  return (
    <div className="rounded-2xl border border-paper-line bg-paper p-3">
      <div className="text-[11px] uppercase tracking-wider text-ink-muted">Novo cartão</div>
      <div className="mt-2 space-y-3">
        <input
          value={title}
          onChange={(event) => setTitle(event.target.value)}
          placeholder="Nome do cartão"
          className="w-full h-10 rounded-xl border border-paper-line bg-paper-card px-3 text-sm text-ink outline-none focus:border-ink/50"
        />
        <div className="grid grid-cols-2 gap-2">
          <SmallDayInput label="Fecha" value={closingDay} onChange={setClosingDay} />
          <SmallDayInput label="Vence" value={dueDay} onChange={setDueDay} />
        </div>
        <button
          type="button"
          onClick={() => void submit()}
          disabled={!canCreate && !!onCreate}
          className="w-full h-10 rounded-xl bold-surface text-xs font-semibold disabled:opacity-40"
        >
          {saving ? 'Salvando...' : 'Salvar cartão'}
        </button>
        {error && (
          <div role="alert" className="rounded-xl border border-accent-neg/30 bg-accent-neg/5 px-3 py-2 text-[12px] text-accent-neg">
            {error}
          </div>
        )}
      </div>
    </div>
  );
}

function SmallDayInput({
  label,
  value,
  onChange,
}: {
  label: string;
  value: number;
  onChange: (value: number) => void;
}) {
  return (
    <label className="block">
      <span className="text-[10px] uppercase tracking-wider text-ink-muted">{label}</span>
      <input
        type="number"
        inputMode="numeric"
        min={1}
        max={31}
        value={value}
        onChange={(event) => onChange(clampDay(parseInt(event.target.value, 10)))}
        className="mt-1 w-full h-10 rounded-xl border border-paper-line bg-paper-card px-2 text-center text-sm font-semibold text-ink outline-none focus:border-ink/50"
      />
    </label>
  );
}

function clampDay(value: number) {
  if (!Number.isFinite(value)) return 1;
  return Math.max(1, Math.min(31, Math.round(value)));
}

function formatDueLabel(iso: string | null): string {
  if (!iso) return '';
  const [y, m, d] = iso.split('-').map(Number);
  const dt = new Date(y, m - 1, d);
  return dt.toLocaleDateString('pt-BR', { day: '2-digit', month: 'short' }).replace('.', '');
}

function InvoicePicker({
  activeInvoiceYM,
  invoiceOverride,
  invoiceDueDate,
  pickerOpen,
  installments,
  onPickerOpen,
  onPickerClose,
  onInvoiceChange,
  onRevert,
}: {
  activeInvoiceYM: string;
  invoiceOverride: string | null;
  invoiceDueDate: string | null;
  pickerOpen: boolean;
  installments: number;
  onPickerOpen: () => void;
  onPickerClose: () => void;
  onInvoiceChange: (ym: string) => void;
  onRevert: () => void;
}) {
  const isOverridden = !!invoiceOverride;

  return (
    <div className="mt-3 rounded-2xl border border-paper-line bg-paper/60 overflow-visible">
      <div className="flex items-center justify-between px-3 pt-2.5 pb-2">
        <div className="flex items-center gap-2">
          <svg className="w-4 h-4 text-ink-muted shrink-0" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth={1.8} strokeLinecap="round" strokeLinejoin="round">
            <rect x="3" y="5" width="18" height="16" rx="2"/>
            <path d="M16 3v4M8 3v4M3 10h18"/>
          </svg>
          <span className="text-[11px] uppercase tracking-wider text-ink-muted font-medium">Fatura</span>
          {isOverridden && (
            <span className="inline-flex items-center px-2 h-5 rounded-full bg-ink/[0.07] text-[10px] font-medium text-ink">
              Alterada
            </span>
          )}
        </div>
        {isOverridden ? (
          <button
            type="button"
            onClick={onRevert}
            className="text-[11px] text-ink-muted hover:text-ink underline-offset-2 hover:underline transition"
          >
            Usar sugerida
          </button>
        ) : null}
      </div>

      <div className="flex items-center justify-between px-3 pb-2.5 gap-2">
        <div className="min-w-0">
          <div className="text-[13px] font-semibold text-ink truncate">
            {formatInvoiceYM(activeInvoiceYM)}
            {!isOverridden && (
              <span className="ml-1.5 text-[10px] font-normal text-ink-muted">(sugerida)</span>
            )}
          </div>
          {invoiceDueDate && (
            <div className="text-[11px] text-ink-muted mt-0.5">
              Vencimento: <span className="font-medium text-ink">{formatDueLabel(invoiceDueDate)}</span>
            </div>
          )}
          {installments > 1 && (
            <div className="text-[11px] text-ink-muted mt-0.5">
              1ª parcela nesta fatura; as próximas seguem mês a mês.
            </div>
          )}
        </div>
        <button
          type="button"
          onClick={pickerOpen ? onPickerClose : onPickerOpen}
          className={`shrink-0 h-8 px-3 rounded-xl border text-[12px] font-medium transition flex items-center gap-1.5
            ${pickerOpen
              ? 'bg-ink text-white border-transparent'
              : 'bg-paper border-paper-line text-ink-soft hover:border-ink/40 hover:text-ink'
            }`}
        >
          {pickerOpen ? 'Cancelar' : 'Trocar fatura'}
          {!pickerOpen && (
            <svg className="w-3.5 h-3.5 text-ink-muted" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth={2} strokeLinecap="round" strokeLinejoin="round">
              <path d="M6 9l6 6 6-6"/>
            </svg>
          )}
        </button>
      </div>

      {pickerOpen && (
        <div className="border-t border-paper-line px-3 pt-3 pb-3 animate-[fadeIn_120ms_ease-out]">
          <MonthYearPicker
            value={activeInvoiceYM}
            onChange={onInvoiceChange}
            placeholder="Selecionar fatura"
            ariaLabel="Mês de vencimento da fatura"
          />
          <p className="mt-2 text-[10px] text-ink-muted leading-snug">
            Selecione o mês de <strong>vencimento</strong> da fatura onde o gasto deve aparecer.
          </p>
        </div>
      )}
    </div>
  );
}

interface QuickSuggestion {
  key: string;
  description: string;
  amount: number;
  category: TransactionCategory;
  cardId?: string;
  tags: string[];
}

function buildQuickSuggestions(
  transactions: Transaction[],
  category: TransactionCategory,
  description: string,
): QuickSuggestion[] {
  const query = description.trim().toLowerCase();
  const seen = new Set<string>();
  const result: QuickSuggestion[] = [];
  for (const tx of transactions.slice().reverse()) {
    if (tx.projected || tx.aggregateOnly || tx.localOnly || !tx.description.trim()) continue;
    if (query && !tx.description.toLowerCase().includes(query)) continue;
    const key = `${tx.category}:${tx.description.toLowerCase()}:${tx.cardId ?? ''}`;
    if (seen.has(key)) continue;
    if (!query && tx.category !== category && result.length >= 3) continue;
    seen.add(key);
    result.push({
      key,
      description: tx.description,
      amount: tx.amount,
      category: tx.category,
      cardId: tx.cardId,
      tags: tx.tags ?? [],
    });
    if (result.length >= 6) break;
  }
  return result;
}
