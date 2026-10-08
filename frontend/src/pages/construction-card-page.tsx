import { useParams } from "react-router";
import { SectionPlaceholder } from "@/components/section-placeholder";

/** Карточка конструкции — маршрут из превью и «в новом окне»; наполнение в задаче 5.2. */
export function ConstructionCardPage() {
	const { constructionId } = useParams<{ constructionId: string }>();

	return (
		<SectionPlaceholder
			title="Карточка конструкции"
			description={`Полная карточка конструкции ${constructionId ?? ""} — сводка метрик, таблицы записей и действия — появится в задаче 5.2.`}
		/>
	);
}
