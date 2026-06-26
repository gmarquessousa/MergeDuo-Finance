import { useRef, useState } from 'react';
import { useFocusTrap } from '../useFocusTrap';
import { useFinance } from '../store';
import { CardsApiError, deleteCard, getCard } from '../api/cards';
import { deleteFixedRule, FixedRulesApiError, getFixedRule } from '../api/fixedRules';
import { deleteTransaction, getTransaction, toTransaction, TransactionsApiError } from '../api/transactions';
import { formatBRL } from '../utils';
import type { Card, FixedTransactionRule, Transaction } from '../types';
import { CategoryIcon } from './CategoryIcon';

interface Props {
  card: Card;
  accessToken: string;
  onClose: () => void;
  onDeleted: () => void;
}

function cardsErrorMessage(err: unknown) {
  if (err instanceof CardsApiError) {
    if (err.code === 'card_conflict' || err.code === 'precondition_failed') {
      return 'Este cartão mudou em outro dispositivo. Atualize e tente novamente.';
    }
    return err.message || 'Não foi possível excluir o cartão.';
  }
  return err instanceof Error ? err.message : 'Não foi possível concluir a operação.';
}

export function DeleteCardSheet({ card, accessToken, onClose, onDeleted }: Props) {
  const {
    transactions,
    fixedTransactions,
    currentUser,
    partner,
    removeCard,
    removeTransactionLocal,
    removeFixedRule,
    updateCard,
    updateFixedRule,
    upsertTransactions,
  } = useFinance();

  const linkedRules: FixedTransactionRule[] = fixedTransactions.filter(
    (r) => r.cardId === card.id,
  );
  const linkedTransactions: Transaction[] = transactions.filter(
    (tx) => tx.cardId === card.id,
  );

  const [deleting, setDeleting] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const panelRef = useRef<HTMLDivElement>(null);
  useFocusTrap(panelRef, true);

  async function confirm() {
    if (deleting) return;
    setDeleting(true);
    setError(null);

    try {
      for (const rule of linkedRules) {
        try {
          const latestRule = await fixedRuleForMutation(rule);
          await deleteFixedRule(accessToken, latestRule.id, latestRule.etag);
        } catch (err) {
          if (!(err instanceof FixedRulesApiError && err.code === 'fixed_rule_not_found')) {
            throw err;
          }
        }
        removeFixedRule(rule.id);
      }

      for (const tx of linkedTransactions) {
        const ym = tx.yearMonth ?? tx.date.slice(0, 7);
        try {
          const latestTx = await transactionForMutation(tx);
          await deleteTransaction(accessToken, latestTx.id, ym, latestTx.etag);
        } catch (err) {
          if (!(err instanceof TransactionsApiError && err.code === 'transaction_not_found')) {
            throw err;
          }
        }
        removeTransactionLocal({ id: tx.id, userId: tx.userId, yearMonth: ym });
      }

      try {
        const latestCard = await cardForMutation(card);
        await deleteCard(accessToken, latestCard.id, latestCard.etag);
      } catch (err) {
        if (!(err instanceof CardsApiError && err.code === 'card_not_found')) {
          throw err;
        }
      }
      removeCard(card.id);
      onDeleted();
    } catch (err) {
      setError(cardsErrorMessage(err));
    } finally {
      setDeleting(false);
    }
  }

  async function fixedRuleForMutation(rule: FixedTransactionRule): Promise<FixedTransactionRule> {
    if (rule.etag?.trim()) {
      return rule;
    }

    const fresh = await getFixedRule(accessToken, rule.id);
    updateFixedRule(fresh);
    return fresh;
  }

  async function transactionForMutation(tx: Transaction): Promise<Transaction> {
    if (tx.etag?.trim()) {
      return tx;
    }

    if (!currentUser) {
      throw new Error('Sua sessão ainda não foi carregada.');
    }

    const ym = tx.yearMonth ?? tx.date.slice(0, 7);
    const fresh = await getTransaction(accessToken, tx.id, ym);
    const mapped = toTransaction(fresh, {
      currentUserId: currentUser.id,
      partnerUserId: partner?.partnerUserId,
      partnerName: partner?.name,
    });
    upsertTransactions([mapped]);
    return mapped;
  }

  async function cardForMutation(candidate: Card): Promise<Card> {
    if (candidate.etag?.trim()) {
      return candidate;
    }

    const fresh = await getCard(accessToken, candidate.id);
    updateCard(fresh);
    return fresh;
  }

  const totalItems = linkedRules.length + linkedTransactions.length;

  return (
    <div className="fixed inset-0 z-50 flex items-end sm:items-center justify-center" role="dialog" aria-modal="true" aria-label="Excluir cartão">
      <div
        className="absolute inset-0 bg-black/50 backdrop-blur-sm"
        onClick={!deleting ? onClose : undefined}
      />

      <div ref={panelRef} className="relative z-10 w-full max-w-md mx-auto rounded-t-3xl sm:rounded-3xl bg-paper border border-paper-line shadow-2xl max-h-[85vh] flex flex-col">
        <div className="flex items-start gap-3 px-5 pt-5 pb-4 border-b border-paper-line shrink-0">
          <div className="w-10 h-10 rounded-full bg-accent-neg/10 grid place-items-center shrink-0 text-accent-neg">
            <svg width="18" height="18" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round" strokeLinejoin="round">
              <polyline points="3 6 5 6 21 6"/>
              <path d="M19 6l-1 14a2 2 0 0 1-2 2H8a2 2 0 0 1-2-2L5 6"/>
              <path d="M10 11v6M14 11v6"/>
              <path d="M9 6V4a1 1 0 0 1 1-1h4a1 1 0 0 1 1 1v2"/>
            </svg>
          </div>
          <div className="flex-1 min-w-0">
            <div className="text-sm font-semibold text-ink">Excluir cartão</div>
            <div className="text-[12px] text-ink-muted mt-0.5">
              <span className="font-medium text-ink">{card.title}</span>
              {' '}· Fecha dia {String(card.closingDay).padStart(2, '0')} · Vence dia {String(card.dueDay).padStart(2, '0')}
            </div>
          </div>
          {!deleting && (
            <button
              onClick={onClose}
              className="w-11 h-11 rounded-full grid place-items-center text-ink-muted hover:bg-paper-line transition shrink-0"
              aria-label="Fechar"
            >
              <svg width="14" height="14" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round" strokeLinejoin="round">
                <line x1="18" y1="6" x2="6" y2="18"/><line x1="6" y1="6" x2="18" y2="18"/>
              </svg>
            </button>
          )}
        </div>

        <div className="flex-1 overflow-y-auto px-5 py-4 space-y-4">
          {totalItems === 0 ? (
            <div className="rounded-xl bg-paper-card border border-paper-line px-4 py-3 text-[12px] text-ink-muted">
              Nenhum lançamento ou regra fixa vinculada a este cartão será excluída.
            </div>
          ) : (
            <>
              <div className="rounded-xl bg-accent-neg/5 border border-accent-neg/20 px-4 py-3 text-[12px] text-accent-neg">
                Os itens abaixo serão excluídos permanentemente junto com o cartão.
              </div>

              {linkedRules.length > 0 && (
                <div>
                  <div className="text-[10px] uppercase tracking-wider text-ink-muted mb-2">
                    Regras fixas ({linkedRules.length})
                  </div>
                  <div className="space-y-1.5">
                    {linkedRules.map((rule) => (
                      <div
                        key={rule.id}
                        className="flex items-center gap-3 rounded-xl bg-paper-card border border-paper-line px-3 py-2.5"
                      >
                        <div className="text-ink-muted shrink-0">
                          <CategoryIcon category={rule.category} size={14} />
                        </div>
                        <div className="flex-1 min-w-0">
                          <div className="text-[12px] font-medium text-ink truncate">
                            {rule.description}
                          </div>
                          <div className="text-[11px] text-ink-muted">
                            {rule.active ? 'Ativa' : 'Pausada'}
                          </div>
                        </div>
                        <div className="text-[12px] font-medium text-ink tabular-nums shrink-0">
                          {formatBRL(rule.amount)}
                        </div>
                      </div>
                    ))}
                  </div>
                </div>
              )}

              {linkedTransactions.length > 0 && (
                <div>
                  <div className="text-[10px] uppercase tracking-wider text-ink-muted mb-2">
                    Lançamentos ({linkedTransactions.length})
                  </div>
                  <div className="space-y-1.5">
                    {linkedTransactions.slice().sort((a, b) => b.date.localeCompare(a.date)).map((tx) => (
                      <div
                        key={tx.id}
                        className="flex items-center gap-3 rounded-xl bg-paper-card border border-paper-line px-3 py-2.5"
                      >
                        <div className="text-ink-muted shrink-0">
                          <CategoryIcon category={tx.category} size={14} />
                        </div>
                        <div className="flex-1 min-w-0">
                          <div className="text-[12px] font-medium text-ink truncate">
                            {tx.description}
                          </div>
                          <div className="text-[11px] text-ink-muted">
                            {tx.date}
                            {tx.installments && (
                              <span> · {tx.installments.index}/{tx.installments.total}x</span>
                            )}
                          </div>
                        </div>
                        <div className="text-[12px] font-medium text-ink tabular-nums shrink-0">
                          {formatBRL(tx.amount)}
                        </div>
                      </div>
                    ))}
                  </div>
                  {linkedTransactions.length > 0 && (
                    <div className="mt-2 rounded-xl bg-paper-card border border-paper-line px-3 py-2 text-[11px] text-ink-muted">
                      Apenas lançamentos já carregados são listados. Lançamentos de meses não visitados também serão excluídos ao confirmar.
                    </div>
                  )}
                </div>
              )}
            </>
          )}

          {error && (
            <div role="alert" className="rounded-xl border border-accent-neg/30 bg-accent-neg/5 px-3 py-2 text-[12px] text-accent-neg">
              {error}
            </div>
          )}
        </div>

        <div className="flex gap-2 px-5 py-4 border-t border-paper-line shrink-0">
          <button
            onClick={onClose}
            disabled={deleting}
            className="flex-1 h-11 rounded-xl border border-paper-line text-sm font-medium text-ink hover:bg-paper-line disabled:opacity-40 transition"
          >
            Cancelar
          </button>
          <button
            onClick={() => void confirm()}
            disabled={deleting}
            className="flex-1 h-11 rounded-xl bg-accent-neg text-white text-sm font-medium disabled:opacity-50 transition hover:opacity-90"
          >
            {deleting ? 'Excluindo...' : 'Confirmar exclusão'}
          </button>
        </div>
      </div>
    </div>
  );
}
