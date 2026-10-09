import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { OctagonAlert, TriangleAlert } from "lucide-react";
import * as React from "react";
import { Button } from "@/components/ui/button";
import { Table, TableBody, TableCell, TableHead, TableHeader, TableRow } from "@/components/ui/table";
import { formatMoment } from "@/lib/format/display-time";
import {
	fetchSyncRuns,
	fetchSyncSettings,
	resetSyncCategory,
	runFullRebuild,
	runSync,
	setSyncBackupBeforeSync,
	type SyncRunJournalEntry,
	type SyncRunSummary,
} from "@/lib/api/sync";

// Раздел «Синхронизация» реализует наполнение маршрута /sync-settings:
// настройки и ручной запуск синка, журнал запусков, переключатель бэкапа,
// предупреждения сверки и заметки запусков, блок подключения Bybit с маской
// и инструкцией appsettings, а также опасную зону пересбора/сброса категории.
//
// Экран сведён с мастером Body #8 «Синхронизация» (qA7CW) design.pen по
// расхождениям §3.9 аудита: опасная зона (UOt6s) — заливка $negSoft/кайма
// $neg/радиус 12/[16,18]; предупреждения (tv63s) — пара $riskSoft/$risk;
// действия зоны — «Кнопка/Danger» с плотной заливкой $neg и белым текстом;
// журнал запусков (D91td) — плотность 6/12·11.5 с шапкой $surface2; секции —
// $surface/$border r12 [16,18]. Состав данных и колонок журнала доменный
// (Non-Goals change reconcile-frontend-with-design).
// Traceability: doc:.wf-research/ui-concept/concept.md#8-раздел-синхронизация-маршрут-sync-settings
// Traceability: doc:.wf-research/ui-concept/concept.md#12-карта-переноса-по-инвентаризации-129

const syncSettingsKey = ["sync-settings"] as const;
const syncRunsKey = ["sync-runs"] as const;

