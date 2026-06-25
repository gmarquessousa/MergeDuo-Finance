import { useRefresh } from '../refreshContext';
import { useOfflineTransactions } from '../offlineTransactionsContext';
import { useOnlineStatus } from '../useOnlineStatus';

export function OfflineBanner() {
  const isOnline = useOnlineStatus();
  const refreshCtx = useRefresh();
  const offlineTransactions = useOfflineTransactions();

  if (isOnline) {
    return null;
  }

  return (
    <div className="mx-4 mt-3 rounded-2xl border border-amber-200 bg-amber-50 px-4 py-3 text-amber-950 shadow-soft sm:mx-5 md:mx-8 lg:mx-10">
      <div className="flex items-start justify-between gap-3">
        <div>
          <div className="text-xs font-semibold uppercase tracking-[0.14em] text-amber-700">
            Sem conexão
          </div>
          <p className="mt-1 text-sm leading-5">
            Mostrando os últimos dados salvos. Quando a conexão voltar, toque em atualizar para sincronizar.
          </p>
          {offlineTransactions && offlineTransactions.queuedCreates > 0 && (
            <p className="mt-2 text-xs font-medium text-amber-800">
              {offlineTransactions.queuedCreates} lançamento{offlineTransactions.queuedCreates > 1 ? 's' : ''} aguardando envio.
            </p>
          )}
        </div>
        <button
          type="button"
          onClick={() => refreshCtx?.refreshAll()}
          disabled={!refreshCtx || refreshCtx.refreshing}
          className="inline-flex h-9 shrink-0 items-center rounded-full border border-amber-300 bg-white px-3 text-xs font-semibold text-amber-900 transition hover:bg-amber-100 disabled:cursor-not-allowed disabled:opacity-60"
        >
          {refreshCtx?.refreshing ? 'Atualizando...' : 'Atualizar'}
        </button>
      </div>
    </div>
  );
}