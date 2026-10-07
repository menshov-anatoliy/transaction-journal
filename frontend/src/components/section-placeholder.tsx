interface SectionPlaceholderProps {
	title: string;
	description: string;
}

/**
 * Заглушка раздела каркаса: имя и пояснение, без бизнес-функциональности.
 * Наполнение разделов — задачи 5.x переноса интерфейса.
 */
export function SectionPlaceholder({ title, description }: SectionPlaceholderProps) {
	return (
		<section className="p-8">
			<h1 className="text-2xl font-semibold tracking-tight">{title}</h1>
			<p className="mt-2 max-w-2xl text-sm leading-relaxed text-muted-foreground">
				{description}
			</p>
		</section>
	);
}