export function SyncSettingsPage() {
	const queryClient = useQueryClient();
	const [rebuildConfirming, setRebuildConfirming] = React.useState(false);
	const [lastRunResult, setLastRunResult] = React.useState<SyncRunSummary | null>(null);
	const [categoryStatus, setCategoryStatus] = React.useState<string | null>(null);

	const settingsQuery = useQuery({ queryKey: syncSettingsKey, queryFn: fetchSyncSettings });
	const runsQuery = useQuery({ queryKey: syncRunsKey, queryFn: () => fetchSyncRuns() });

	const backupMutation = useMutation({
		mutationFn: setSyncBackupBeforeSync,
		onSuccess: () => {
			void queryClient.invalidateQueries({ queryKey: syncSettingsKey });
		},
	});

	const syncMutation = useMutation({
		mutationFn: runSync,
		onSuccess: (result) => {
			setLastRunResult(result);
			void queryClient.invalidateQueries({ queryKey: syncRunsKey });
		},
	});

	const rebuildMutation = useMutation({
		mutationFn: runFullRebuild,
		onSuccess: () => {
			setRebuildConfirming(false);
		},
	});

	const resetMutation = useMutation({
		mutationFn: resetSyncCategory,
		onSuccess: (_, category) => {
			setCategoryStatus(`Состояние категории ${category} сброшено: следующий запуск выполнит backfill.`);
		},
	});

	const settings = settingsQuery.data;
	const bybit = settings?.bybit;

	return (
		<section className="flex flex-col gap-3.5 px-7 pt-5 pb-6">
			<h1 className="page-title">Синхронизация</h1>

			{/* Карточка подключения (Uf04R): $surface/$border, r12, [16,18], гэп 10. */}
			<section className="flex flex-col gap-2.5 rounded-lg border bg-surface px-[18px] py-4">
				{/* Шапка (lAyID): заголовок 13.5/600 + пилюля состояния «подключено» (oi3fT). */}
				<div className="flex items-center gap-2.5">
					<h2 className="text-[13.5px] font-semibold">Подключение Bybit</h2>
					{bybit?.isConfigured === true && (
						<span className="rounded-full bg-accent-soft px-2 py-[3px] text-[10.5px] leading-none text-accent-strong">
							подключено
						</span>
					)}
				</div>
				{settingsQuery.isPending && <p className="text-muted-foreground text-xs">чтение настроек подключения…</p>}
				{settingsQuery.isError && (
					<p className="text-destructive text-sm" role="alert">
						Настройки подключения недоступны: {settingsQuery.error.message}
					</p>
				)}
				{settings !== undefined && bybit !== undefined && (
					<>
						{bybit.isConfigured ? (
							/* Ряды kdgGn: ключ Inter 12 $textMuted, распорка, значение 12 $textPrimary. */
							<div className="flex flex-col gap-1.5 text-xs">
								<div className="flex items-center gap-3">
									<span className="text-text-muted">Ключ API</span>
									<span className="flex-1" aria-hidden="true" />
									<span className="text-text-primary">{bybit.maskedApiKey}</span>
								</div>
								<div className="flex items-center gap-3">
									<span className="text-text-muted">Аккаунт</span>
									<span className="flex-1" aria-hidden="true" />
									<span className="text-text-primary">{bybit.accountDescription}</span>
								</div>
								<div className="flex items-center gap-3">
									<span className="text-text-muted">Хранение секрета</span>
									<span className="flex-1" aria-hidden="true" />
									<span className="text-text-primary">{bybit.secretStorage}</span>
								</div>
							</div>
						) : (
							<p className="text-xs" role="note">
								{bybit.setupHint}
							</p>
						)}
					</>
				)}
			</section>

			{/* Карточка синхронизации (YmlIB): $surface/$border, r12, [16,18], гэп 12;
				журнал запусков и предупреждения живут внутри той же секции мастера. */}
			<section className="flex flex-col gap-3 rounded-lg border bg-surface px-[18px] py-4">
				<h2 className="text-[13.5px] font-semibold">Синхронизация</h2>
				<div className="flex flex-wrap items-center gap-3">
					{/* RunBtn мастера (Q2tXMN): инстанс Primary [8,14], кегль 12.5. */}
					<Button size="sm" className="px-3.5 text-[12.5px]" disabled={syncMutation.isPending} onClick={() => syncMutation.mutate()}>
						{syncMutation.isPending ? "Синхронизация выполняется…" : "Синхронизировать сейчас"}
					</Button>

					<label className="flex items-center gap-2 text-xs text-text-secondary">
						<input
							type="checkbox"
							checked={settings?.backupBeforeSyncEnabled ?? true}
							disabled={settings === undefined || backupMutation.isPending}
							onChange={(event) => backupMutation.mutate(event.currentTarget.checked)}
						/>
						резервная копия перед синхронизацией
					</label>
				</div>

				{settings?.backupPolicyError !== null && settings?.backupPolicyError !== undefined && (
					<p className="text-destructive text-xs" role="alert">
						{settings.backupPolicyError}
					</p>
				)}

				{syncMutation.isError && (
					<p className="text-destructive text-xs" role="alert">
						Синхронизация прервана: {syncMutation.error.message}
					</p>
				)}
				{lastRunResult !== null && (
					<SyncRunResultNote summary={lastRunResult} />
				)}

				{runsQuery.isPending && <p className="text-muted-foreground text-xs">чтение журнала запусков…</p>}
				{runsQuery.isError && (
					<p className="text-destructive text-xs" role="alert">
						Журнал синхронизаций недоступен: {runsQuery.error.message}
					</p>
				)}
				{runsQuery.data !== undefined && runsQuery.data.length === 0 && (
					<p className="text-muted-foreground text-xs">Запусков синхронизации пока нет.</p>
				)}
				{runsQuery.data !== undefined && runsQuery.data.length > 0 && (
					<div className="overflow-hidden rounded-[10px] border border-divider">
						{/* Плотность Body #8 (qA7CW/D91td): журнал запусков — ячейки 6/12, шрифт 11.5,
							шапка на $surface2, прерванный запуск подсвечен $neg. */}
						{/* Traceability: change:reconcile-frontend-with-design/design#D6 */}
						{/* Traceability: openspec:ui/design-system#requirement-typography-matches-design-system */}
						<Table density="dense">
							<TableHeader>
								<TableRow className="bg-surface-2 hover:bg-surface-2">
									<TableHead>Время</TableHead>
									<TableHead>Режим</TableHead>
									<TableHead>Результат</TableHead>
									<TableHead>Статус</TableHead>
								</TableRow>
							</TableHeader>
							<TableBody>
								{runsQuery.data.map((run) => (
									<TableRow key={run.id}>
										<TableCell className={run.status === "failed" ? "text-neg" : "text-text-secondary"}>
											{formatMoment(run.startedAt)}
										</TableCell>
										<TableCell className={run.status === "failed" ? "text-neg" : "text-text-secondary"}>
											{run.mode === "backfill" ? "первичная загрузка" : "догрузка"}
										</TableCell>
										<TableCell className={run.status === "failed" ? "text-neg" : "text-text-secondary"}>
											{formatRunResult(run)}
										</TableCell>
										<TableCell className={run.status === "failed" ? "text-neg" : "text-text-secondary"}>
											{formatRunStatus(run.status)}
										</TableCell>
									</TableRow>
								))}
							</TableBody>
						</Table>
					</div>
				)}
				{runsQuery.data !== undefined && runsQuery.data.length > 0 && <SyncRunWarningsList run={runsQuery.data[0]} />}
			</section>

			{/* Опасная зона (UOt6s): заливка $negSoft, кайма $neg, r12, [16,18], гэп 10;
				действия — инстансы «Кнопка/Danger» с плотной заливкой $neg (jmklC/EK3Ob). */}
			{/* Traceability: openspec:ui/design-system#requirement-visual-layer-uses-design-tokens */}
			{/* Traceability: change:reconcile-frontend-with-design/design#D2 */}
			<section className="flex flex-col gap-2.5 rounded-lg border border-neg bg-neg-soft px-[18px] py-4">
				{/* Шапка (EbnzH): OctagonAlert 16 $neg + заголовок Inter 13.5/600 $neg. */}
				<div className="flex items-center gap-2">
					<OctagonAlert className="size-4 text-neg" aria-hidden="true" />
					<h2 className="text-[13.5px] font-semibold text-neg">Опасная зона</h2>
				</div>

				{/* Row1 (Z0KQV): заголовок 12.5/600, описание 11.5/1.45, действие справа. */}
				<div className="flex flex-wrap items-center gap-3.5">
					<div className="flex min-w-48 flex-1 flex-col gap-[3px]">
						<p className="text-[12.5px] font-semibold">Собрать конструкции (полный пересбор)</p>
						<p className="text-[11.5px] leading-[1.45] text-text-secondary">
							Безвозвратно стирает: комментарии, ручные пометки, корректировки PnL, чаты привязанных конструкций.
							Автобэкап включён.
						</p>
					</div>
					<Button
						variant="destructive-solid"
						size="sm"
						className="px-3.5 text-[12px]"
						disabled={rebuildMutation.isPending}
						onClick={() => setRebuildConfirming(true)}
					>
						Собрать конструкции
					</Button>
				</div>

				{/* Row2 (cEu9l): сброс состояния категорий; категории доменные — две кнопки
					действия вместо одной «Сбросить» мастера. */}
				<div className="flex flex-wrap items-center gap-3.5">
					<div className="flex min-w-48 flex-1 flex-col gap-[3px]">
						<p className="text-[12.5px] font-semibold">Сброс состояния категорий linear/option</p>
						<p className="text-[11.5px] leading-[1.45] text-text-secondary">
							Возвращает разметку к исходной и запускает повторную сводку.
						</p>
					</div>
					<div className="flex gap-2">
						<Button
							variant="destructive-solid"
							size="sm"
							className="px-3.5 text-[12px]"
							disabled={resetMutation.isPending}
							onClick={() => resetMutation.mutate("linear")}
						>
							Сбросить linear
						</Button>
						<Button
							variant="destructive-solid"
							size="sm"
							className="px-3.5 text-[12px]"
							disabled={resetMutation.isPending}
							onClick={() => resetMutation.mutate("option")}
						>
							Сбросить option
						</Button>
					</div>
				</div>

				{rebuildConfirming && (
					<div className="flex flex-col gap-2 rounded-md border border-neg/50 p-3 text-sm">
						<p>
							Подтвердите пересбор: будут удалены конструкции, привязки сделок, капитал, комментарии, корректировки
							PnL и ручные пометки закрытия.
						</p>
						<div className="flex gap-2">
							<Button size="sm" variant="destructive-solid" className="px-3.5 text-[12px]" onClick={() => rebuildMutation.mutate()}>
								Подтвердить пересбор
							</Button>
							<Button size="sm" variant="outline" onClick={() => setRebuildConfirming(false)}>
								Отмена
							</Button>
						</div>
					</div>
				)}

				{rebuildMutation.isError && (
					<p className="text-destructive text-sm" role="alert">
						Пересбор не выполнен: {rebuildMutation.error.message}
					</p>
				)}
				{rebuildMutation.data !== undefined && (
					<p className="text-sm" role="status">
						Пересбор завершён: конструкций {rebuildMutation.data.constructionsCount}, привязано сделок{" "}
						{rebuildMutation.data.boundCount}, во «Входящих» {rebuildMutation.data.tradesInInbox}.
					</p>
				)}

				{resetMutation.isError && (
					<p className="text-destructive text-sm" role="alert">
						Сброс категории не выполнен: {resetMutation.error.message}
					</p>
				)}
				{categoryStatus !== null && (
					<p className="text-sm" role="status">
						{categoryStatus}
					</p>
				)}

				{/* Сноска LlmNote (fzke1): Inter 11/normal $textMuted. */}
				<p className="text-[11px] text-text-muted">
					Настройки LLM-провайдера не выводятся в UI — управление через appsettings.Local.json.
				</p>
			</section>
		</section>
	);
}

