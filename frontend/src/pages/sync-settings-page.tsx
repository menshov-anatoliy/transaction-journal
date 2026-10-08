import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
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
		<section className="flex flex-col gap-6 p-6">
			<h1 className="page-title">Синхронизация</h1>

			<section className="flex flex-col gap-3 rounded-lg border p-4">
				<h2 className="text-lg font-semibold">Подключение Bybit</h2>
				{settingsQuery.isPending && <p className="text-muted-foreground text-sm">чтение настроек подключения…</p>}
				{settingsQuery.isError && (
					<p className="text-destructive text-sm" role="alert">
						Настройки подключения недоступны: {settingsQuery.error.message}
					</p>
				)}
				{settings !== undefined && bybit !== undefined && (
					<>
						{bybit.isConfigured ? (
							<div className="grid grid-cols-1 gap-2 text-sm md:grid-cols-[220px_minmax(0,1fr)]">
								<span className="text-muted-foreground">API-ключ (read-only)</span>
								<span>{bybit.maskedApiKey}</span>
								<span className="text-muted-foreground">Аккаунт</span>
								<span>{bybit.accountDescription}</span>
								<span className="text-muted-foreground">Секрет</span>
								<span>{bybit.secretStorage}</span>
							</div>
						) : (
							<p className="text-sm" role="note">
								{bybit.setupHint}
							</p>
						)}
					</>
				)}
			</section>

			<section className="flex flex-col gap-3 rounded-lg border p-4">
				<h2 className="text-lg font-semibold">Синхронизация</h2>
				<div className="flex flex-wrap items-center gap-3">
					<Button size="sm" disabled={syncMutation.isPending} onClick={() => syncMutation.mutate()}>
						{syncMutation.isPending ? "Синхронизация выполняется…" : "Синхронизировать сейчас"}
					</Button>

					<label className="flex items-center gap-2 text-sm">
						<input
							type="checkbox"
							checked={settings?.backupBeforeSyncEnabled ?? true}
							disabled={settings === undefined || backupMutation.isPending}
							onChange={(event) => backupMutation.mutate(event.currentTarget.checked)}
						/>
						делать резервную копию перед синхронизацией
					</label>
				</div>

				{settings?.backupPolicyError !== null && settings?.backupPolicyError !== undefined && (
					<p className="text-destructive text-sm" role="alert">
						{settings.backupPolicyError}
					</p>
				)}

				{syncMutation.isError && (
					<p className="text-destructive text-sm" role="alert">
						Синхронизация прервана: {syncMutation.error.message}
					</p>
				)}
				{lastRunResult !== null && (
					<SyncRunResultNote summary={lastRunResult} />
				)}
			</section>

			<section className="flex flex-col gap-3 rounded-lg border p-4">
				<h2 className="text-lg font-semibold">Журнал синхронизаций</h2>
				{runsQuery.isPending && <p className="text-muted-foreground text-sm">чтение журнала запусков…</p>}
				{runsQuery.isError && (
					<p className="text-destructive text-sm" role="alert">
						Журнал синхронизаций недоступен: {runsQuery.error.message}
					</p>
				)}
				{runsQuery.data !== undefined && runsQuery.data.length === 0 && (
					<p className="text-muted-foreground text-sm">Запусков синхронизации пока нет.</p>
				)}
				{runsQuery.data !== undefined && runsQuery.data.length > 0 && (
					<div className="rounded-md border">
						{/* Плотность Body #8 (qA7CW): журнал запусков — ячейки 6/12, шрифт 11.5. */}
						{/* Traceability: change:reconcile-frontend-with-design/design#D6 */}
						{/* Traceability: openspec:ui/design-system#requirement-typography-matches-design-system */}
						<Table density="dense">
							<TableHeader>
								<TableRow>
									<TableHead>Время</TableHead>
									<TableHead>Режим</TableHead>
									<TableHead>Результат</TableHead>
									<TableHead>Статус</TableHead>
								</TableRow>
							</TableHeader>
							<TableBody>
								{runsQuery.data.map((run) => (
									<TableRow key={run.id}>
										<TableCell>{formatMoment(run.startedAt)}</TableCell>
										<TableCell>{run.mode === "backfill" ? "первичная загрузка" : "догрузка"}</TableCell>
										<TableCell>{formatRunResult(run)}</TableCell>
										<TableCell>{formatRunStatus(run.status)}</TableCell>
									</TableRow>
								))}
							</TableBody>
						</Table>
					</div>
				)}
				{runsQuery.data !== undefined && runsQuery.data.length > 0 && <SyncRunWarningsList run={runsQuery.data[0]} />}
			</section>

			<section className="flex flex-col gap-3 rounded-lg border border-destructive/40 p-4">
				<h2 className="text-lg font-semibold text-destructive">Опасная зона</h2>
				<p className="text-sm text-muted-foreground">
					Полный пересбор стирает все конструкции и строит их заново из сырья журнала. Перед пересбором автоматически
					делается резервная копия.
				</p>
				<div className="flex flex-wrap gap-3">
					<Button
						variant="destructive"
						size="sm"
						disabled={rebuildMutation.isPending}
						onClick={() => setRebuildConfirming(true)}
					>
						Собрать конструкции
					</Button>
					<Button
						variant="outline"
						size="sm"
						disabled={resetMutation.isPending}
						onClick={() => resetMutation.mutate("linear")}
					>
						Сбросить состояние linear
					</Button>
					<Button
						variant="outline"
						size="sm"
						disabled={resetMutation.isPending}
						onClick={() => resetMutation.mutate("option")}
					>
						Сбросить состояние option
					</Button>
				</div>

				{rebuildConfirming && (
					<div className="flex flex-col gap-2 rounded-md border border-destructive/50 p-3 text-sm">
						<p>
							Подтвердите пересбор: будут удалены конструкции, привязки сделок, капитал, комментарии, корректировки
							PnL и ручные пометки закрытия.
						</p>
						<div className="flex gap-2">
							<Button size="sm" variant="destructive" onClick={() => rebuildMutation.mutate()}>
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
			</section>
		</section>
	);
}

