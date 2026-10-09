import { clsx, type ClassValue } from "clsx";
import { twMerge } from "tailwind-merge";

/** Склейка классов Tailwind: условия через clsx, конфликты решает tailwind-merge. */
export function cn(...inputs: ClassValue[]) {
	return twMerge(clsx(inputs));
}