/*
	Блок предупреждения перенесён с ноды Warn (tv63s) мастера Body #8:
	заливка $riskSoft, радиус 9, паддинги [8,12], иконка TriangleAlert 14
	$risk и текст Inter 11.5/1.4 $risk — сверки экспираций и заметки запуска.
*/
// Traceability: openspec:ui/design-system#requirement-visual-layer-uses-design-tokens
// Traceability: change:reconcile-frontend-with-design/design#D2
function WarnBlock({ children }: { children: React.ReactNode }) {
	return (
		<div className="flex items-start gap-2 rounded-[9px] bg-risk-soft px-3 py-2 text-[11.5px] leading-[1.4] text-risk">
			<TriangleAlert className="mt-px size-3.5 shrink-0" aria-hidden="true" />
			<div className="min-w-0 flex-1">{children}</div>
		</div>
	);
}

function SyncRunResultNote({ summary }: { summary: SyncRunSummary }) {
	return (
		<div className="flex flex-col gap-2 text-xs" role="status">
			{/* Итог запуска по слотам LastT/LastD мастера: 12/600 + 12 $textSecondary. */}
			<p className="font-semibold">Результат запуска {formatMoment(summary.startedAt)}</p>
			<p className="text-text-secondary">
				режим {summary.mode === "backfill" ? "первичная загрузка" : "догрузка"} · исполнений {summary.newExecutions} ·
				delivery {summary.newDeliveries} · инструментов {summary.newInstruments}
			</p>
			{summary.projectionError !== null && (
				<p className="text-destructive" role="alert">
					Проекция журнала не построена: {summary.projectionError}
				</p>
			)}
			{/* Предупреждения сверки — блок Warn мастера (риск-пара, не neg). */}
			{summary.reconciliationWarnings.length > 0 && (
				<WarnBlock>
					{summary.reconciliationWarnings.map((warning) => (
						<p key={`${warning.symbol}-${warning.deliveryTime}`}>
							Сверка экспираций: {warning.symbol} ({formatMoment(warning.deliveryTime)}) — расхождение{" "}
							{warning.difference}.
						</p>
					))}
				</WarnBlock>
			)}
		</div>
	);
}