function SyncRunResultNote({ summary }: { summary: SyncRunSummary }) {
	return (
		<div className="text-sm" role="status">
			<p>
				Последний запуск: {summary.mode === "backfill" ? "первичная загрузка" : "догрузка"} — исполнений{" "}
				{summary.newExecutions}, delivery {summary.newDeliveries}, инструментов {summary.newInstruments}.
			</p>
			{summary.projectionError !== null && (
				<p className="text-destructive" role="alert">
					Проекция журнала не построена: {summary.projectionError}
				</p>
			)}
			{summary.reconciliationWarnings.length > 0 && (
				<ul className="list-disc pl-5">
					{summary.reconciliationWarnings.map((warning) => (
						<li key={`${warning.symbol}-${warning.deliveryTime}`}>
							{warning.symbol} ({formatMoment(warning.deliveryTime)}): расхождение {warning.difference}
						</li>
					))}
				</ul>
			)}
		</div>
	);
}

function SyncRunWarningsList({ run }: { run: SyncRunJournalEntry }) {
	const hasWarnings = run.skippedAreas.length > 0 || run.unresolvedInstruments.length > 0 || run.uncoveredBaseCoins.length > 0;
	if (hasWarnings == false) {
		return <p className="text-muted-foreground text-sm">Заметок по последнему запуску нет.</p>;
	}

	return (
		<div className="text-sm">
			<p className="font-medium">Заметки последнего запуска</p>
			{run.skippedAreas.length > 0 && <p>Пропущенные области: {run.skippedAreas.join(", ")}</p>}
			{run.unresolvedInstruments.length > 0 && <p>Неразрешённые инструменты: {run.unresolvedInstruments.join(", ")}</p>}
			{run.uncoveredBaseCoins.length > 0 && <p>Непокрытые базовые активы: {run.uncoveredBaseCoins.join(", ")}</p>}
		</div>
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