function SyncRunWarningsList({ run }: { run: SyncRunJournalEntry }) {
	const hasWarnings = run.skippedAreas.length > 0 || run.unresolvedInstruments.length > 0 || run.uncoveredBaseCoins.length > 0;
	if (hasWarnings == false) {
		return <p className="text-muted-foreground text-xs">Заметок по последнему запуску нет.</p>;
	}

	/* Заметки запуска собраны в единый блок Warn мастера (tv63s): строки
		пропусков/неразрешённых инструментов/непокрытых активов на паре risk. */
	return (
		<WarnBlock>
			{run.skippedAreas.length > 0 && <p>Пропущенные области: {run.skippedAreas.join(", ")}</p>}
			{run.unresolvedInstruments.length > 0 && <p>Неразрешённые инструменты: {run.unresolvedInstruments.join(", ")}</p>}
			{run.uncoveredBaseCoins.length > 0 && <p>Непокрытые базовые активы: {run.uncoveredBaseCoins.join(", ")}</p>}
		</WarnBlock>
	);
}

function formatRunResult(run: SyncRunJournalEntry) {
	if (run.status === "running") {
		return "выполняется…";
	}

	if (run.status === "failed") {
		return `прерван: ${run.error ?? "неизвестная ошибка"}`;
	}

	return `исполнений ${run.newExecutions} · delivery ${run.newDeliveries} · инструментов ${run.newInstruments}`;
}

function formatRunStatus(status: SyncRunJournalEntry["status"]) {
	switch (status) {
		case "running":
			return "выполняется";
		case "failed":
			return "ошибка";
		default:
			return "успех";
	}
}
